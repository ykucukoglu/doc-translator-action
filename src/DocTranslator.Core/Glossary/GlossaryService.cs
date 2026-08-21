using System.Text;
using System.Text.RegularExpressions;
using DocTranslator.Core.Models;

namespace DocTranslator.Core.Glossary;

public interface IGlossaryService
{
    /// <summary>Loads and parses a <c>.doc-terms.json</c> file. Returns <see cref="GlossaryContext.Empty"/> if the file doesn't exist.</summary>
    GlossaryContext Load(string glossaryPath);

    /// <summary>Builds the style guide and glossary instruction block embedded in the LLM prompt for a given target language.</summary>
    string BuildPromptHint(GlossaryContext glossary, string targetLanguage);

    /// <summary>
    /// Post-translation QA check: confirms every <c>dont_translate</c> term (and required
    /// <c>custom_mappings</c> rendering) that appeared in the source survived into the
    /// translated output. Uses word-boundary matching, not plain substring search, so short
    /// terms like "API" don't false-positive inside longer words like "CAPITAL".
    /// </summary>
    IReadOnlyList<string> Validate(string sourceText, string translatedText, GlossaryContext glossary, string targetLanguage);

    /// <summary>
    /// Which <c>dont_translate</c> terms appear in <paramref name="sourceText"/> and were confirmed
    /// still present, verbatim, in <paramref name="translatedText"/> - the positive counterpart to
    /// <see cref="Validate"/>'s failure warnings, used to report what was actually protected (e.g.
    /// in the PR summary), not just what went wrong.
    /// </summary>
    IReadOnlyCollection<string> FindPreservedTerms(string sourceText, string translatedText, GlossaryContext glossary);
}

public sealed class GlossaryService : IGlossaryService
{
    public GlossaryContext Load(string glossaryPath)
    {
        if (!File.Exists(glossaryPath))
        {
            return GlossaryContext.Empty;
        }

        var json = File.ReadAllText(glossaryPath);
        var file = System.Text.Json.JsonSerializer.Deserialize(json, DocTermsJsonContext.Default.DocTermsFile)
            ?? throw new InvalidOperationException($"Glossary file '{glossaryPath}' is empty or invalid.");

        var customMappings = file.CustomMappings.ToDictionary(
            kvp => kvp.Key,
            kvp => (IReadOnlyDictionary<string, string>)kvp.Value);

        return new GlossaryContext(
            DontTranslate: new HashSet<string>(file.DontTranslate, file.CaseSensitive ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase),
            CustomMappings: customMappings,
            CaseSensitive: file.CaseSensitive,
            StyleGuide: string.IsNullOrWhiteSpace(file.StyleGuide) ? null : file.StyleGuide.Trim());
    }

    public string BuildPromptHint(GlossaryContext glossary, string targetLanguage)
    {
        var hasStyleGuide = !string.IsNullOrEmpty(glossary.StyleGuide);
        if (!hasStyleGuide && glossary.DontTranslate.Count == 0 && glossary.MappingsFor(targetLanguage).Count == 0)
        {
            return string.Empty;
        }

        var sb = new StringBuilder();

        if (hasStyleGuide)
        {
            sb.Append("Follow this style guide: ").Append(glossary.StyleGuide).Append(' ');
        }

        if (glossary.DontTranslate.Count > 0)
        {
            sb.Append("These terms must appear verbatim, untranslated, in the output: ")
              .Append(string.Join(", ", glossary.DontTranslate))
              .Append('.').Append(' ');
        }

        var mappings = glossary.MappingsFor(targetLanguage);
        if (mappings.Count > 0)
        {
            sb.Append("These source terms must be rendered exactly as specified: ")
              .Append(string.Join(", ", mappings.Select(kvp => $"\"{kvp.Key}\" -> \"{kvp.Value}\"")))
              .Append('.');
        }

        return sb.ToString();
    }

    public IReadOnlyList<string> Validate(string sourceText, string translatedText, GlossaryContext glossary, string targetLanguage)
    {
        var warnings = new List<string>();
        var comparison = glossary.CaseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase;

        foreach (var term in glossary.DontTranslate)
        {
            if (!ContainsWholeWord(sourceText, term, comparison))
            {
                continue; // term isn't even in this chunk's source text - nothing to check
            }

            if (!ContainsWholeWord(translatedText, term, comparison))
            {
                warnings.Add($"Glossary term '{term}' was expected verbatim in the translation but was not found.");
            }
        }

        foreach (var (sourceTerm, requiredTarget) in glossary.MappingsFor(targetLanguage))
        {
            if (!ContainsWholeWord(sourceText, sourceTerm, comparison))
            {
                continue;
            }

            if (!ContainsWordStart(translatedText, requiredTarget, comparison))
            {
                warnings.Add($"Glossary mapping '{sourceTerm}' -> '{requiredTarget}' ({targetLanguage}) was not found in the translation.");
            }
        }

        return warnings;
    }

    public IReadOnlyCollection<string> FindPreservedTerms(string sourceText, string translatedText, GlossaryContext glossary)
    {
        var comparison = glossary.CaseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase;

        return glossary.DontTranslate
            .Where(term => ContainsWholeWord(sourceText, term, comparison) && ContainsWholeWord(translatedText, term, comparison))
            .ToList();
    }

    private static bool ContainsWholeWord(string text, string term, RegexOptions comparisonOptions)
    {
        var pattern = $@"\b{Regex.Escape(term)}\b";
        return Regex.IsMatch(text, pattern, comparisonOptions | RegexOptions.CultureInvariant);
    }

    /// <summary>
    /// Like <see cref="ContainsWholeWord"/> but only requires a word boundary before the term, not
    /// after it. Agglutinative target languages (Turkish, Finnish, Hungarian...) glue case suffixes
    /// directly onto native words with no separator - e.g. "depo" (repository) commonly appears as
    /// "depoya"/"deposunu"/"depodan" in real translations - so a trailing \b produces false-positive
    /// warnings for otherwise-correct output. Only used for custom_mappings' required target term;
    /// dont_translate stays whole-word since those terms (GitHub, API, SDK...) are meant to survive
    /// verbatim, and Turkish orthography attaches an apostrophe before suffixes on such loanwords/
    /// abbreviations anyway (e.g. "API'ye"), which a trailing \b already handles correctly.
    /// </summary>
    private static bool ContainsWordStart(string text, string term, RegexOptions comparisonOptions)
    {
        var pattern = $@"\b{Regex.Escape(term)}";
        return Regex.IsMatch(text, pattern, comparisonOptions | RegexOptions.CultureInvariant);
    }
}
