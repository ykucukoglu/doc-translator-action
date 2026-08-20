namespace DocTranslator.Cli.Options;

/// <summary>Fully-resolved configuration for one run, after merging env vars and CLI overrides.</summary>
public sealed class ActionOptions
{
    public required string GitHubToken { get; init; }

    public string? GeminiApiKey { get; init; }

    public string? OpenAiApiKey { get; init; }

    public string? AnthropicApiKey { get; init; }

    public string LlmProvider { get; init; } = "auto";

    public string? GeminiModel { get; init; }

    public string? OpenAiModel { get; init; }

    public string? ClaudeModel { get; init; }

    public required IReadOnlyList<string> TargetLanguages { get; init; }

    public string SourcePath { get; init; } = "docs";

    public string IncludeGlob { get; init; } = "**/*.md";

    public string GlossaryPath { get; init; } = ".doc-terms.json";

    public string OutputPathTemplate { get; init; } = "docs/{lang}/{relativePath}";

    public string? BaseBranch { get; init; }

    public bool DryRun { get; init; }

    public bool FailOnStaleTranslations { get; init; }

    /// <summary>Bounds how many LLM batch requests run concurrently (per file/language). See <c>SemaphoreSlim</c> usage in ChatClientLlmTranslationService.</summary>
    public int MaxParallelRequests { get; init; } = 4;

    public string RepositoryPath { get; init; } = Directory.GetCurrentDirectory();
}
