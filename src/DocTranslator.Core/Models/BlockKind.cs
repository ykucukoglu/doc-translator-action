namespace DocTranslator.Core.Models;

/// <summary>
/// The kind of Markdown block a <see cref="TranslationChunk"/> was extracted from.
/// </summary>
public enum BlockKind
{
    Paragraph,
    Heading,
    TableCell,
    ListItem,
    BlockQuote,
}
