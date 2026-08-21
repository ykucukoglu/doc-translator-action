using DocTranslator.Cli.Logging;
using DocTranslator.Cli.Options;
using DocTranslator.Core.Glossary;
using DocTranslator.Core.Ignore;
using DocTranslator.Core.Models;
using DocTranslator.Core.Parsing;
using DocTranslator.Core.Provenance;
using DocTranslator.Core.Reconstruction;
using DocTranslator.GitHub.Cache;
using DocTranslator.GitHub.Diff;
using DocTranslator.GitHub.Operations;
using DocTranslator.LLM;
using DocTranslator.LLM.Batching;
using DocTranslator.LLM.Providers;
using Microsoft.Extensions.FileSystemGlobbing;
using System.Text.RegularExpressions;

namespace DocTranslator.Cli.Orchestration;

/// <summary>
/// Wires the whole pipeline together: Git Diff -&gt; AST Parse -&gt; LLM Translate -&gt; AST
/// Reconstruct -&gt; File Write -&gt; Open PR. Pure orchestration/wiring - all the actual logic
/// lives in Core/LLM/GitHub, which is why this type is allowed to live in Cli rather than Core.
/// </summary>
public sealed class TranslationOrchestrator(
    IGlossaryService glossaryService,
    IDocIgnoreService docIgnoreService,
    IMarkdigParserService parserService,
    IAstReconstructor astReconstructor,
    ILlmProviderFactory llmProviderFactory,
    IGitDiffAnalyzer diffAnalyzer,
    ITranslationCache translationCache,
    IDriftDetector driftDetector,
    IGitHubService gitHubService,
    IPrSummaryBuilder prSummaryBuilder,
    IOutputPathResolver outputPathResolver,
    IConsoleSummaryWriter consoleSummaryWriter,
    IJobSummaryWriter jobSummaryWriter,
    IGitHubActionsLog log)
{
    private const int MaxPreviewParagraphsPerFile = 3;
    private const int MaxPreviewEntries = 5;

    public async Task<int> RunAsync(ActionOptions options, CancellationToken cancellationToken)
    {
        var glossary = glossaryService.Load(Path.Combine(options.RepositoryPath, options.GlossaryPath));
        var docIgnoreFilter = docIgnoreService.Load(Path.Combine(options.RepositoryPath, ".doc-ignore"));
        var fullGlob = CombineGlob(options.SourcePath, options.IncludeGlob);

        var changedFiles = diffAnalyzer.GetChangedFiles(options.RepositoryPath, options.BaseBranch, fullGlob, docIgnoreFilter);
        var workItems = changedFiles.Select(f => new FileWorkItem(f, options.TargetLanguages)).ToList();

        // Shared by backfill (finds files/languages with no translation yet) and drift detection
        // (checks every existing translation for staleness) - computed once up front and reused by
        // whichever of those actually run this call, instead of each independently re-walking the
        // source tree. Skipped entirely when neither will run (estimate-only with backfill off).
        var needsFullSourceScan = options.BackfillMissingTranslations || !options.EstimateCostOnly;
        var allSourceFiles = needsFullSourceScan ? ScanAllSourceFiles(options, docIgnoreFilter).ToList() : [];

        if (options.BackfillMissingTranslations)
        {
            AddBackfillWorkItems(options, allSourceFiles, changedFiles, workItems);
        }

        if (options.EstimateCostOnly)
        {
            await WriteCostEstimateAsync(options, workItems, cancellationToken);
            return 0;
        }

        var summary = new TranslationRunSummary();
        var filesToCommit = new Dictionary<string, string>();

        if (workItems.Count == 0)
        {
            Console.WriteLine("No matching documentation files changed - nothing to translate.");
        }
        else
        {
            await TranslateChangedFilesAsync(options, glossary, workItems, summary, filesToCommit, cancellationToken);
        }

        CollectDriftWarnings(options, allSourceFiles, summary);

        var prOutcome = await PublishAsync(options, summary, filesToCommit, cancellationToken);
        var pullRequestUrl = prOutcome?.Url;

        consoleSummaryWriter.Write(summary, filesToCommit.Count, pullRequestUrl, options.DryRun);
        await jobSummaryWriter.WriteAsync(summary, filesToCommit.Count, pullRequestUrl, options.DryRun, cancellationToken);
        await WriteGitHubOutputsAsync(prOutcome, filesToCommit.Count, summary.DriftWarnings.Count, cancellationToken);

        return options.FailOnStaleTranslations && summary.DriftWarnings.Count > 0 ? 1 : 0;
    }

    /// <summary>One source file plus the specific target languages to translate it into - normally
    /// every configured language, but a backfill-only work item (see <see cref="AddBackfillWorkItems"/>)
    /// carries just the languages that don't have an output yet, so a file that's already fully
    /// translated in every language except one newly-added target doesn't get needlessly
    /// re-translated into the languages it already has.</summary>
    private readonly record struct FileWorkItem(ChangedFile File, IReadOnlyList<string> Languages);

    private async Task TranslateChangedFilesAsync(
        ActionOptions options,
        GlossaryContext glossary,
        IReadOnlyList<FileWorkItem> workItems,
        TranslationRunSummary summary,
        Dictionary<string, string> filesToCommit,
        CancellationToken cancellationToken)
    {
        using var llmService = llmProviderFactory.Create();
        var languageStats = options.TargetLanguages.ToDictionary(lang => lang, _ => (Translated: 0, Cached: 0));

        foreach (var (changedFile, languagesForFile) in workItems)
        {
            var absoluteSourcePath = Path.Combine(options.RepositoryPath, changedFile.Path);
            var markdown = await File.ReadAllTextAsync(absoluteSourcePath, cancellationToken);

            // A previously-generated translation can itself land back inside source-path/
            // include-glob (e.g. output-path-template co-locating "guide.tr.md" next to
            // "guide.md", or the include-glob simply not excluding the {lang} subfolder a
            // directory-based template writes into) - without this check, a later run would treat
            // its own output as new source and re-translate already-translated text. Every
            // generated file starts with this provenance header (see ToHeaderComment), so it's a
            // reliable, template-agnostic "we wrote this" signal to skip on regardless of naming.
            if (IsGeneratedOutput(markdown))
            {
                continue;
            }

            using var group = log.BeginGroup($"Translate {changedFile.Path}");

            var context = parserService.ParseAndExtractChunks(changedFile.Path, markdown, options.TranslateMermaidDiagrams);
            var sourceTextByChunkId = context.Chunks.ToDictionary(c => c.ChunkId, c => c.SourceText);
            var provenance = new TranslationProvenance(driftDetector.HashFile(absoluteSourcePath), changedFile.Path, string.Empty, DateTimeOffset.UtcNow);

            // Counted once per file, not per target language - these placeholders are the same
            // regardless of which language this file is translated into, so counting them again
            // for every language would inflate the "N preserved" figure by a factor of language count.
            summary.PreservedCodeBlocks += context.CodeBlockCount;
            foreach (var chunk in context.Chunks)
            {
                summary.PreservedInlineCode += CountOccurrences(chunk.SourceText, "⟦CODE");
                summary.PreservedLinks += CountOccurrences(chunk.SourceText, "⟦AUTOLINK") + CountOccurrences(chunk.SourceText, "<link");
            }

            foreach (var targetLanguage in languagesForFile)
            {
                var (translatedChunks, fromCache) = await TranslateWithCacheAsync(
                    llmService, context, targetLanguage, glossary, changedFile.Path, options.SourceLanguage, cancellationToken);

                foreach (var translated in translatedChunks)
                {
                    var sourceText = sourceTextByChunkId[translated.ChunkId];
                    var warnings = glossaryService.Validate(sourceText, translated.TranslatedText, glossary, targetLanguage);
                    foreach (var warning in warnings)
                    {
                        summary.GlossaryWarnings.Add($"{changedFile.Path} [{targetLanguage}]: {warning}");
                        log.LogWarning($"[{targetLanguage}] {warning}", changedFile.Path);
                    }

                    summary.PreservedGlossaryTerms.UnionWith(glossaryService.FindPreservedTerms(sourceText, translated.TranslatedText, glossary));
                }

                if (summary.Previews.Count < MaxPreviewEntries)
                {
                    var paragraphs = context.Chunks
                        .Take(MaxPreviewParagraphsPerFile)
                        .Select(c => translatedChunks.FirstOrDefault(t => t.ChunkId == c.ChunkId))
                        .Where(t => t is not null)
                        .Select(t => (Original: CleanForPreview(sourceTextByChunkId[t!.ChunkId]), Translated: CleanForPreview(t!.TranslatedText)))
                        .ToList();

                    if (paragraphs.Count > 0)
                    {
                        summary.Previews.Add(new TranslationPreview(changedFile.Path, targetLanguage, paragraphs));
                    }
                }

                // Self-healing: a chunk whose translation dropped a required placeholder/tag
                // marker is re-translated (via llmService.RepairChunkAsync) up to 2 times before
                // falling back to leaving just that paragraph untranslated - see
                // AstReconstructor.ReconstructAsync. Defensive: any other unexpected failure here
                // skips just this file/language pair rather than aborting the whole run.
                ReconstructionOutcome outcome;
                try
                {
                    outcome = await astReconstructor.ReconstructAsync(
                        context,
                        translatedChunks,
                        (chunk, previousText, missing, ct) => llmService.RepairChunkAsync(chunk, previousText, missing, targetLanguage, glossary, ct, options.SourceLanguage),
                        provenance with { TargetLanguage = targetLanguage },
                        cancellationToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    summary.Errors.Add($"{changedFile.Path} [{targetLanguage}]: unexpected reconstruction failure - {ex.Message}");
                    log.LogError($"[{targetLanguage}] Unexpected reconstruction failure: {ex.Message}", changedFile.Path);
                    continue;
                }

                foreach (var chunkId in outcome.RepairedChunkIds)
                {
                    summary.SelfHealedChunks.Add($"{changedFile.Path} [{targetLanguage}] chunk {chunkId}");
                }

                foreach (var chunkId in outcome.UnrecoverableChunkIds)
                {
                    var message = $"{changedFile.Path} [{targetLanguage}] chunk {chunkId}: kept dropping required markers after repair attempts; left untranslated.";
                    summary.UnrecoverableChunks.Add(message);
                    log.LogWarning(message, changedFile.Path);
                }

                var outputRelativePath = outputPathResolver.Resolve(
                    options.OutputPathTemplate, targetLanguage, ToSourceRelativePath(changedFile.Path, options.SourcePath));
                filesToCommit[outputRelativePath] = outcome.Markdown;

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
        string sourceLanguage,
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
            var translated = await llmService.TranslateAsync(misses, targetLanguage, glossary, cancellationToken, sourceLanguage);
            var missesById = misses.ToDictionary(c => c.ChunkId);

            foreach (var t in translated)
            {
                result.Add(t);
                translationCache.SetTranslation(sourceRelativePath, targetLanguage, missesById[t.ChunkId].ContentHash, t.TranslatedText);
            }
        }

        return (result, fromCache);
    }

    /// <summary>
    /// Every source file matching source-path/include-glob, minus .doc-ignore exclusions - the
    /// full document set, independent of this run's git diff. Shared by drift detection (checks
    /// every existing translation for staleness, not just changed files) and backfill (finds
    /// files/languages with no translation at all yet).
    /// </summary>
    private static IEnumerable<string> ScanAllSourceFiles(ActionOptions options, IDocIgnoreFilter docIgnoreFilter)
    {
        var sourceRoot = Path.Combine(options.RepositoryPath, options.SourcePath);
        if (!Directory.Exists(sourceRoot))
        {
            yield break;
        }

        var matcher = new Matcher();
        matcher.AddInclude(options.IncludeGlob);

        foreach (var absoluteSourcePath in matcher.GetResultsInFullPath(sourceRoot))
        {
            var relativeSourcePath = Path.GetRelativePath(options.RepositoryPath, absoluteSourcePath).Replace('\\', '/');

            if (!docIgnoreFilter.IsIgnored(relativeSourcePath))
            {
                yield return relativeSourcePath;
            }
        }
    }

    /// <summary>
    /// Diff-only translation never picks up a repository's pre-existing docs - nothing in them
    /// "changed" on the run that first adds this action, or on the run after a new target
    /// language is added. When backfill-missing-translations is on, this adds one work item per
    /// (file, missing languages) pair found with no existing output yet, skipping any file this
    /// run's diff already covers (which already gets every configured language) and any file
    /// that is itself a prior translation output (same provenance-header check as the main loop).
    /// </summary>
    private void AddBackfillWorkItems(
        ActionOptions options,
        IReadOnlyList<string> allSourceFiles,
        IReadOnlyList<ChangedFile> changedFiles,
        List<FileWorkItem> workItems)
    {
        var changedPaths = changedFiles.Select(f => f.Path).ToHashSet(StringComparer.Ordinal);

        foreach (var relativeSourcePath in allSourceFiles)
        {
            if (changedPaths.Contains(relativeSourcePath))
            {
                continue; // already a full work item (every language) from the diff
            }

            var absoluteSourcePath = Path.Combine(options.RepositoryPath, relativeSourcePath);
            // Bounded read, not the whole file: the provenance header is always within the first
            // handful of lines (immediately after an optional frontmatter block), and this runs
            // across every source file in a full-tree scan.
            var leadingLines = string.Join('\n', File.ReadLines(absoluteSourcePath).Take(20));
            if (IsGeneratedOutput(leadingLines))
            {
                continue; // this file is itself a previously-generated translation, not a source
            }

            var missingLanguages = options.TargetLanguages
                .Where(lang => !File.Exists(Path.Combine(
                    options.RepositoryPath,
                    outputPathResolver.Resolve(options.OutputPathTemplate, lang, ToSourceRelativePath(relativeSourcePath, options.SourcePath)))))
                .ToList();

            if (missingLanguages.Count > 0)
            {
                workItems.Add(new FileWorkItem(new ChangedFile(relativeSourcePath, FileChangeKind.Modified), missingLanguages));
            }
        }
    }

    /// <summary>
    /// Parses every work item's chunks (without calling any LLM) and reports an estimated
    /// input-token count, skipping chunk/language pairs already in the translation cache.
    /// </summary>
    private async Task WriteCostEstimateAsync(ActionOptions options, IReadOnlyList<FileWorkItem> workItems, CancellationToken cancellationToken)
    {
        var totalPairs = 0;
        var cachedPairs = 0;
        var estimatedTokens = 0;

        foreach (var (file, languages) in workItems)
        {
            var absoluteSourcePath = Path.Combine(options.RepositoryPath, file.Path);
            var markdown = await File.ReadAllTextAsync(absoluteSourcePath, cancellationToken);
            if (IsGeneratedOutput(markdown))
            {
                continue;
            }

            var context = parserService.ParseAndExtractChunks(file.Path, markdown, options.TranslateMermaidDiagrams);

            foreach (var chunk in context.Chunks)
            {
                foreach (var targetLanguage in languages)
                {
                    totalPairs++;

                    if (translationCache.TryGet(file.Path, targetLanguage, chunk.ContentHash) is not null)
                    {
                        cachedPairs++;
                        continue;
                    }

                    estimatedTokens += ChunkBatcher.EstimateTokens(chunk.SourceText);
                }
            }
        }

        var estimate = new CostEstimate(workItems.Count, totalPairs, cachedPairs, estimatedTokens);
        consoleSummaryWriter.WriteCostEstimate(estimate);
        await jobSummaryWriter.WriteCostEstimateAsync(estimate, cancellationToken);
    }

    /// <summary>Every generated file carries this header (see <see cref="TranslationProvenance.ToHeaderComment"/>, and <see cref="IDriftDetector.TryParseHeader"/> for where it's expected relative to a leading frontmatter block) - a reliable, output-path-template-agnostic "we wrote this" signal, used everywhere this orchestrator needs to tell a real source apart from its own prior output.</summary>
    private bool IsGeneratedOutput(string text) => driftDetector.TryParseHeader(text) is not null;

    private static int CountOccurrences(string text, string marker)
    {
        var count = 0;
        for (var index = text.IndexOf(marker, StringComparison.Ordinal); index >= 0; index = text.IndexOf(marker, index + marker.Length, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }

    /// <summary>
    /// Cosmetic cleanup for the PR summary's side-by-side preview, not structural parsing: paired
    /// tags (&lt;em0&gt;, &lt;link1&gt;, ...) are stripped down to their inner text, and atomic
    /// placeholders (code spans, autolinks, raw HTML, line breaks - whose real content isn't
    /// available here) are shown as a plain ellipsis, so a reviewer sees readable prose instead of
    /// this codebase's internal marker syntax.
    /// </summary>
    private static string CleanForPreview(string encodedText) =>
        Regex.Replace(Regex.Replace(encodedText, @"</?(?:em|strong|link)\d+>", string.Empty), @"⟦[A-Z]+\d+⟧", "…").Trim();

    /// <summary>
    /// Scans every source file matching the glob (not just this run's changed files) and flags
    /// any existing translated output whose provenance header no longer matches its source's
    /// current hash - catches translations that fell out of sync from an earlier failed/skipped run.
    /// </summary>
    private void CollectDriftWarnings(ActionOptions options, IReadOnlyList<string> allSourceFiles, TranslationRunSummary summary)
    {
        foreach (var relativeSourcePath in allSourceFiles)
        {
            var absoluteSourcePath = Path.Combine(options.RepositoryPath, relativeSourcePath);

            foreach (var targetLanguage in options.TargetLanguages)
            {
                var outputRelativePath = outputPathResolver.Resolve(
                    options.OutputPathTemplate, targetLanguage, ToSourceRelativePath(relativeSourcePath, options.SourcePath));
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

    /// <summary>
    /// IOutputPathResolver's {relativePath} placeholder is documented as relative to source-path
    /// (e.g. "getting-started.md"), but every caller here only has a repo-root-relative path
    /// (e.g. "docs/getting-started.md" - git diff output and Matcher.GetResultsInFullPath both
    /// naturally produce repo-relative paths, not source-path-relative ones). Passing that
    /// straight through duplicated source-path into the resolved output, e.g.
    /// "docs/{lang}/{relativePath}" resolving to "docs/tr/docs/getting-started.md" instead of
    /// "docs/tr/getting-started.md". Strips the source-path prefix so callers satisfy the
    /// resolver's actual contract.
    /// </summary>
    private static string ToSourceRelativePath(string repoRelativePath, string sourcePath)
    {
        var normalizedRepoPath = repoRelativePath.Replace('\\', '/').TrimStart('/');
        var normalizedSourcePath = sourcePath.Replace('\\', '/').Trim('/');

        if (normalizedSourcePath.Length == 0 || normalizedSourcePath == ".")
        {
            return normalizedRepoPath;
        }

        var prefix = normalizedSourcePath + "/";
        return normalizedRepoPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? normalizedRepoPath[prefix.Length..]
            : normalizedRepoPath;
    }

    private async Task<PullRequestOutcome?> PublishAsync(
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
            Token: options.GitHubToken,
            CleanupStaleBranches: options.CleanupStaleBranches);

        return await gitHubService.CommitAndOpenPullRequestAsync(request, cancellationToken);
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

    private static async Task WriteGitHubOutputsAsync(PullRequestOutcome? prOutcome, int translatedFilesCount, int staleTranslationsCount, CancellationToken cancellationToken)
    {
        var outputFile = Environment.GetEnvironmentVariable("GITHUB_OUTPUT");
        if (string.IsNullOrWhiteSpace(outputFile))
        {
            return; // not running inside GitHub Actions (e.g. local dev) - nothing to write
        }

        var lines = new[]
        {
            $"pr-url={prOutcome?.Url}",
            $"pr-was-created={(prOutcome?.WasCreated ?? false).ToString().ToLowerInvariant()}",
            $"translated-files-count={translatedFilesCount}",
            $"stale-translations-count={staleTranslationsCount}",
        };

        // GITHUB_OUTPUT, not the deprecated ::set-output workflow command.
        await File.AppendAllLinesAsync(outputFile, lines, cancellationToken);
    }

    private static string CombineGlob(string sourcePath, string includeGlob) =>
        $"{sourcePath.TrimEnd('/', '\\')}/{includeGlob.TrimStart('/', '\\')}";
}
