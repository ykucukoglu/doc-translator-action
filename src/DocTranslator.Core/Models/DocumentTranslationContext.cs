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
}
