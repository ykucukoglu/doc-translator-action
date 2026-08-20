using DocTranslator.Core.Models;

namespace DocTranslator.LLM;

/// <summary>
/// Translates a set of chunks into one target language. Implementations must return exactly one
/// <see cref="TranslatedChunk"/> per input <see cref="TranslationChunk"/>, matched by ChunkId.
/// The app-facing contract is provider-agnostic - see <see cref="Services.ChatClientLlmTranslationService"/>
/// for the single implementation shared by all three providers.
/// </summary>
public interface ILlmTranslationService : IDisposable
{
    string ProviderName { get; }

    Task<IReadOnlyList<TranslatedChunk>> TranslateAsync(
        IReadOnlyList<TranslationChunk> chunks,
        string targetLanguageCode,
        GlossaryContext glossary,
        CancellationToken cancellationToken);

    /// <summary>
    /// Re-translates a single chunk whose previous translation dropped a required placeholder/tag
    /// marker, with a prompt that shows the model its own bad output and exactly which markers
    /// must be restored. Backs the self-healing loop in <c>AstReconstructor.ReconstructAsync</c>
    /// (via <c>Core.Reconstruction.ChunkRepairCallback</c>) - best-effort: the caller re-validates
    /// the result and decides whether to retry again or fall back.
    /// </summary>
    Task<TranslatedChunk> RepairChunkAsync(
        TranslationChunk chunk,
        string previousTranslatedText,
        IReadOnlyList<string> missingMarkers,
        string targetLanguageCode,
        GlossaryContext glossary,
        CancellationToken cancellationToken);
}
