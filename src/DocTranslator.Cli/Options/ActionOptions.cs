namespace DocTranslator.Cli.Options;

/// <summary>Fully-resolved configuration for one run, after merging env vars and CLI overrides.</summary>
public sealed class ActionOptions
{
    public required string GitHubToken { get; init; }

    public string? GeminiApiKey { get; init; }

    public string? OpenAiApiKey { get; init; }

    public string? AnthropicApiKey { get; init; }

    public string? AzureOpenAiApiKey { get; init; }

    public string? AzureOpenAiEndpoint { get; init; }

    public string? AzureOpenAiDeployment { get; init; }

    public string LlmProvider { get; init; } = "auto";

    /// <summary>When set, retried against once if the primary provider's translation call fails after exhausting its own retries. Must name a different provider than the resolved primary.</summary>
    public string? LlmFallbackProvider { get; init; }

    public string? GeminiModel { get; init; }

    public string? OpenAiModel { get; init; }

    /// <summary>Redirects the OpenAI provider at any OpenAI-compatible endpoint (Ollama, LM Studio, vLLM, OpenRouter, ...) instead of api.openai.com.</summary>
    public string? OpenAiBaseUrl { get; init; }

    public string? ClaudeModel { get; init; }

    public required IReadOnlyList<string> TargetLanguages { get; init; }

    public string SourcePath { get; init; } = "docs";

    public string IncludeGlob { get; init; } = "**/*.md";

    public string GlossaryPath { get; init; } = ".doc-terms.json";

    public string OutputPathTemplate { get; init; } = "docs/{lang}/{relativePath}";

    public string? BaseBranch { get; init; }

    public bool DryRun { get; init; }

    public bool FailOnStaleTranslations { get; init; }

    /// <summary>
    /// When true, also translates any source file/language pair with no existing output yet,
    /// regardless of whether that file changed in this run's diff. Off by default: the diff-only
    /// pipeline never picks up a repository's pre-existing docs on first install (nothing in them
    /// "changed"), so this is the opt-in escape hatch for that first run, or for backfilling a
    /// newly-added target language.
    /// </summary>
    public bool BackfillMissingTranslations { get; init; }

    /// <summary>
    /// When true, reports an estimated input-token count for this run's chunk/language pairs
    /// (skipping ones already cached) and exits, without calling any LLM or touching git/GitHub.
    /// </summary>
    public bool EstimateCostOnly { get; init; }

    /// <summary>Bounds how many LLM batch requests run concurrently (per file/language). See <c>SemaphoreSlim</c> usage in ChatClientLlmTranslationService.</summary>
    public int MaxParallelRequests { get; init; } = 4;

    /// <summary>
    /// When true (default), deletes this action's own <c>doc-translator/&lt;sha&gt;</c> branches once
    /// their pull request is closed (merged or declined) - otherwise a new branch accumulates every
    /// run and nothing ever removes the old ones.
    /// </summary>
    public bool CleanupStaleBranches { get; init; } = true;

    /// <summary>Source language code told to the LLM, or "auto" (default) to leave it unstated and let the model infer it.</summary>
    public string SourceLanguage { get; init; } = "auto";

    /// <summary>Approximate token budget per LLM batch request (char/4 heuristic). See <c>ChunkBatcher</c>.</summary>
    public int MaxBatchTokens { get; init; } = 4000;

    /// <summary>Prints the full exception (not just its message) to stderr on failure.</summary>
    public bool Verbose { get; init; }

    /// <summary>
    /// When true, translates node/edge/subgraph labels inside ```mermaid flowchart/graph diagrams
    /// (see <c>MermaidLabelExtractor</c>) - off by default, since this is the only feature that
    /// ever modifies content inside what would otherwise be an untouched code block.
    /// </summary>
    public bool TranslateMermaidDiagrams { get; init; }

    /// <summary>
    /// When true, commits and pushes translated files directly onto whatever branch is already
    /// checked out - no new branch, no pull request. Meant for a workflow with its own reason to
    /// already be on a specific branch (e.g. a PR-comment-triggered run pushing back onto that
    /// PR's own branch). Requires the checkout to be a real branch, not a detached HEAD.
    /// </summary>
    public bool PushToCurrentBranch { get; init; }

    public string RepositoryPath { get; init; } = Directory.GetCurrentDirectory();
}
