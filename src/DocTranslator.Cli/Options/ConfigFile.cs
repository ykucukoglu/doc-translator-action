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

    [JsonPropertyName("allowForkPullRequestTarget")]
    public bool? AllowForkPullRequestTarget { get; set; }

    [JsonPropertyName("maxParallelRequests")]
    public int? MaxParallelRequests { get; set; }

    [JsonPropertyName("llmProvider")]
    public string? LlmProvider { get; set; }

    [JsonPropertyName("geminiModel")]
    public string? GeminiModel { get; set; }

    [JsonPropertyName("openAiModel")]
    public string? OpenAiModel { get; set; }

    [JsonPropertyName("claudeModel")]
    public string? ClaudeModel { get; set; }
}

[JsonSerializable(typeof(ConfigFile))]
internal sealed partial class ConfigFileJsonContext : JsonSerializerContext
{
}
