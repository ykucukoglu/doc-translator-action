using System.Globalization;
using System.Text;
using DocTranslator.Core.Models;
using Markdig.Syntax.Inlines;

namespace DocTranslator.Core.Parsing;

/// <summary>
/// Walks the inline tree of a single leaf block (a paragraph or heading's <c>Inline</c>
/// container) and encodes it into a translatable string: natural-language text passes through
/// unchanged, non-text inlines (code spans, autolinks, raw HTML, line breaks) become atomic
/// placeholder tokens, and inlines whose children ARE translatable but whose wrapper carries
/// metadata (emphasis, links/images) become paired synthetic tags. See §1 of the implementation
/// plan for the full rationale.
/// </summary>
public sealed class InlineChunkExtractor
{
    public string Encode(ContainerInline container, BlockReconstructionContext context)
    {
        var builder = new StringBuilder();
        EncodeChildren(container, builder, context);
        return builder.ToString();
    }

    private void EncodeChildren(ContainerInline container, StringBuilder builder, BlockReconstructionContext context)
    {
        for (var inline = container.FirstChild; inline is not null; inline = inline.NextSibling)
        {
            EncodeInline(inline, builder, context);
        }
    }

    private void EncodeInline(Inline inline, StringBuilder builder, BlockReconstructionContext context)
    {
        switch (inline)
        {
            case LiteralInline literal:
                builder.Append(literal.Content.AsSpan());
                break;

            case LineBreakInline lineBreak:
                AppendPlaceholder(builder, context, "BR", lineBreak);
                break;

            case CodeInline code:
                AppendPlaceholder(builder, context, "CODE", code);
                break;

            case AutolinkInline autolink:
                AppendPlaceholder(builder, context, "AUTOLINK", autolink);
                break;

            case HtmlInline html:
                AppendPlaceholder(builder, context, "HTML", html);
                break;

            case HtmlEntityInline htmlEntity:
                // An entity is markup (e.g. "&copy;"), not natural language - preserve it
                // untouched exactly like raw HTML rather than letting it ride through as text.
                AppendPlaceholder(builder, context, "HTML", htmlEntity);
                break;

            case LinkInline link:
                {
                    var index = context.LinkTags.Count;
                    context.LinkTags[index] = new LinkMetadata(link.Url, link.Title, link.IsImage);
                    AppendOpenTag(builder, "link", index);
                    EncodeChildren(link, builder, context);
                    AppendCloseTag(builder, "link", index);
                    break;
                }

            case EmphasisInline emphasis:
                {
                    var index = context.EmphasisTags.Count;
                    context.EmphasisTags[index] = new EmphasisMetadata(emphasis.DelimiterChar, emphasis.DelimiterCount);
                    var tagName = emphasis.DelimiterCount >= 2 ? "strong" : "em";
                    AppendOpenTag(builder, tagName, index);
                    EncodeChildren(emphasis, builder, context);
                    AppendCloseTag(builder, tagName, index);
                    break;
                }

            case ContainerInline nestedContainer:
                // Any other container inline (future/extension inline types) - recurse without
                // adding a marker of our own; we don't know its semantics, so we don't invent one.
                EncodeChildren(nestedContainer, builder, context);
                break;

            default:
                // Unrecognized leaf inline type - treat as an opaque atomic unit rather than
                // risk corrupting something we don't understand.
                AppendPlaceholder(builder, context, "HTML", inline);
                break;
        }
    }

    private static void AppendPlaceholder(StringBuilder builder, BlockReconstructionContext context, string kind, Inline originalInline)
    {
        var index = context.AtomicPlaceholders.Count;
        context.AtomicPlaceholders[index] = originalInline;
        builder.Append('⟦').Append(kind).Append(index.ToString(CultureInfo.InvariantCulture)).Append('⟧');
    }

    private static void AppendOpenTag(StringBuilder builder, string tagName, int index) =>
        builder.Append('<').Append(tagName).Append(index.ToString(CultureInfo.InvariantCulture)).Append('>');

    private static void AppendCloseTag(StringBuilder builder, string tagName, int index) =>
        builder.Append("</").Append(tagName).Append(index.ToString(CultureInfo.InvariantCulture)).Append('>');
}
