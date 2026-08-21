using System.Text.RegularExpressions;
using DocTranslator.Core.Models;

namespace DocTranslator.Core.Diagrams;

/// <summary>
/// Extracts the human-readable string VALUES of a fixed, safe allowlist of frontmatter keys
/// (<c>title</c>, <c>description</c>, <c>sidebar_label</c>, <c>label</c>) from a YAML (<c>---</c>)
/// or TOML (<c>+++</c>) frontmatter block's raw text, so just those get translated while every
/// other field - <c>slug</c>, <c>sidebar_position</c>, <c>tags</c>, dates, booleans, numbers, or
/// anything not on the allowlist - stays exactly as written. Deliberately narrow, not a YAML/TOML
/// parser: a value that isn't a plain or quoted scalar on one line (an array, a multi-line block
/// scalar, a nested mapping) is left alone rather than guessed at.
/// </summary>
public static class FrontmatterFieldExtractor
{
    private static readonly HashSet<string> TranslatableKeys = new(StringComparer.Ordinal)
    {
        "title", "description", "sidebar_label", "label",
    };

    private static readonly Regex YamlFieldPattern = new(
        @"^(?<indent>\s*)(?<key>[A-Za-z0-9_-]+)\s*:\s*(?<value>.*)$", RegexOptions.Compiled);

    private static readonly Regex TomlFieldPattern = new(
        @"^(?<indent>\s*)(?<key>[A-Za-z0-9_-]+)\s*=\s*(?<value>.*)$", RegexOptions.Compiled);

    /// <summary>Every translatable field-value span found, in source order. Offsets are into <paramref name="rawText"/> exactly as given (fence lines included).</summary>
    public static IReadOnlyList<ExtractedTextSpan> ExtractTranslatableFields(string rawText)
    {
        var lines = rawText.Split('\n');
        if (lines.Length < 2)
        {
            return [];
        }

        var fence = lines[0].TrimEnd('\r').Trim();
        var fieldPattern = fence switch
        {
            "---" => YamlFieldPattern,
            "+++" => TomlFieldPattern,
            _ => null,
        };

        if (fieldPattern is null)
        {
            return [];
        }

        var spans = new List<ExtractedTextSpan>();
        var lineStart = lines[0].Length + 1;

        // Line 0 is the opening fence, handled above; the last element of Split is whatever
        // follows the closing fence (open-ended, since ExtractTomlFrontmatterIfPresent/the YAML
        // block's own Span both include it) - only the lines strictly between the two fences can
        // hold fields, and the loop below stops as soon as it sees the matching closing fence.
        for (var i = 1; i < lines.Length; i++)
        {
            var line = lines[i];
            if (line.TrimEnd('\r').Trim() == fence)
            {
                break; // closing fence - nothing after this belongs to the frontmatter block
            }

            ExtractFromLine(line, lineStart, fieldPattern, spans);
            lineStart += line.Length + 1;
        }

        return spans;
    }

    private static void ExtractFromLine(string line, int lineStart, Regex fieldPattern, List<ExtractedTextSpan> spans)
    {
        var match = fieldPattern.Match(line);
        if (!match.Success || !TranslatableKeys.Contains(match.Groups["key"].Value))
        {
            return;
        }

        var valueGroup = match.Groups["value"];
        var raw = valueGroup.Value.TrimEnd('\r');
        if (!LooksLikeTranslatableScalar(raw))
        {
            return;
        }

        var start = lineStart + valueGroup.Index;
        var length = raw.Length;
        var text = raw;

        if (raw.Length >= 2 && (raw[0] == '"' && raw[^1] == '"' || raw[0] == '\'' && raw[^1] == '\''))
        {
            // Narrowed to exclude the quote characters, same reasoning as MermaidLabelExtractor:
            // splicing a translation back in only ever touches what was between them.
            start += 1;
            length -= 2;
            text = raw[1..^1];
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        spans.Add(new ExtractedTextSpan(start, length, text));
    }

    /// <summary>
    /// A conservative allowlist, not a YAML/TOML type check: rejects anything that looks like it
    /// might not be a plain string (empty, an array/object opener, a block-scalar opener, a bare
    /// number, or a bare true/false) rather than trying to fully classify the value's real type.
    /// </summary>
    private static bool LooksLikeTranslatableScalar(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.Length == 0)
        {
            return false;
        }

        if (trimmed[0] is '[' or '{' or '|' or '>' or '&' or '*')
        {
            return false;
        }

        if (trimmed is "true" or "false" || double.TryParse(trimmed, out _))
        {
            return false;
        }

        return true;
    }
}
