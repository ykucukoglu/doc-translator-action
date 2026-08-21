using DocTranslator.Core.Models;

namespace DocTranslator.LLM.Services;

/// <summary>
/// Wraps a primary <see cref="ILlmTranslationService"/> with a second one to fall back to if the
/// primary exhausts its own retries (rate limit, outage, quota) - opt-in via the
/// <c>llm-fallback-provider</c> input, never inferred from which provider keys happen to be set.
/// A single fallback, not a chain: if the fallback also fails, that exception propagates as-is.
/// </summary>
public sealed class FallbackLlmTranslationService(ILlmTranslationService primary, ILlmTranslationService fallback) : ILlmTranslationService
{
    public string ProviderName => primary.ProviderName;

    public void Dispose()
    {
        primary.Dispose();
        fallback.Dispose();
    }

    public async Task<IReadOnlyList<TranslatedChunk>> TranslateAsync(
        IReadOnlyList<TranslationChunk> chunks,
        string targetLanguageCode,
        GlossaryContext glossary,
        CancellationToken cancellationToken,
        string sourceLanguage = "auto")
    {
        try
        {
            return await primary.TranslateAsync(chunks, targetLanguageCode, glossary, cancellationToken, sourceLanguage).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Console.WriteLine($"::warning::Primary LLM provider '{primary.ProviderName}' failed, falling back to '{fallback.ProviderName}': {ex.Message}");
            return await fallback.TranslateAsync(chunks, targetLanguageCode, glossary, cancellationToken, sourceLanguage).ConfigureAwait(false);
        }
    }

    public async Task<TranslatedChunk> RepairChunkAsync(
        TranslationChunk chunk,
        string previousTranslatedText,
        IReadOnlyList<string> missingMarkers,
        string targetLanguageCode,
        GlossaryContext glossary,
        CancellationToken cancellationToken,
        string sourceLanguage = "auto")
    {
        try
        {
            return await primary.RepairChunkAsync(chunk, previousTranslatedText, missingMarkers, targetLanguageCode, glossary, cancellationToken, sourceLanguage).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Console.WriteLine($"::warning::Primary LLM provider '{primary.ProviderName}' failed during repair, falling back to '{fallback.ProviderName}': {ex.Message}");
            return await fallback.RepairChunkAsync(chunk, previousTranslatedText, missingMarkers, targetLanguageCode, glossary, cancellationToken, sourceLanguage).ConfigureAwait(false);
        }
    }
}
