using System.Globalization;
using System.Text.Json;

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
    public string? ConfigPath { get; init; }
    public string? OutputPathTemplate { get; init; }
    public string? BaseBranch { get; init; }
    public bool? PrMode { get; init; }
    public bool? DryRun { get; init; }
    public bool? UseFakeLlm { get; init; }
    public int? MaxParallelRequests { get; init; }
    public bool Verbose { get; init; }
}

/// <summary>
/// Resolves the final <see cref="ActionOptions"/> by merging (in priority order) CLI flags, then
/// GitHub Action inputs, then the optional <c>config-path</c> JSON file, then sane defaults.
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
        var repositoryPath = Directory.GetCurrentDirectory();
        var config = LoadConfigFile(cli, repositoryPath);

        // pr-mode is the user-facing on/off switch (default: open a PR); dry-run remains as an
        // explicit override for anyone who set it directly - if both are given, dry-run wins.
        var prMode = cli.PrMode ?? ParseBool(ReadInput("pr-mode")) ?? true;
        var dryRun = cli.DryRun ?? ParseBool(ReadInput("dry-run")) ?? !prMode;

        // github-token is only required when we're actually going to push/open a PR - a dry
        // run never touches GitHub credentials, so local smoke testing needs no token at all.
        var githubToken = cli.GitHubToken ?? ReadInput("github-token")
            ?? (dryRun ? string.Empty : throw new InvalidOperationException(
                "The 'github-token' input (or --github-token) is required when pr-mode is enabled. "
                + $"(resolved: pr-mode='{ReadInput("pr-mode") ?? "<unset>"}', dry-run='{ReadInput("dry-run") ?? "<unset>"}')"));

        var targetLanguagesRaw = cli.TargetLanguages ?? ReadInput("target-languages") ?? "tr";

        var llmProvider = cli.UseFakeLlm == true ? "fake" : ReadInput("llm-provider") ?? config?.LlmProvider ?? "auto";

        return new ActionOptions
        {
            GitHubToken = githubToken,
            GeminiApiKey = ReadInput("gemini-api-key"),
            OpenAiApiKey = ReadInput("openai-api-key"),
            AnthropicApiKey = ReadInput("anthropic-api-key"),
            LlmProvider = llmProvider,
            GeminiModel = ReadInput("gemini-model") ?? config?.GeminiModel,
            OpenAiModel = ReadInput("openai-model") ?? config?.OpenAiModel,
            ClaudeModel = ReadInput("claude-model") ?? config?.ClaudeModel,
            TargetLanguages = targetLanguagesRaw
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            SourcePath = cli.SourcePath ?? ReadInput("source-path") ?? config?.SourcePath ?? "docs",
            IncludeGlob = cli.IncludeGlob ?? ReadInput("include-glob") ?? config?.IncludeGlob ?? "**/*.md",
            GlossaryPath = cli.GlossaryPath ?? ReadInput("glossary-path") ?? ".doc-terms.json",
            OutputPathTemplate = cli.OutputPathTemplate ?? ReadInput("output-path-template") ?? config?.OutputPathTemplate ?? "docs/{lang}/{relativePath}",
            BaseBranch = cli.BaseBranch ?? ReadInput("base-branch") ?? config?.BaseBranch ?? NullIfEmpty(Environment.GetEnvironmentVariable("GITHUB_BASE_REF")),
            DryRun = dryRun,
            FailOnStaleTranslations = ParseBool(ReadInput("fail-on-stale-translations")) ?? config?.FailOnStaleTranslations ?? false,
            MaxParallelRequests = cli.MaxParallelRequests ?? ParseInt(ReadInput("max-parallel-requests")) ?? config?.MaxParallelRequests ?? 4,
            RepositoryPath = repositoryPath,
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
        SetIfPresent("INPUT_MAX_PARALLEL_REQUESTS", options.MaxParallelRequests.ToString(CultureInfo.InvariantCulture));
    }

    private static ConfigFile? LoadConfigFile(ActionOptionsCliOverrides cli, string repositoryPath)
    {
        var configPath = cli.ConfigPath ?? ReadInput("config-path");
        if (string.IsNullOrWhiteSpace(configPath))
        {
            return null;
        }

        var fullPath = Path.IsPathRooted(configPath) ? configPath : Path.Combine(repositoryPath, configPath);
        if (!File.Exists(fullPath))
        {
            throw new InvalidOperationException($"The 'config-path' input points to '{configPath}', which does not exist.");
        }

        var json = File.ReadAllText(fullPath);
        return JsonSerializer.Deserialize(json, ConfigFileJsonContext.Default.ConfigFile)
            ?? throw new InvalidOperationException($"Config file '{configPath}' is empty or invalid.");
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

    private static int? ParseInt(string? value) =>
        !string.IsNullOrWhiteSpace(value) && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) && parsed > 0
            ? parsed
            : null;

    // GITHUB_BASE_REF is set by GitHub Actions to an empty string (not unset) on push events - it's
    // only populated for pull_request events. A plain `??` chain treats "" as a present value, so
    // this normalizes it to null before it reaches any downstream `?? "main"`-style default.
    private static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
