using Markdig.Syntax;

namespace DocTranslator.Core.Models;

/// <summary>
/// The result of parsing and extracting one Markdown document: the chunks ready to be sent for
/// translation, plus the live in-memory mapping needed to reconstruct the document afterward.
/// </summary>
public sealed class DocumentTranslationContext
{
    public required string SourceFilePath { get; init; }

    public required MarkdownDocument MarkdownDocument { get; init; }

    public required IReadOnlyList<TranslationChunk> Chunks { get; init; }

    /// <summary>
    /// In-memory only, never serialized: maps each chunk's <see cref="TranslationChunk.ChunkId"/>
    /// back to the live AST node (and its placeholder/tag side tables) it was extracted from.
    /// </summary>
    public required IReadOnlyDictionary<string, BlockReconstructionContext> ReconstructionMap { get; init; }

    /// <summary>
    /// Verbatim source text (including the <c>---</c> delimiters) of a leading YAML frontmatter
    /// block, captured before the block is removed from <see cref="MarkdownDocument"/> - Markdig's
    /// normalizing renderer doesn't round-trip frontmatter delimiters correctly, so this is spliced
    /// back onto the output directly instead of relying on that render path. Null if the document
    /// has no frontmatter.
    /// </summary>
    public string? FrontmatterRawText { get; init; }

    /// <summary>Count of fenced/indented code blocks skipped entirely at the block level (never chunked, never sent to the LLM) - reported in the PR summary alongside inline code/link/glossary preservation counts.</summary>
    public int CodeBlockCount { get; init; }

    /// <summary>
    /// One entry per ```mermaid fenced code block whose labels were extracted (only when
    /// <c>translate-mermaid-diagrams</c> is on) - each label is also present in <see cref="Chunks"/>
    /// (<see cref="BlockKind.MermaidLabel"/>) so it goes through the same LLM/cache pipeline as
    /// everything else, but reconstruction splices it back via this list instead of
    /// <see cref="ReconstructionMap"/>, since mermaid content has no Markdig Inline tree to splice into.
    /// </summary>
    public IReadOnlyList<MermaidBlockContext> MermaidBlocks { get; init; } = [];
}
