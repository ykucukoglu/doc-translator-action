namespace DocTranslator.Core.Reconstruction;

/// <summary>
/// Result of <see cref="IAstReconstructor.ReconstructAsync"/>. <see cref="RepairedChunkIds"/> and
/// <see cref="UnrecoverableChunkIds"/> are always disjoint and both empty in the common case
/// (every chunk's markers survived translation on the first attempt).
/// </summary>
/// <param name="Markdown">The fully reconstructed, rendered document.</param>
/// <param name="RepairedChunkIds">Chunks that needed 1-2 self-healing repair attempts but ultimately succeeded.</param>
/// <param name="UnrecoverableChunkIds">
/// Chunks where the LLM repeatedly dropped required markers even after repair attempts (or no
/// repair delegate was supplied); these were spliced back in using their original, untranslated
/// source text rather than corrupting the document.
/// </param>
public sealed record ReconstructionOutcome(
    string Markdown,
    IReadOnlyList<string> RepairedChunkIds,
    IReadOnlyList<string> UnrecoverableChunkIds);
