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

    /// <summary>A single node/edge/subgraph label extracted from a ```mermaid fenced code block - see <c>MermaidLabelExtractor</c>.</summary>
    MermaidLabel,
}
