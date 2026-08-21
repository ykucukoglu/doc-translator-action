using System.Text.RegularExpressions;
using DocTranslator.Core.Models;

namespace DocTranslator.Core.Diagrams;

/// <summary>
/// Extracts human-readable labels from a ```mermaid fenced code block's raw text, so they can be
/// translated while every structural token (node ids, arrows, keywords, the diagram type
/// declaration) stays byte-for-byte untouched. Deliberately narrow, not a mermaid parser: scoped
/// to <c>flowchart</c>/<c>graph</c> diagrams only (the most common type) and to a fixed, safe set
/// of label shapes. A line whose label content doesn't cleanly match one of those shapes is left
/// alone rather than guessed at - an unrecognized diagram/line staying untranslated is always
/// preferable to a corrupted one that no longer renders.
/// </summary>
public static class MermaidLabelExtractor
{
    // Each shape's content is either a double-quoted string (mermaid's own escape for labels that
    // need to contain delimiter characters) or a run of characters containing none of that shape's
    // own delimiters - never a guess past that. Written out per shape (rather than built from one
    // shared template) so each pattern reads plainly instead of through an extra layer of escaping.
    private const string Q = @"""(?:[^""\\]|\\.)*""";

    private static readonly Regex NodeLabelPattern = new(
        @"[A-Za-z0-9_.-]+(?:" +
        @"\(\[(?<stadium>" + Q + @"|[^\[\]()]+)\]\)" +
        @"|\[\[(?<subroutine>" + Q + @"|[^\[\]]+)\]\]" +
        @"|\[\((?<cylinder>" + Q + @"|[^\[\]()]+)\)\]" +
        @"|\(\((?<circle>" + Q + @"|[^()]+)\)\)" +
        @"|\{\{(?<hexagon>" + Q + @"|[^{}]+)\}\}" +
        @"|\[(?<rectangle>" + Q + @"|[^\[\]]+)\]" +
        @"|\((?<round>" + Q + @"|[^()]+)\)" +
        @"|\{(?<diamond>" + Q + @"|[^{}]+)\}" +
        @")",
        RegexOptions.Compiled);

    private static readonly Regex EdgeLabelPattern = new(
        @"\|(?<label>" + Q + @"|[^|]+)\|", RegexOptions.Compiled);

    // Mermaid allows both "subgraph id[Title]" and "subgraph id [Title]" (space before the bracket
    // is optional) - NodeLabelPattern alone can't find this form since it requires the bracket to
    // immediately follow the id with no space at all. Tried first; only a plain "subgraph Title"
    // with no bracket anywhere falls through to the bare-title pattern below.
    private static readonly Regex SubgraphBracketPattern = new(
        @"^\s*subgraph\s+[\w.-]+\s*\[(?<label>" + Q + @"|[^\[\]]+)\]",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex SubgraphBareTitlePattern = new(
        @"^\s*subgraph\s+(?<label>[^\r\n\[\]]+?)\s*$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly string[] NodeShapeGroupNames =
        ["stadium", "subroutine", "cylinder", "circle", "hexagon", "rectangle", "round", "diamond"];

    /// <summary>Whether this block's own first non-blank, non-directive line declares a <c>flowchart</c>/<c>graph</c> diagram - the only type this extractor handles.</summary>
    public static bool IsSupportedDiagram(string rawText)
    {
        foreach (var rawLine in rawText.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith("%%", StringComparison.Ordinal))
            {
                continue; // blank line or a %%{...}%%/comment directive
            }

            return line.StartsWith("flowchart", StringComparison.OrdinalIgnoreCase)
                || line.StartsWith("graph", StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    /// <summary>Every label span found, in source order. Offsets are into <paramref name="rawText"/> exactly as given (no normalization).</summary>
    public static IReadOnlyList<MermaidLabelSpan> ExtractLabels(string rawText)
    {
        if (!IsSupportedDiagram(rawText))
        {
            return [];
        }

        var spans = new List<MermaidLabelSpan>();
        var lineStart = 0;

        foreach (var line in rawText.Split('\n'))
        {
            if (!line.TrimStart().StartsWith("%%", StringComparison.Ordinal))
            {
                ExtractFromLine(line, lineStart, spans);
            }

            lineStart += line.Length + 1; // +1 for the '\n' Split consumed
        }

        return spans;
    }

    private static void ExtractFromLine(string line, int lineStart, List<MermaidLabelSpan> spans)
    {
        if (line.TrimStart().StartsWith("subgraph", StringComparison.OrdinalIgnoreCase))
        {
            var bracketMatch = SubgraphBracketPattern.Match(line);
            var titleMatch = bracketMatch.Success ? bracketMatch : SubgraphBareTitlePattern.Match(line);
            if (titleMatch.Success)
            {
                AddSpan(spans, lineStart, titleMatch.Groups["label"]);
            }

            return; // a subgraph title line is only ever that - no node/edge labels share it
        }

        foreach (Match match in NodeLabelPattern.Matches(line))
        {
            AddSpan(spans, lineStart, FirstSucceeded(match, NodeShapeGroupNames));
        }

        foreach (Match match in EdgeLabelPattern.Matches(line))
        {
            AddSpan(spans, lineStart, match.Groups["label"]);
        }
    }

    private static Group FirstSucceeded(Match match, string[] groupNames)
    {
        foreach (var name in groupNames)
        {
            if (match.Groups[name].Success)
            {
                return match.Groups[name];
            }
        }

        throw new InvalidOperationException("NodeLabelPattern matched but no named alternative captured - regex/group name mismatch.");
    }

    private static void AddSpan(List<MermaidLabelSpan> spans, int lineStart, Group group)
    {
        var raw = group.Value;
        var start = lineStart + group.Index;
        var length = raw.Length;
        var text = raw;

        if (raw.Length >= 2 && raw[0] == '"' && raw[^1] == '"')
        {
            // The span is narrowed to exclude the quote characters themselves, so splicing a
            // translation back in later only ever replaces what was between them - the quotes stay
            // put as part of the untouched surrounding text, with no need to track "was this
            // quoted" separately or re-add them at reconstruction time.
            start += 1;
            length -= 2;
            text = raw[1..^1];
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            return; // nothing translatable here (e.g. an empty "" label) - leave it alone
        }

        spans.Add(new MermaidLabelSpan(start, length, text));
    }
}
