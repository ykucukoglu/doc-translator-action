namespace DocTranslator.Core.Models;

/// <summary>
/// Parsed contents of a <c>.doc-terms.json</c> glossary, ready to be used for prompt hints and
/// post-translation validation. See <see cref="DocTranslator.Core.Glossary.GlossaryService"/>.
/// </summary>
/// <param name="DontTranslate">Terms that must appear verbatim, untranslated, in every target language.</param>
/// <param name="CustomMappings">Per-target-language source-term -&gt; required-rendering overrides.</param>
/// <param name="CaseSensitive">Whether glossary term matching is case-sensitive.</param>
public sealed record GlossaryContext(
    IReadOnlySet<string> DontTranslate,
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> CustomMappings,
    bool CaseSensitive)
{
    public static GlossaryContext Empty { get; } = new(
        DontTranslate: new HashSet<string>(),
        CustomMappings: new Dictionary<string, IReadOnlyDictionary<string, string>>(),
        CaseSensitive: false);

    public IReadOnlyDictionary<string, string> MappingsFor(string targetLanguage) =>
        CustomMappings.TryGetValue(targetLanguage, out var mappings)
            ? mappings
            : new Dictionary<string, string>();
}
