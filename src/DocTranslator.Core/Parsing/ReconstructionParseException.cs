namespace DocTranslator.Core.Parsing;

/// <summary>
/// Thrown when <see cref="ReconstructionScanner"/> encounters translated text that doesn't match
/// the synthetic placeholder/tag mini-language it produced at extraction time (e.g. the LLM
/// dropped or mangled a marker). Callers should treat this the same as a ChunkId mismatch -
/// as a signal to retry the translation, not as a parsing bug to silently work around.
/// </summary>
public sealed class ReconstructionParseException(string message) : Exception(message);
