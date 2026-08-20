using System.Text;
using System.Text.RegularExpressions;
using DocTranslator.Core.Models;

namespace DocTranslator.Core.Glossary;

public interface IGlossaryService
{
    /// <summary>Loads and parses a <c>.doc-terms.json</c> file. Returns <see cref="GlossaryContext.Empty"/> if the file doesn't exist.</summary>
    GlossaryContext Load(string glossaryPath);

    /// <summary>Builds the glossary instruction block embedded in the LLM prompt for a given target language.</summary>
    string BuildPromptHint(GlossaryContext glossary, string targetLanguage);

    /// <summary>
    /// Post-translation QA check: confirms every <c>dont_translate</c> term (and required
    /// <c>custom_mappings</c> rendering) that appeared in the source survived into the
    /// translated output. Uses word-boundary matching, not plain substring search, so short
    /// terms like "API" don't false-positive inside longer words like "CAPITAL".
    /// </summary>
    IReadOnlyList<string> Validate(string sourceText, string translatedText, GlossaryContext glossary, string targetLanguage);
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
            CaseSensitive: file.CaseSensitive);
    }

    public string BuildPromptHint(GlossaryContext glossary, string targetLanguage)
    {
        if (glossary.DontTranslate.Count == 0 && glossary.MappingsFor(targetLanguage).Count == 0)
        {
            return string.Empty;
        }

        var sb = new StringBuilder();

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

            if (!ContainsWholeWord(translatedText, requiredTarget, comparison))
            {
                warnings.Add($"Glossary mapping '{sourceTerm}' -> '{requiredTarget}' ({targetLanguage}) was not found in the translation.");
            }
        }

        return warnings;
    }

    private static bool ContainsWholeWord(string text, string term, RegexOptions comparisonOptions)
    {
        var pattern = $@"\b{Regex.Escape(term)}\b";
        return Regex.IsMatch(text, pattern, comparisonOptions | RegexOptions.CultureInvariant);
    }
}
