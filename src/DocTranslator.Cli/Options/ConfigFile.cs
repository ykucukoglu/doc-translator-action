using System.Text.Json.Serialization;

namespace DocTranslator.Cli.Options;

/// <summary>
/// Optional JSON config file (<c>config-path</c> input) supplying any of the less-commonly-changed
/// settings, so a large <c>with:</c> block isn't required for advanced setups. Every field is
/// optional; explicit action inputs / CLI flags always take priority over the config file, which
/// in turn takes priority over hardcoded defaults.
/// </summary>
public sealed class ConfigFile
{
    [JsonPropertyName("targetLanguages")]
    public string? TargetLanguages { get; set; }

    [JsonPropertyName("sourcePath")]
    public string? SourcePath { get; set; }

    [JsonPropertyName("includeGlob")]
    public string? IncludeGlob { get; set; }

    [JsonPropertyName("glossaryPath")]
    public string? GlossaryPath { get; set; }

    [JsonPropertyName("outputPathTemplate")]
    public string? OutputPathTemplate { get; set; }

    [JsonPropertyName("baseBranch")]
    public string? BaseBranch { get; set; }

    [JsonPropertyName("failOnStaleTranslations")]
    public bool? FailOnStaleTranslations { get; set; }

    [JsonPropertyName("backfillMissingTranslations")]
    public bool? BackfillMissingTranslations { get; set; }

    [JsonPropertyName("estimateCostOnly")]
    public bool? EstimateCostOnly { get; set; }

    // Deliberately no allowForkPullRequestTarget field here - see ActionOptionsBinder.cs. That
    // safety-net override is only ever honored from the real action input, never from this file,
    // since the file is read from the job's working directory, which under pull_request_target
    // with a fork-head checkout is the fork's own (untrusted) content.

    [JsonPropertyName("maxParallelRequests")]
    public int? MaxParallelRequests { get; set; }

    [JsonPropertyName("cleanupStaleBranches")]
    public bool? CleanupStaleBranches { get; set; }

    [JsonPropertyName("sourceLanguage")]
    public string? SourceLanguage { get; set; }

    [JsonPropertyName("maxBatchTokens")]
    public int? MaxBatchTokens { get; set; }

    [JsonPropertyName("llmProvider")]
    public string? LlmProvider { get; set; }

    [JsonPropertyName("llmFallbackProvider")]
    public string? LlmFallbackProvider { get; set; }

    [JsonPropertyName("geminiModel")]
    public string? GeminiModel { get; set; }

    [JsonPropertyName("openAiModel")]
    public string? OpenAiModel { get; set; }

    [JsonPropertyName("openAiBaseUrl")]
    public string? OpenAiBaseUrl { get; set; }

    [JsonPropertyName("claudeModel")]
    public string? ClaudeModel { get; set; }
}

[JsonSerializable(typeof(ConfigFile))]
internal sealed partial class ConfigFileJsonContext : JsonSerializerContext
{
}
