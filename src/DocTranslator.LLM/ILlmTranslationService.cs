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
}
