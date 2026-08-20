using DocTranslator.Core.Models;

namespace DocTranslator.LLM.Providers;

/// <summary>
/// Trivial marker-wrapping passthrough - does not go through <c>IChatClient</c> or any network
/// call at all. Opt in via <c>INPUT_LLM_PROVIDER=fake</c> / <c>--use-fake-llm</c> for local
/// smoke testing without API keys.
/// </summary>
public sealed class FakeTranslationService : ILlmTranslationService
{
    public string ProviderName => "fake";

    public Task<IReadOnlyList<TranslatedChunk>> TranslateAsync(
        IReadOnlyList<TranslationChunk> chunks,
        string targetLanguageCode,
        GlossaryContext glossary,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<TranslatedChunk> result = chunks
            .Select(c => new TranslatedChunk(c.ChunkId, $"[{targetLanguageCode}] {c.SourceText}"))
            .ToList();

        return Task.FromResult(result);
    }
}
