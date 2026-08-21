using DocTranslator.Cli.Logging;
using DocTranslator.Cli.Options;
using DocTranslator.Cli.Orchestration;
using DocTranslator.Core.Glossary;
using DocTranslator.Core.Ignore;
using DocTranslator.Core.Models;
using DocTranslator.Core.Parsing;
using DocTranslator.Core.Provenance;
using DocTranslator.Core.Reconstruction;
using DocTranslator.Core.Telemetry;
using DocTranslator.GitHub.Cache;
using DocTranslator.GitHub.Diff;
using DocTranslator.GitHub.Operations;
using DocTranslator.LLM;
using DocTranslator.LLM.Providers;
using FluentAssertions;
using LibGit2Sharp;

namespace DocTranslator.Cli.Tests;

/// <summary>
/// End-to-end orchestrator tests against a real throwaway git repo (LibGit2Sharp), with only the
/// two network-touching seams faked: the LLM call (<see cref="FakeTranslationService"/>, already
/// shipped for local smoke testing) and the GitHub PR API (<see cref="StubGitHubService"/> below).
/// Everything else - diff analysis, AST parse/reconstruct, glossary, .doc-ignore, output path
/// resolution, the translation cache - runs for real. This is deliberate: every one of the real
/// production bugs found during dogfooding (output paths duplicating source-path, a prior
/// translation being picked back up as new source, an empty GITHUB_BASE_REF crashing PR creation)
/// lived in exactly this wiring, and `dotnet test` was green through all of them because nothing
/// exercised it end to end - only a live GitHub Actions run ever did.
/// </summary>
public sealed class TranslationOrchestratorTests : IDisposable
{
    private readonly string _repoRoot = Path.Combine(Path.GetTempPath(), "doc-translator-tests-" + Guid.NewGuid().ToString("N"));

    public TranslationOrchestratorTests()
    {
        Repository.Init(_repoRoot);
    }

