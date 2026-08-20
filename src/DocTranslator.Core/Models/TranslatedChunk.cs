namespace DocTranslator.Core.Models;

/// <summary>
/// An LLM's translation of one <see cref="TranslationChunk"/>, matched back by <see cref="ChunkId"/>.
/// </summary>
public sealed record TranslatedChunk(string ChunkId, string TranslatedText);
