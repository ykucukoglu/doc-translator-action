using System.Text.Json.Serialization;

namespace DocTranslator.Core.Glossary;

/// <summary>Raw deserialization shape of <c>.doc-terms.json</c>. See the schema in the implementation plan.</summary>
internal sealed class DocTermsFile
{
    [JsonPropertyName("version")]
    public int Version { get; set; } = 1;

    [JsonPropertyName("case_sensitive")]
    public bool CaseSensitive { get; set; }

    [JsonPropertyName("dont_translate")]
    public List<string> DontTranslate { get; set; } = [];

    [JsonPropertyName("custom_mappings")]
    public Dictionary<string, Dictionary<string, string>> CustomMappings { get; set; } = [];

    [JsonPropertyName("style_guide")]
    public string? StyleGuide { get; set; }
}

[JsonSerializable(typeof(DocTermsFile))]
internal sealed partial class DocTermsJsonContext : JsonSerializerContext
{
}