    public void Dispose()
    {
        try
        {
            // LibGit2Sharp/Windows can leave file handles briefly open on the .git folder.
            Directory.Delete(_repoRoot, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    [Fact]
    public async Task RunAsync_DefaultOutputTemplate_DoesNotDuplicateSourcePathSegment()
    {
        WriteAndCommit("docs/guide.md", "# Hello\n\nSome text.\n", "base");
        WriteAndCommit("docs/guide.md", "# Hello\n\nSome revised text.\n", "revise");

        var orchestrator = BuildOrchestrator(out _, out _);
        var options = BuildOptions(targetLanguages: ["tr"]);

        var exitCode = await orchestrator.RunAsync(options, CancellationToken.None);

        exitCode.Should().Be(0);
        File.Exists(Path.Combine(_repoRoot, "docs", "tr", "guide.md")).Should().BeTrue(
            "the output should land at docs/tr/guide.md");
        File.Exists(Path.Combine(_repoRoot, "docs", "tr", "docs", "guide.md")).Should().BeFalse(
            "source-path must not be duplicated into the resolved {relativePath}");
    }

    [Fact]
    public async Task RunAsync_ChangedFileIncludesItsOwnPriorTranslation_OnlyTranslatesTheRealSource()
    {
        WriteAndCommit("docs/guide.md", "# Hello\n\nSome text.\n", "base");
        WriteAndCommit(
            "docs/tr/guide.md",
            "<!-- doc-translator: source-hash=abc; source-path=docs/guide.md; target-lang=tr; generated=2026-01-01T00:00:00Z -->\n# Merhaba\n",
            "seed prior translation");

        // Both the real source and its own existing translation change in the SAME commit - the
        // scenario that actually happened once this repo's docs/getting-started.md translation
        // was merged and then touched again in the same dogfooding push. The diff analyzer (with
        // no explicit base ref) only diffs HEAD against its single parent, so both writes must
        // land in one commit for the test to see what a real push event sees.
        File.WriteAllText(Path.Combine(_repoRoot, "docs", "guide.md"), "# Hello\n\nSome revised text.\n");
        File.WriteAllText(
            Path.Combine(_repoRoot, "docs", "tr", "guide.md"),
            "<!-- doc-translator: source-hash=abc; source-path=docs/guide.md; target-lang=tr; generated=2026-01-01T00:00:00Z -->\n# Merhaba revised\n");
        using (var repo = new Repository(_repoRoot))
        {
            Commands.Stage(repo, "docs/guide.md");
            Commands.Stage(repo, "docs/tr/guide.md");
            var sig = new Signature("test", "test@test.com", DateTimeOffset.Now);
            repo.Commit("revise source and touch its translation output", sig, sig);
        }

        var orchestrator = BuildOrchestrator(out var translateCalls, out _);
        var options = BuildOptions(targetLanguages: ["tr"]);

        await orchestrator.RunAsync(options, CancellationToken.None);

        translateCalls.Distinct().Should().Equal(
            ["docs/guide.md"],
            "only docs/guide.md should have been sent to the LLM - docs/tr/guide.md carries the " +
            "provenance header and must never be re-translated as if it were new source");
    }

    [Fact]
    public async Task RunAsync_BackfillOff_PreExistingUntouchedDocsAreNeverTranslated()
    {
        // The scenario a first-time adopter hits: they add the action to a repository whose docs/
        // already existed for a while and never change again in this run's diff. Without opting
        // in to backfill, that pre-existing content should never get auto-translated.
        WriteAndCommit("docs/guide.md", "# Hello\n\nSome text.\n", "base");
        WriteAndCommit("README.md", "unrelated change", "unrelated");

        var orchestrator = BuildOrchestrator(out var translateCalls, out _);
        var options = BuildOptions(targetLanguages: ["tr"], backfillMissingTranslations: false);

        await orchestrator.RunAsync(options, CancellationToken.None);

        translateCalls.Should().BeEmpty();
    }

    [Fact]
    public async Task RunAsync_BackfillOn_TranslatesPreExistingDocsWithNoOutputYet()
    {
        WriteAndCommit("docs/guide.md", "# Hello\n\nSome text.\n", "base");
        WriteAndCommit("README.md", "unrelated change", "unrelated");

        var orchestrator = BuildOrchestrator(out var translateCalls, out _);
        var options = BuildOptions(targetLanguages: ["tr"], backfillMissingTranslations: true);

        await orchestrator.RunAsync(options, CancellationToken.None);

        translateCalls.Distinct().Should().Equal(["docs/guide.md"]);
        File.Exists(Path.Combine(_repoRoot, "docs", "tr", "guide.md")).Should().BeTrue();
    }

    [Fact]
    public async Task RunAsync_EstimateCostOnly_NeverCallsLlmOrWritesFiles()
    {
        WriteAndCommit("docs/guide.md", "# Hello\n\nSome text.\n", "base");
        WriteAndCommit("docs/guide.md", "# Hello\n\nSome revised text.\n", "revise");

        var orchestrator = BuildOrchestrator(out var translateCalls, out var gitHubService);
        var options = BuildOptions(targetLanguages: ["tr"], estimateCostOnly: true);

        var exitCode = await orchestrator.RunAsync(options, CancellationToken.None);

        exitCode.Should().Be(0);
        translateCalls.Should().BeEmpty();
        gitHubService.LastRequest.Should().BeNull();
        File.Exists(Path.Combine(_repoRoot, "docs", "tr", "guide.md")).Should().BeFalse();
    }

    [Fact]
    public async Task RunAsync_EstimateCostOnly_PrintsChunkAndTokenCounts()
    {
        WriteAndCommit("docs/guide.md", "# Hello\n\nSome text.\n", "base");
        WriteAndCommit("docs/guide.md", "# Hello\n\nSome revised text.\n", "revise");

        var orchestrator = BuildOrchestrator(out _, out _);
        var options = BuildOptions(targetLanguages: ["tr"], estimateCostOnly: true);

        var originalOut = Console.Out;
        using var writer = new StringWriter();
        Console.SetOut(writer);
        try
        {
            await orchestrator.RunAsync(options, CancellationToken.None);
        }
        finally
        {
            Console.SetOut(originalOut);
        }

        var output = writer.ToString();
        output.Should().Contain("cost estimate");
        output.Should().Contain("Estimated input tokens");
    }

    [Fact]
    public async Task RunAsync_BackfillOn_OnlyFillsMissingLanguagesNotAlreadyTranslatedOnes()
    {
        // Simulates adding a new target language later: docs/guide.md already has a "de"
        // translation (with its provenance header), and the user now also wants "tr". Backfill
        // should translate only the missing "tr" output, not re-translate "de" too. A second,
        // unrelated commit is required so the diff analyzer doesn't treat this repo's very first
        // commit as "everything added" (a null base tree diffs every file as new).
        WriteAndCommit("docs/guide.md", "# Hello\n\nSome text.\n", "base");
        Directory.CreateDirectory(Path.Combine(_repoRoot, "docs", "de"));
        File.WriteAllText(
            Path.Combine(_repoRoot, "docs", "de", "guide.md"),
            "<!-- doc-translator: source-hash=abc; source-path=docs/guide.md; target-lang=de; generated=2026-01-01T00:00:00Z -->\n# Hallo\n");
        WriteAndCommit("README.md", "unrelated change", "unrelated");

        var orchestrator = BuildOrchestrator(out var translateCalls, out _);
        var options = BuildOptions(targetLanguages: ["tr", "de"], backfillMissingTranslations: true);

        await orchestrator.RunAsync(options, CancellationToken.None);

        translateCalls.Distinct().Should().Equal(["docs/guide.md"]);
        File.Exists(Path.Combine(_repoRoot, "docs", "tr", "guide.md")).Should().BeTrue();
        // The existing "de" output is untouched - still exactly what was seeded above.
        File.ReadAllText(Path.Combine(_repoRoot, "docs", "de", "guide.md")).Should().Contain("# Hallo");
    }

    [Fact]
    public async Task RunAsync_NoChangedFiles_ReturnsZeroAndSkipsTranslation()
    {
        WriteAndCommit("README.md", "hello", "base");

        var orchestrator = BuildOrchestrator(out var translateCalls, out _);
        var options = BuildOptions(targetLanguages: ["tr"]);

        var exitCode = await orchestrator.RunAsync(options, CancellationToken.None);

        exitCode.Should().Be(0);
        translateCalls.Should().BeEmpty();
    }

    [Fact]
    public async Task RunAsync_PrModeWithNullBaseBranch_OpensPullRequestAgainstMain()
    {
        WriteAndCommit("docs/guide.md", "# Hello\n\nSome text.\n", "base");
        WriteAndCommit("docs/guide.md", "# Hello\n\nSome revised text.\n", "revise");

        var orchestrator = BuildOrchestrator(out _, out var gitHubService);
        var options = BuildOptions(targetLanguages: ["tr"], dryRun: false, baseBranch: null);

        var previousRepository = Environment.GetEnvironmentVariable("GITHUB_REPOSITORY");
        Environment.SetEnvironmentVariable("GITHUB_REPOSITORY", "test-owner/test-repo");
        try
        {
            await orchestrator.RunAsync(options, CancellationToken.None);
        }
        finally
        {
            Environment.SetEnvironmentVariable("GITHUB_REPOSITORY", previousRepository);
        }

        // Mirrors the empty-GITHUB_BASE_REF crash: BaseBranch unset must still resolve to a
        // concrete branch to open the PR against, never null/empty reaching Octokit.
        gitHubService.LastRequest.Should().NotBeNull();
        gitHubService.LastRequest!.BaseBranch.Should().Be("main");
    }

    [Fact]
    public async Task RunAsync_PrModeWithGlossaryAndCode_SummaryCommentReportsPreservedContentAndPreview()
    {
        File.WriteAllText(Path.Combine(_repoRoot, ".doc-terms.json"), """{"dont_translate": ["GitHub"]}""");
        WriteAndCommit("docs/guide.md", "# Hello\n\nSome text.\n", "base");
        WriteAndCommit(
            "docs/guide.md",
            "# Getting Started\n\nClone the GitHub repository and run `npm install` first.\n\n```bash\necho hello\n```\n",
            "revise");

        var orchestrator = BuildOrchestrator(out _, out var gitHubService);
        var options = BuildOptions(targetLanguages: ["tr"], dryRun: false, baseBranch: null);

        var previousRepository = Environment.GetEnvironmentVariable("GITHUB_REPOSITORY");
        Environment.SetEnvironmentVariable("GITHUB_REPOSITORY", "test-owner/test-repo");
        try
        {
            await orchestrator.RunAsync(options, CancellationToken.None);
        }
        finally
        {
            Environment.SetEnvironmentVariable("GITHUB_REPOSITORY", previousRepository);
        }

        var comment = gitHubService.LastRequest!.SummaryComment;
        comment.Should().Contain("GitHub");
        comment.Should().Contain("code block");
        comment.Should().Contain("inline code span");
        comment.Should().Contain("<details>");
        comment.Should().Contain("docs/guide.md");
        comment.Should().Contain("Getting Started");
    }

    [Fact]
    public async Task RunAsync_PushToCurrentBranch_CommitsDirectlyAndNeverOpensAPullRequest()
    {
        WriteAndCommit("docs/guide.md", "# Hello\n\nSome text.\n", "base");
        WriteAndCommit("docs/guide.md", "# Hello\n\nSome revised text.\n", "revise");

        var orchestrator = BuildOrchestrator(out _, out var gitHubService);
        var options = BuildOptions(targetLanguages: ["tr"], dryRun: false, pushToCurrentBranch: true);

        var exitCode = await orchestrator.RunAsync(options, CancellationToken.None);

        exitCode.Should().Be(0);
        gitHubService.LastCurrentBranchRequest.Should().NotBeNull();
        gitHubService.LastCurrentBranchRequest!.FilesToCommit.Should().ContainSingle(kvp => kvp.Key.Contains("guide.md") && kvp.Key.Contains("tr"));
        gitHubService.LastRequest.Should().BeNull("push-to-current-branch never opens/reuses a pull request");
    }

    private void WriteAndCommit(string relativePath, string content, string message)
    {
        var fullPath = Path.Combine(_repoRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, content);

        using var repo = new Repository(_repoRoot);
        Commands.Stage(repo, relativePath);
        var signature = new Signature("test", "test@test.com", DateTimeOffset.Now);
        repo.Commit(message, signature, signature);
    }

    private TranslationOrchestrator BuildOrchestrator(out List<string> translateCalls, out StubGitHubService gitHubService)
    {
        var calls = new List<string>();
        gitHubService = new StubGitHubService();
        var tokenUsageTracker = new TokenUsageTracker();

        var orchestrator = new TranslationOrchestrator(
            new GlossaryService(),
            new DocIgnoreService(),
            new MarkdigParserService(),
            new AstReconstructor(),
            new StubLlmProviderFactory(calls),
            new LibGit2SharpDiffAnalyzer(),
            new FileTranslationCache(Path.Combine(_repoRoot, ".doc-translator-cache")),
            new DriftDetector(),
            gitHubService,
            new PrSummaryBuilder(),
            new OutputPathResolver(),
            new ConsoleSummaryWriter(tokenUsageTracker),
            new JobSummaryWriter(tokenUsageTracker),
            new GitHubActionsLog());

        translateCalls = calls;
        return orchestrator;
    }

    private ActionOptions BuildOptions(
        IReadOnlyList<string> targetLanguages,
        bool dryRun = true,
        string? baseBranch = null,
        bool backfillMissingTranslations = false,
        bool estimateCostOnly = false,
        bool pushToCurrentBranch = false) =>
        new()
        {
            GitHubToken = "dummy",
            TargetLanguages = targetLanguages,
            SourcePath = "docs",
            RepositoryPath = _repoRoot,
            DryRun = dryRun,
            BaseBranch = baseBranch,
            GlossaryPath = Path.Combine(_repoRoot, ".doc-terms.json"),
            BackfillMissingTranslations = backfillMissingTranslations,
            EstimateCostOnly = estimateCostOnly,
            PushToCurrentBranch = pushToCurrentBranch,
        };

    /// <summary>Records which source files were sent for translation, per <see cref="FakeTranslationService"/>-style behavior - no network call.</summary>
    private sealed class StubLlmProviderFactory(List<string> calls) : ILlmProviderFactory
    {
        public ILlmTranslationService Create() => new RecordingFakeTranslationService(calls);
    }

    private sealed class RecordingFakeTranslationService(List<string> calls) : ILlmTranslationService
    {
        private readonly FakeTranslationService _inner = new();

        public string ProviderName => _inner.ProviderName;

        public void Dispose() => _inner.Dispose();

        public Task<IReadOnlyList<TranslatedChunk>> TranslateAsync(
            IReadOnlyList<TranslationChunk> chunks,
            string targetLanguageCode,
            GlossaryContext glossary,
            CancellationToken cancellationToken,
            string sourceLanguage = "auto")
        {
            foreach (var chunk in chunks)
            {
                calls.Add(chunk.SourceFilePath);
            }

            return _inner.TranslateAsync(chunks, targetLanguageCode, glossary, cancellationToken, sourceLanguage);
        }

        public Task<TranslatedChunk> RepairChunkAsync(
            TranslationChunk chunk,
            string previousTranslatedText,
            IReadOnlyList<string> missingMarkers,
            string targetLanguageCode,
            GlossaryContext glossary,
            CancellationToken cancellationToken,
            string sourceLanguage = "auto") =>
            _inner.RepairChunkAsync(chunk, previousTranslatedText, missingMarkers, targetLanguageCode, glossary, cancellationToken, sourceLanguage);
    }

    /// <summary>Captures the request instead of calling the real GitHub API - no network call.</summary>
    private sealed class StubGitHubService : IGitHubService
    {
        public GitHubPushRequest? LastRequest { get; private set; }

        public CurrentBranchPushRequest? LastCurrentBranchRequest { get; private set; }

        public Task<PullRequestOutcome> CommitAndOpenPullRequestAsync(GitHubPushRequest request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(new PullRequestOutcome("https://example.com/pr/1", 1, WasCreated: true));
        }

        public Task CommitToCurrentBranchAsync(CurrentBranchPushRequest request, CancellationToken cancellationToken)
        {
            LastCurrentBranchRequest = request;
            return Task.CompletedTask;
        }
    }
}
