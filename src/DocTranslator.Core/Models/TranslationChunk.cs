namespace DocTranslator.Core.Models;

/// <summary>
/// One translatable unit of text extracted from a Markdown leaf block. This is the only
/// representation of a chunk that is ever sent across the wire to an LLM provider - it carries
/// no reference back to the Markdig AST.
/// </summary>
/// <param name="ChunkId">Stable identifier used to splice the translated text back into the AST.</param>
/// <param name="SourceText">
/// The block's natural-language content, encoded with atomic placeholders (e.g. code spans,
/// autolinks) and paired synthetic tags (emphasis, links) so inline structure survives translation.
/// </param>
/// <param name="ContentHash">SHA-256 of <paramref name="SourceText"/>, used as the translation cache key.</param>
public sealed record TranslationChunk(
    string ChunkId,
    string SourceText,
    string ContentHash,
    BlockKind BlockKind,
    string SourceFilePath);
