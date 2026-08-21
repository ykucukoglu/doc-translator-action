using System.CommandLine;
using DocTranslator.Cli.Logging;
using DocTranslator.Cli.Options;
using DocTranslator.Cli.Orchestration;
using DocTranslator.Core.Extensions;
using DocTranslator.Core.Glossary;
using DocTranslator.Core.Ignore;
using DocTranslator.Core.Parsing;
using DocTranslator.Core.Provenance;
using DocTranslator.Core.Reconstruction;
using DocTranslator.Core.Telemetry;
using DocTranslator.GitHub.Cache;
using DocTranslator.GitHub.Diff;
using DocTranslator.GitHub.Operations;
using DocTranslator.LLM.Batching;
using DocTranslator.LLM.Prompting;
using DocTranslator.LLM.Providers;
using DocTranslator.LLM.Retry;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var githubTokenOption = new Option<string?>("--github-token", "GitHub token (overrides the github-token input).");
var targetLanguagesOption = new Option<string?>("--target-languages", "Comma-separated target language codes (overrides the target-languages input).");
var sourcePathOption = new Option<string?>("--source-path", "Root folder to scan for documentation.");
var includeGlobOption = new Option<string?>("--include-glob", "Glob (relative to --source-path) selecting which files to translate.");
var glossaryPathOption = new Option<string?>("--glossary-path", "Path to the .doc-terms.json glossary file.");
var configPathOption = new Option<string?>("--config-path", "Path to an optional JSON config file supplying advanced settings.");
var outputPathTemplateOption = new Option<string?>("--output-path-template", "Output path template. Supports {lang}, {relativePath}, {dir}, {filename}, {ext}.");
var baseBranchOption = new Option<string?>("--base-branch", "Base branch to diff against and to open the pull request into.");
var prModeOption = new Option<bool?>("--pr-mode", "When false, behaves like --dry-run: writes translated files locally without pushing/opening a PR.");
var dryRunOption = new Option<bool?>("--dry-run", "Skip git push/PR - write translated files locally only. Overrides --pr-mode.");
var useFakeLlmOption = new Option<bool?>("--use-fake-llm", "Use a trivial marker-wrapping fake translator instead of a real LLM provider.");
var maxParallelRequestsOption = new Option<int?>("--max-parallel-requests", "Bounds how many LLM batch requests run concurrently per file/language (default 4).");
var backfillMissingTranslationsOption = new Option<bool?>("--backfill-missing-translations", "Also translate any source file/language pair with no existing output yet, regardless of this run's diff - for a first install or a newly-added target language.");
var estimateCostOnlyOption = new Option<bool?>("--estimate-cost-only", "Report an estimated input-token count for this run and exit - no LLM call, no git/GitHub access.");
var sourceLanguageOption = new Option<string?>("--source-language", "Source language code told to the LLM, or 'auto' (default) to leave it unstated.");
var maxBatchTokensOption = new Option<int?>("--max-batch-tokens", "Approximate token budget per LLM batch request (default 4000).");
var verboseOption = new Option<bool>("--verbose", () => false, "Verbose console output.");

var root = new RootCommand("doc-translator-action: AST-aware Markdown translation via Markdig + LLM.");
root.AddOption(githubTokenOption);
root.AddOption(targetLanguagesOption);
root.AddOption(sourcePathOption);
root.AddOption(includeGlobOption);
root.AddOption(glossaryPathOption);
root.AddOption(configPathOption);
root.AddOption(outputPathTemplateOption);
root.AddOption(baseBranchOption);
root.AddOption(prModeOption);
root.AddOption(dryRunOption);
root.AddOption(useFakeLlmOption);
root.AddOption(maxParallelRequestsOption);
root.AddOption(backfillMissingTranslationsOption);
root.AddOption(estimateCostOnlyOption);
root.AddOption(sourceLanguageOption);
root.AddOption(maxBatchTokensOption);
root.AddOption(verboseOption);

