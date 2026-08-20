using DocTranslator.Core.Models;
using DocTranslator.LLM.Batching;
using DocTranslator.LLM.Dto;
using DocTranslator.LLM.Exceptions;
using DocTranslator.LLM.Prompting;
using DocTranslator.LLM.Retry;
using Microsoft.Extensions.AI;

namespace DocTranslator.LLM.Services;

/// <summary>
/// The single <see cref="ILlmTranslationService"/> implementation, shared by all three providers.
/// Each vendor's official SDK already builds an <see cref="IChatClient"/>; this class does the
/// app-specific work - glossary-aware prompting, chunk batching, ChunkId-set validation, and
/// retry-with-repair - once, against that provider-agnostic interface, instead of tripled per
/// vendor. See <see cref="DocTranslator.LLM.Providers.LlmProviderFactory"/> for how the
/// concrete <see cref="IChatClient"/> gets constructed and handed in here.
/// </summary>
public sealed class ChatClientLlmTranslationService(
    IChatClient chatClient,
    string providerName,
    IPromptBuilder promptBuilder,
    IChunkBatcher chunkBatcher,
    ILlmResponseValidator validator,
    int maxRetries = 3) : ILlmTranslationService
{
    public string ProviderName { get; } = providerName;

    /// <summary>
    /// Owns and disposes the <see cref="IChatClient"/> handed in by
    /// <see cref="DocTranslator.LLM.Providers.LlmProviderFactory"/> - each vendor's SDK client
    /// wraps an <c>HttpClient</c>-backed connection that should be released once the run is done.
    /// </summary>
    public void Dispose() => chatClient.Dispose();

    public async Task<IReadOnlyList<TranslatedChunk>> TranslateAsync(
        IReadOnlyList<TranslationChunk> chunks,
        string targetLanguageCode,
        GlossaryContext glossary,
        CancellationToken cancellationToken)
    {
        if (chunks.Count == 0)
        {
            return [];
        }

        var results = new List<TranslatedChunk>(chunks.Count);

        foreach (var batch in chunkBatcher.Batch(chunks))
        {
            var translated = await TranslateBatchWithRetryAsync(batch, targetLanguageCode, glossary, cancellationToken)
                .ConfigureAwait(false);
            results.AddRange(translated);
        }

        return results;
    }

    private async Task<IReadOnlyList<TranslatedChunk>> TranslateBatchWithRetryAsync(
        IReadOnlyList<TranslationChunk> batch,
        string targetLanguageCode,
        GlossaryContext glossary,
        CancellationToken cancellationToken)
    {
        Exception? lastError = null;

        for (var attempt = 1; attempt <= maxRetries; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var messages = promptBuilder.Build(batch, targetLanguageCode, glossary).ToList();

                if (attempt > 1 && lastError is not null)
                {
                    messages.Add(new ChatMessage(
                        ChatRole.User,
                        $"Your previous response was invalid: {lastError.Message} "
                        + $"Return a translation for exactly these chunk ids, no more and no fewer: {string.Join(", ", batch.Select(c => c.ChunkId))}."));
                }

                var response = await chatClient
                    .GetResponseAsync<TranslationBatchResult>(messages, options: null, useJsonSchemaResponseFormat: true, cancellationToken)
                    .ConfigureAwait(false);

                if (!response.TryGetResult(out var result) || result is null)
                {
                    throw new LlmTranslationException("Provider response could not be parsed as the expected structured JSON shape.");
                }

                validator.ValidateChunkIdsMatch(batch, result);

                return result.Translations
                    .Select(t => new TranslatedChunk(t.ChunkId, t.TranslatedText))
                    .ToList();
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                lastError = ex;
            }
        }

        throw new LlmTranslationException(
            $"Translation via '{ProviderName}' failed after {maxRetries} attempt(s).", lastError);
    }
}
