using System.Globalization;

namespace DocTranslator.Cli.Options;

/// <summary>
/// CLI flag values parsed by System.CommandLine (see Program.cs). Any non-null field here
/// overrides the corresponding GitHub Action input / env var - this is what lets a developer run
/// the tool locally without setting any env vars at all.
/// </summary>
public sealed class ActionOptionsCliOverrides
{
    public string? GitHubToken { get; init; }
    public string? TargetLanguages { get; init; }
    public string? SourcePath { get; init; }
    public string? IncludeGlob { get; init; }
    public string? GlossaryPath { get; init; }
    public string? BaseBranch { get; init; }
    public bool? DryRun { get; init; }
    public bool? UseFakeLlm { get; init; }
    public bool Verbose { get; init; }
}

/// <summary>
/// Resolves the final <see cref="ActionOptions"/> by merging (in priority order) CLI flags,
/// then GitHub Action inputs, then sane defaults.
///
/// GitHub Actions documents its Docker-action env var convention as "converts input names to
/// uppercase letters and replaces spaces with `_` characters" - i.e. hyphens in the input name
/// (e.g. <c>target-languages</c>) are expected to survive as <c>INPUT_TARGET-LANGUAGES</c>, which
/// <see cref="Microsoft.Extensions.Configuration"/>'s default underscore-delimited env-var binder
/// would mis-map. Rather than trust one reading of that rule, <see cref="ReadInput"/> checks both
/// the hyphenated and the underscored form of every input name, so this works regardless of which
/// convention is actually in effect at runtime.
/// </summary>
public static class ActionOptionsBinder
{
    public static ActionOptions Bind(ActionOptionsCliOverrides cli)
    {
        var dryRun = cli.DryRun ?? ParseBool(ReadInput("dry-run")) ?? false;

        // github-token is only required when we're actually going to push/open a PR - a dry
        // run never touches GitHub credentials, so local smoke testing needs no token at all.
        var githubToken = cli.GitHubToken ?? ReadInput("github-token")
            ?? (dryRun ? string.Empty : throw new InvalidOperationException("The 'github-token' input (or --github-token) is required."));

        var targetLanguagesRaw = cli.TargetLanguages ?? ReadInput("target-languages")
            ?? throw new InvalidOperationException("The 'target-languages' input (or --target-languages) is required.");

        var llmProvider = cli.UseFakeLlm == true ? "fake" : ReadInput("llm-provider") ?? "auto";

        return new ActionOptions
        {
            GitHubToken = githubToken,
            GeminiApiKey = ReadInput("gemini-api-key"),
            OpenAiApiKey = ReadInput("openai-api-key"),
            AnthropicApiKey = ReadInput("anthropic-api-key"),
            LlmProvider = llmProvider,
            GeminiModel = ReadInput("gemini-model"),
            OpenAiModel = ReadInput("openai-model"),
            ClaudeModel = ReadInput("claude-model"),
            TargetLanguages = targetLanguagesRaw
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            SourcePath = cli.SourcePath ?? ReadInput("source-path") ?? "docs",
            IncludeGlob = cli.IncludeGlob ?? ReadInput("include-glob") ?? "**/*.md",
            GlossaryPath = cli.GlossaryPath ?? ReadInput("glossary-path") ?? ".doc-terms.json",
            OutputPathTemplate = ReadInput("output-path-template") ?? "docs/{lang}/{relativePath}",
            BaseBranch = cli.BaseBranch ?? ReadInput("base-branch") ?? Environment.GetEnvironmentVariable("GITHUB_BASE_REF"),
            DryRun = dryRun,
            FailOnStaleTranslations = ParseBool(ReadInput("fail-on-stale-translations")) ?? false,
            RepositoryPath = Directory.GetCurrentDirectory(),
        };
    }

    /// <summary>
    /// Copies the resolved LLM-related settings into the plain, unprefixed env var names
    /// DocTranslator.LLM's <c>LlmProviderFactory</c> reads (<c>GEMINI_API_KEY</c>,
    /// <c>INPUT_GEMINI_MODEL</c>, etc.) - the LLM layer intentionally knows nothing about the
    /// GitHub Action's hyphenated input-naming convention, so this is where the two meet.
    /// </summary>
    public static void PublishLlmEnvironmentVariables(ActionOptions options)
    {
        SetIfPresent("GEMINI_API_KEY", options.GeminiApiKey);
        SetIfPresent("OPENAI_API_KEY", options.OpenAiApiKey);
        SetIfPresent("ANTHROPIC_API_KEY", options.AnthropicApiKey);
        SetIfPresent("INPUT_LLM_PROVIDER", options.LlmProvider);
        SetIfPresent("INPUT_GEMINI_MODEL", options.GeminiModel);
        SetIfPresent("INPUT_OPENAI_MODEL", options.OpenAiModel);
        SetIfPresent("INPUT_CLAUDE_MODEL", options.ClaudeModel);
    }

    private static void SetIfPresent(string envVarName, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            Environment.SetEnvironmentVariable(envVarName, value);
        }
    }

    private static string? ReadInput(string inputName)
    {
        var hyphenated = Environment.GetEnvironmentVariable($"INPUT_{inputName.ToUpperInvariant()}");
        if (!string.IsNullOrEmpty(hyphenated))
        {
            return hyphenated;
        }

        var underscored = Environment.GetEnvironmentVariable($"INPUT_{inputName.ToUpperInvariant().Replace('-', '_')}");
        return string.IsNullOrEmpty(underscored) ? null : underscored;
    }

    private static bool? ParseBool(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : bool.Parse(value.Trim().ToLower(CultureInfo.InvariantCulture));
}