root.SetHandler(async context =>
{
    var cli = new ActionOptionsCliOverrides
    {
        GitHubToken = context.ParseResult.GetValueForOption(githubTokenOption),
        TargetLanguages = context.ParseResult.GetValueForOption(targetLanguagesOption),
        SourcePath = context.ParseResult.GetValueForOption(sourcePathOption),
        IncludeGlob = context.ParseResult.GetValueForOption(includeGlobOption),
        GlossaryPath = context.ParseResult.GetValueForOption(glossaryPathOption),
        ConfigPath = context.ParseResult.GetValueForOption(configPathOption),
        OutputPathTemplate = context.ParseResult.GetValueForOption(outputPathTemplateOption),
        BaseBranch = context.ParseResult.GetValueForOption(baseBranchOption),
        PrMode = context.ParseResult.GetValueForOption(prModeOption),
        DryRun = context.ParseResult.GetValueForOption(dryRunOption),
        UseFakeLlm = context.ParseResult.GetValueForOption(useFakeLlmOption),
        MaxParallelRequests = context.ParseResult.GetValueForOption(maxParallelRequestsOption),
        BackfillMissingTranslations = context.ParseResult.GetValueForOption(backfillMissingTranslationsOption),
        EstimateCostOnly = context.ParseResult.GetValueForOption(estimateCostOnlyOption),
        SourceLanguage = context.ParseResult.GetValueForOption(sourceLanguageOption),
        MaxBatchTokens = context.ParseResult.GetValueForOption(maxBatchTokensOption),
        Verbose = context.ParseResult.GetValueForOption(verboseOption),
    };

    context.ExitCode = await RunAsync(cli, context.GetCancellationToken());
});

return await root.InvokeAsync(args);

static async Task<int> RunAsync(ActionOptionsCliOverrides cliOverrides, CancellationToken cancellationToken)
{
    ActionOptions options;
    try
    {
        options = ActionOptionsBinder.Bind(cliOverrides);
    }
    catch (InvalidOperationException ex)
    {
        Console.Error.WriteLine($"Configuration error: {ex.Message}");
        return 1;
    }

    // Bridges the Action's hyphenated INPUT_* inputs into the plain env var names
    // DocTranslator.LLM's LlmProviderFactory reads (GEMINI_API_KEY, INPUT_GEMINI_MODEL, ...).
    ActionOptionsBinder.PublishLlmEnvironmentVariables(options);

    using var host = Host.CreateDefaultBuilder()
        .ConfigureServices(services =>
        {
            services.AddSingleton<IMarkdigParserService, MarkdigParserService>();
            services.AddSingleton<IGlossaryService, GlossaryService>();
            services.AddSingleton<IAstReconstructor, AstReconstructor>();
            services.AddSingleton<IDriftDetector, DriftDetector>();
            services.AddSingleton<IDocIgnoreService, DocIgnoreService>();
            services.AddSingleton<ITokenUsageTracker, TokenUsageTracker>();

            services.AddSingleton<IEnvironmentProvider, EnvironmentProvider>();
            services.AddSingleton<IChatClientFactory, ChatClientFactory>();
            services.AddSingleton<IPromptBuilder, PromptBuilder>();
            services.AddSingleton<IChunkBatcher>(_ => new ChunkBatcher(options.MaxBatchTokens));
            services.AddSingleton<ILlmResponseValidator, LlmResponseValidator>();
            services.AddSingleton<ILlmProviderFactory, LlmProviderFactory>();

            services.AddSingleton<IGitDiffAnalyzer, LibGit2SharpDiffAnalyzer>();
            services.AddSingleton<ITranslationCache>(_ =>
                new FileTranslationCache(Path.Combine(options.RepositoryPath, ".doc-translator-cache")));
            services.AddSingleton<IGitWriter, GitWriter>();
            services.AddSingleton<IGitHubService, OctokitGitHubService>();
            services.AddSingleton<IPrSummaryBuilder, PrSummaryBuilder>();

            services.AddSingleton<IOutputPathResolver, OutputPathResolver>();
            services.AddSingleton<IConsoleSummaryWriter, ConsoleSummaryWriter>();
            services.AddSingleton<IGitHubActionsLog, GitHubActionsLog>();
            services.AddSingleton<IJobSummaryWriter, JobSummaryWriter>();
            services.AddSingleton<TranslationOrchestrator>();
        })
        .Build();

    var orchestrator = host.Services.GetRequiredService<TranslationOrchestrator>();

    try
    {
        return await orchestrator.RunAsync(options, cancellationToken);
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine(Redact($"doc-translator-action failed: {ex.Message}", options));
        if (options.Verbose)
        {
            // ex.ToString() walks the full InnerException chain, including messages that
            // originate from vendor SDKs we don't control - redact known secret values before
            // printing rather than trusting every current and future SDK never to echo one back
            // (e.g. in a request-URI or header dump inside an HTTP-layer exception).
            Console.Error.WriteLine(Redact(ex.ToString(), options));
        }

        return 1;
    }
}

static string Redact(string text, ActionOptions options)
{
    foreach (var secret in new[] { options.GitHubToken, options.GeminiApiKey, options.OpenAiApiKey, options.AnthropicApiKey, options.AzureOpenAiApiKey })
    {
        if (!string.IsNullOrEmpty(secret))
        {
            text = text.Replace(secret, "***", StringComparison.Ordinal);
        }
    }

    return text;
}
