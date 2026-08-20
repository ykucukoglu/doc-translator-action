using DocTranslator.Cli.Logging;
using DocTranslator.Cli.Options;
using DocTranslator.Core.Glossary;
using DocTranslator.Core.Models;
using DocTranslator.Core.Parsing;
using DocTranslator.Core.Provenance;
using DocTranslator.Core.Reconstruction;
using DocTranslator.GitHub.Cache;
using DocTranslator.GitHub.Diff;
using DocTranslator.GitHub.Operations;
using DocTranslator.LLM;
using DocTranslator.LLM.Providers;
using Microsoft.Extensions.FileSystemGlobbing;

namespace DocTranslator.Cli.Orchestration;

/// <summary>
/// Wires the whole pipeline together: Git Diff -&gt; AST Parse -&gt; LLM Translate -&gt; AST
/// Reconstruct -&gt; File Write -&gt; Open PR. Pure orchestration/wiring - all the actual logic
/// lives in Core/LLM/GitHub, which is why this type is allowed to live in Cli rather than Core.
/// </summary>
public sealed class TranslationOrchestrator(
    IGlossaryService glossaryService,
    IMarkdigParserService parserService,
    IAstReconstructor astReconstructor,
    ILlmProviderFactory llmProviderFactory,
    IGitDiffAnalyzer diffAnalyzer,
    ITranslationCache translationCache,
    IDriftDetector driftDetector,
    IGitHubService gitHubService,
    IPrSummaryBuilder prSummaryBuilder,
    IOutputPathResolver outputPathResolver,
    IConsoleSummaryWriter consoleSummaryWriter)
{
    public async Task<int> RunAsync(ActionOptions options, CancellationToken cancellationToken)
    {
        var glossary = glossaryService.Load(Path.Combine(options.RepositoryPath, options.GlossaryPath));
        var fullGlob = CombineGlob(options.SourcePath, options.IncludeGlob);

        var changedFiles = diffAnalyzer.GetChangedFiles(options.RepositoryPath, options.BaseBranch, fullGlob);
        var summary = new TranslationRunSummary();
        var filesToCommit = new Dictionary<string, string>();

        if (changedFiles.Count == 0)
        {
            Console.WriteLine("No matching documentation files changed - nothing to translate.");
        }
        else
        {
            await TranslateChangedFilesAsync(options, glossary, changedFiles, summary, filesToCommit, cancellationToken);
        }

        CollectDriftWarnings(options, summary);

        var pullRequestUrl = await PublishAsync(options, summary, filesToCommit, cancellationToken);

        consoleSummaryWriter.Write(summary, filesToCommit.Count, pullRequestUrl, options.DryRun);
        await WriteGitHubOutputsAsync(pullRequestUrl, filesToCommit.Count, summary.DriftWarnings.Count, cancellationToken);

        return options.FailOnStaleTranslations && summary.DriftWarnings.Count > 0 ? 1 : 0;
    }

    private async Task TranslateChangedFilesAsync(
        ActionOptions options,
        GlossaryContext glossary,
        IReadOnlyList<ChangedFile> changedFiles,
        TranslationRunSummary summary,
        Dictionary<string, string> filesToCommit,
        CancellationToken cancellationToken)
    {
        var llmService = llmProviderFactory.Create();
        var languageStats = options.TargetLanguages.ToDictionary(lang => lang, _ => (Translated: 0, Cached: 0));

        foreach (var changedFile in changedFiles)
        {
            var absoluteSourcePath = Path.Combine(options.RepositoryPath, changedFile.Path);
            var markdown = await File.ReadAllTextAsync(absoluteSourcePath, cancellationToken);
            var context = parserService.ParseAndExtractChunks(changedFile.Path, markdown);
            var sourceTextByChunkId = context.Chunks.ToDictionary(c => c.ChunkId, c => c.SourceText);
            var provenance = new TranslationProvenance(driftDetector.HashFile(absoluteSourcePath), changedFile.Path, string.Empty, DateTimeOffset.UtcNow);

            foreach (var targetLanguage in options.TargetLanguages)
            {
                var (translatedChunks, fromCache) = await TranslateWithCacheAsync(
                    llmService, context, targetLanguage, glossary, changedFile.Path, cancellationToken);

                foreach (var translated in translatedChunks)
                {
                    var warnings = glossaryService.Validate(sourceTextByChunkId[translated.ChunkId], translated.TranslatedText, glossary, targetLanguage);
                    foreach (var warning in warnings)
                    {
                        summary.GlossaryWarnings.Add($"{changedFile.Path} [{targetLanguage}]: {warning}");
                    }
                }

                var outputMarkdown = astReconstructor.Reconstruct(context, translatedChunks, provenance with { TargetLanguage = targetLanguage });
                var outputRelativePath = outputPathResolver.Resolve(options.OutputPathTemplate, targetLanguage, changedFile.Path);
                filesToCommit[outputRelativePath] = outputMarkdown;

                var (translatedSoFar, cachedSoFar) = languageStats[targetLanguage];
                languageStats[targetLanguage] = (translatedSoFar + translatedChunks.Count, cachedSoFar + fromCache);
            }
        }

        translationCache.Save();

        foreach (var (language, stats) in languageStats)
        {
            summary.Languages.Add(new LanguageSummary(language, stats.Translated, stats.Cached));
        }
    }

    private async Task<(IReadOnlyList<TranslatedChunk> Chunks, int FromCache)> TranslateWithCacheAsync(
        ILlmTranslationService llmService,
        DocumentTranslationContext context,
        string targetLanguage,
        GlossaryContext glossary,
        string sourceRelativePath,
        CancellationToken cancellationToken)
    {
        var result = new List<TranslatedChunk>(context.Chunks.Count);
        var misses = new List<TranslationChunk>();

        foreach (var chunk in context.Chunks)
        {
            var cached = translationCache.TryGet(sourceRelativePath, targetLanguage, chunk.ContentHash);
            if (cached is not null)
            {
                result.Add(new TranslatedChunk(chunk.ChunkId, cached));
            }
            else
            {
                misses.Add(chunk);
            }
        }

        var fromCache = result.Count;

        if (misses.Count > 0)
        {
            var translated = await llmService.TranslateAsync(misses, targetLanguage, glossary, cancellationToken);
            var missesById = misses.ToDictionary(c => c.ChunkId);

            foreach (var t in translated)
            {
                result.Add(t);
                translationCache.Set(sourceRelativePath, targetLanguage, missesById[t.ChunkId].ContentHash, t.TranslatedText);
            }
        }

        return (result, fromCache);
    }

    /// <summary>
    /// Scans every source file matching the glob (not just this run's changed files) and flags
    /// any existing translated output whose provenance header no longer matches its source's
    /// current hash - catches translations that fell out of sync from an earlier failed/skipped run.
    /// </summary>
    private void CollectDriftWarnings(ActionOptions options, TranslationRunSummary summary)
    {
        var sourceRoot = Path.Combine(options.RepositoryPath, options.SourcePath);
        if (!Directory.Exists(sourceRoot))
        {
            return;
        }

        var matcher = new Matcher();
        matcher.AddInclude(options.IncludeGlob);
        var matches = matcher.GetResultsInFullPath(sourceRoot);

        foreach (var absoluteSourcePath in matches)
        {
            var relativeSourcePath = Path.GetRelativePath(options.RepositoryPath, absoluteSourcePath).Replace('\\', '/');

            foreach (var targetLanguage in options.TargetLanguages)
            {
                var outputRelativePath = outputPathResolver.Resolve(options.OutputPathTemplate, targetLanguage, relativeSourcePath);
                var absoluteOutputPath = Path.Combine(options.RepositoryPath, outputRelativePath);

                if (!File.Exists(absoluteOutputPath))
                {
                    continue; // never translated yet - not "drift", just not done
                }

                var existingContent = File.ReadAllText(absoluteOutputPath);
                var result = driftDetector.CheckDrift(absoluteSourcePath, existingContent);

                if (result.IsStale)
                {
                    summary.DriftWarnings.Add($"{outputRelativePath} [{targetLanguage}]: {result.Reason}");
                }
            }
        }
    }

    private async Task<string?> PublishAsync(
        ActionOptions options,
        TranslationRunSummary summary,
        Dictionary<string, string> filesToCommit,
        CancellationToken cancellationToken)
    {
        if (filesToCommit.Count == 0)
        {
            return null;
        }

        if (options.DryRun)
        {
            foreach (var (relativePath, content) in filesToCommit)
            {
                var fullPath = Path.Combine(options.RepositoryPath, relativePath);
                var directory = Path.GetDirectoryName(fullPath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                await File.WriteAllTextAsync(fullPath, content, cancellationToken);
            }

            return null;
        }

        var (owner, repositoryName) = ResolveRepository();
        var shortSha = diffAnalyzer.GetHeadShortSha(options.RepositoryPath);
        var branchName = $"doc-translator/{shortSha}";

        var request = new GitHubPushRequest(
            RepositoryPath: options.RepositoryPath,
            Owner: owner,
            RepositoryName: repositoryName,
            BaseBranch: options.BaseBranch ?? "main",
            BranchName: branchName,
            FilesToCommit: filesToCommit,
            CommitMessage: "docs: automated translation update",
            PullRequestTitle: "docs: automated translation update",
            PullRequestBody: "Automated documentation translation generated by doc-translator-action.",
            SummaryComment: prSummaryBuilder.Build(summary),
            Token: options.GitHubToken);

        var outcome = await gitHubService.CommitAndOpenPullRequestAsync(request, cancellationToken);
        return outcome.Url;
    }

    private static (string Owner, string RepositoryName) ResolveRepository()
    {
        var repository = Environment.GetEnvironmentVariable("GITHUB_REPOSITORY");
        if (string.IsNullOrWhiteSpace(repository) || !repository.Contains('/'))
        {
            throw new InvalidOperationException(
                "GITHUB_REPOSITORY environment variable (owner/repo) is required to open a pull request. Use --dry-run for local runs.");
        }

        var parts = repository.Split('/', 2);
        return (parts[0], parts[1]);
    }

    private static async Task WriteGitHubOutputsAsync(string? pullRequestUrl, int translatedFilesCount, int staleTranslationsCount, CancellationToken cancellationToken)
    {
        var outputFile = Environment.GetEnvironmentVariable("GITHUB_OUTPUT");
        if (string.IsNullOrWhiteSpace(outputFile))
        {
            return; // not running inside GitHub Actions (e.g. local dev) - nothing to write
        }

        var lines = new[]
        {
            $"pr-url={pullRequestUrl}",
            $"translated-files-count={translatedFilesCount}",
            $"stale-translations-count={staleTranslationsCount}",
        };

        // GITHUB_OUTPUT, not the deprecated ::set-output workflow command.
        await File.AppendAllLinesAsync(outputFile, lines, cancellationToken);
    }

    private static string CombineGlob(string sourcePath, string includeGlob) =>
        $"{sourcePath.TrimEnd('/', '\\')}/{includeGlob.TrimStart('/', '\\')}";
}
