using System.Text;

namespace DocTranslator.Core.Parsing;

/// <summary>
/// Hand-written recursive-descent scanner that turns translated text back into a tree of
/// <see cref="EncodedNode"/>s. This intentionally does NOT use Regex - but note that this is a
/// synthetic mini-format this codebase fully controls (placeholder/tag markers written by
/// <see cref="InlineChunkExtractor"/>), not a scan for translation boundaries over arbitrary
/// Markdown. That's the distinction the "no regex for AST parsing" principle is about; it
/// doesn't apply here.
/// </summary>
public sealed class ReconstructionScanner
{
    private const char PlaceholderOpen = '⟦'; // ⟦
    private const char PlaceholderClose = '⟧'; // ⟧

    private static readonly string[] KnownTagNames = ["em", "strong", "link"];

    // Kept as an instance method (not static) for consistency with every other service in this
    // codebase, all of which are instantiated and could be DI-registered/mocked the same way,
    // even though this particular one happens not to hold instance state today.
#pragma warning disable CA1822
    public IReadOnlyList<EncodedNode> Parse(string text)
    {
        var position = 0;
        var nodes = ParseNodes(text, ref position, closingTag: null);

        if (position < text.Length)
        {
            throw new ReconstructionParseException(
                $"Unexpected trailing content at position {position}: unmatched closing tag or malformed markup.");
        }

        return nodes;
    }
#pragma warning restore CA1822

    private static List<EncodedNode> ParseNodes(string text, ref int position, string? closingTag)
    {
        var nodes = new List<EncodedNode>();
        var literal = new StringBuilder();

        void FlushLiteral()
        {
            if (literal.Length > 0)
            {
                nodes.Add(new TextRunNode(literal.ToString()));
                literal.Clear();
            }
        }

        while (position < text.Length)
        {
            var ch = text[position];

            if (ch == PlaceholderOpen)
            {
                FlushLiteral();
                nodes.Add(ParsePlaceholder(text, ref position));
                continue;
            }

            if (ch == '<')
            {
                if (TryMatchClosingTag(text, position, out var closedTag, out var afterClose))
                {
                    if (closingTag is not null && closedTag == closingTag)
                    {
                        FlushLiteral();
                        position = afterClose;
                        return nodes;
                    }

                    throw new ReconstructionParseException(
                        $"Unmatched closing tag '</{closedTag}>' at position {position}"
                        + (closingTag is null ? " (no open tag)." : $" (expected '</{closingTag}>')."));
                }

                if (TryMatchOpeningTag(text, position, out var tagName, out var index, out var afterOpen))
                {
                    FlushLiteral();
                    position = afterOpen;
                    var fullTag = tagName + index.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    var children = ParseNodes(text, ref position, fullTag);
                    nodes.Add(new TaggedSpanNode(tagName, index, children));
                    continue;
                }
            }

            literal.Append(ch);
            position++;
        }

        if (closingTag is not null)
        {
            throw new ReconstructionParseException($"Missing closing tag '</{closingTag}>'.");
        }

        FlushLiteral();
        return nodes;
    }

    private static PlaceholderRefNode ParsePlaceholder(string text, ref int position)
    {
        var start = position;
        position++; // consume PlaceholderOpen

        var kindStart = position;
        while (position < text.Length && char.IsLetter(text[position]))
        {
            position++;
        }

        var kindLength = position - kindStart;

        var digitsStart = position;
        while (position < text.Length && char.IsAsciiDigit(text[position]))
        {
            position++;
        }

        var digitsLength = position - digitsStart;

        if (kindLength == 0 || digitsLength == 0 || position >= text.Length || text[position] != PlaceholderClose)
        {
            throw new ReconstructionParseException($"Malformed placeholder token starting at position {start}.");
        }

        var index = int.Parse(text.AsSpan(digitsStart, digitsLength), System.Globalization.CultureInfo.InvariantCulture);
        position++; // consume PlaceholderClose
        return new PlaceholderRefNode(index);
    }

    /// <summary>Matches an opening tag like <c>&lt;em0&gt;</c> at <paramref name="position"/> (which must point at '&lt;').</summary>
    private static bool TryMatchOpeningTag(string text, int position, out string tagName, out int index, out int afterOpen)
    {
        tagName = string.Empty;
        index = 0;
        afterOpen = position;

        var p = position + 1; // skip '<'
        var nameStart = p;
        while (p < text.Length && char.IsAsciiLetter(text[p]))
        {
            p++;
        }

        if (p == nameStart)
        {
            return false;
        }

        var name = text[nameStart..p];
        if (Array.IndexOf(KnownTagNames, name) < 0)
        {
            return false;
        }

        var digitsStart = p;
        while (p < text.Length && char.IsAsciiDigit(text[p]))
        {
            p++;
        }

        if (p == digitsStart || p >= text.Length || text[p] != '>')
        {
            return false;
        }

        tagName = name;
        index = int.Parse(text.AsSpan(digitsStart, p - digitsStart), System.Globalization.CultureInfo.InvariantCulture);
        afterOpen = p + 1;
        return true;
    }

    /// <summary>Matches a closing tag like <c>&lt;/em0&gt;</c> at <paramref name="position"/> (which must point at '&lt;').</summary>
    private static bool TryMatchClosingTag(string text, int position, out string closedTag, out int afterClose)
    {
        closedTag = string.Empty;
        afterClose = position;

        if (position + 1 >= text.Length || text[position + 1] != '/')
        {
            return false;
        }

        var p = position + 2;
        var nameStart = p;
        while (p < text.Length && char.IsAsciiLetter(text[p]))
        {
            p++;
        }

        if (p == nameStart)
        {
            return false;
        }

        var name = text[nameStart..p];
        if (Array.IndexOf(KnownTagNames, name) < 0)
        {
            return false;
        }

        var digitsStart = p;
        while (p < text.Length && char.IsAsciiDigit(text[p]))
        {
            p++;
        }

        if (p == digitsStart || p >= text.Length || text[p] != '>')
        {
            return false;
        }

        closedTag = name + text[digitsStart..p];
        afterClose = p + 1;
        return true;
    }
}
