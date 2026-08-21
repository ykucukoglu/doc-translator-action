namespace DocTranslator.Core.Models;

/// <summary>One extracted piece of text's position within some larger raw text, and the exact source text found there (quotes, if any, stripped). Shared shape for both <c>MermaidLabelExtractor</c> and <c>FrontmatterFieldExtractor</c>.</summary>
public readonly record struct ExtractedTextSpan(int Start, int Length, string Text);

/// <summary>
/// One ```mermaid fenced code block's original raw content plus the labels extracted from it,
/// each already tied to the <see cref="TranslationChunk"/> id sent for translation. Reconstruction
/// splices translated text back into a copy of <see cref="OriginalRawText"/> and finds/replaces
/// that exact original block in the rendered document - see <c>AstReconstructor</c>. Deliberately
/// separate from <see cref="BlockReconstructionContext"/>: mermaid content isn't Markdown, so there
/// is no Inline tree to splice into.
/// </summary>
public sealed class MermaidBlockContext
{
    public required string OriginalRawText { get; init; }

    public required IReadOnlyList<(string ChunkId, ExtractedTextSpan Span)> Labels { get; init; }
}
