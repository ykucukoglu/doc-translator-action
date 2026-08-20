using DocTranslator.Core.Models;
using DocTranslator.Core.Telemetry;
using DocTranslator.LLM.Batching;
using DocTranslator.LLM.Dto;
using DocTranslator.LLM.Exceptions;
using DocTranslator.LLM.Prompting;
using DocTranslator.LLM.Retry;
using Microsoft.Extensions.AI;
using Polly;

namespace DocTranslator.LLM.Services;

/// <summary>
/// The single <see cref="ILlmTranslationService"/> implementation, shared by all three providers.
/// Each vendor's official SDK already builds an <see cref="IChatClient"/>; this class does the
/// app-specific work - glossary-aware prompting, chunk batching, ChunkId-set validation, and
/// retry-with-repair - once, against that provider-agnostic interface, instead of tripled per
/// vendor. See <see cref="DocTranslator.LLM.Providers.LlmProviderFactory"/> for how the
/// concrete <see cref="IChatClient"/> gets constructed and handed in here.
///
/// Batches for one file/language translate concurrently, bounded by a
/// <see cref="SemaphoreSlim"/> sized to <paramref name="maxParallelRequests"/> (action input
/// <c>max-parallel-requests</c>, default 4). Each provider call is wrapped in a Polly resilience
/// pipeline that retries transient HTTP failures (429/5xx) with exponential backoff, independent
/// of the semantic ChunkId-validation retry below it. Token usage from every successful response
/// is recorded into the shared <see cref="ITokenUsageTracker"/>.
/// </summary>
public sealed class ChatClientLlmTranslationService(
    IChatClient chatClient,
    string providerName,
    IPromptBuilder promptBuilder,
    IChunkBatcher chunkBatcher,
    ILlmResponseValidator validator,
    ResiliencePipeline resiliencePipeline,
    ITokenUsageTracker tokenUsageTracker,
    int maxParallelRequests = 4,
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

        var batches = chunkBatcher.Batch(chunks);
        using var semaphore = new SemaphoreSlim(Math.Max(1, maxParallelRequests));

        var batchTasks = batches.Select(async batch =>
        {
            await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                return await TranslateBatchWithRetryAsync(batch, targetLanguageCode, glossary, cancellationToken)
                    .ConfigureAwait(false);
            }
            finally
            {
                semaphore.Release();
            }
        });

        var batchResults = await Task.WhenAll(batchTasks).ConfigureAwait(false);
        return batchResults.SelectMany(translatedBatch => translatedBatch).ToList();
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

                // Transient HTTP failures (429/5xx) are retried here, with backoff, before the
                // outer loop above ever sees them - the outer loop is for malformed/mismatched
                // *responses*, not for waiting out a rate limit.
                var response = await resiliencePipeline.ExecuteAsync(
                    async ct => await chatClient
                        .GetResponseAsync<TranslationBatchResult>(messages, options: null, useJsonSchemaResponseFormat: true, ct)
                        .ConfigureAwait(false),
                    cancellationToken).ConfigureAwait(false);

                if (response.Usage is { } usage)
                {
                    tokenUsageTracker.Record(usage.InputTokenCount ?? 0, usage.OutputTokenCount ?? 0);
                }

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

    public async Task<TranslatedChunk> RepairChunkAsync(
        TranslationChunk chunk,
        string previousTranslatedText,
        IReadOnlyList<string> missingMarkers,
        string targetLanguageCode,
        GlossaryContext glossary,
        CancellationToken cancellationToken)
    {
        var messages = promptBuilder.Build([chunk], targetLanguageCode, glossary).ToList();
        messages.Add(new ChatMessage(ChatRole.Assistant, previousTranslatedText));
        messages.Add(new ChatMessage(
            ChatRole.User,
            "That translation dropped markers that must be preserved EXACTLY, verbatim, in their original relative position: "
            + string.Join("; ", missingMarkers)
            + $". Return the corrected translation for chunk id '{chunk.ChunkId}' only, with every marker restored."));

        try
        {
            var response = await resiliencePipeline.ExecuteAsync(
                async ct => await chatClient
                    .GetResponseAsync<TranslationBatchResult>(messages, options: null, useJsonSchemaResponseFormat: true, ct)
                    .ConfigureAwait(false),
                cancellationToken).ConfigureAwait(false);

            if (response.Usage is { } usage)
            {
                tokenUsageTracker.Record(usage.InputTokenCount ?? 0, usage.OutputTokenCount ?? 0);
            }

            if (response.TryGetResult(out var result) && result is { Translations.Count: > 0 })
            {
                var match = result.Translations.FirstOrDefault(t => t.ChunkId == chunk.ChunkId) ?? result.Translations[0];
                return new TranslatedChunk(chunk.ChunkId, match.TranslatedText);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Fall through: the caller (AstReconstructor) re-validates whatever we return and
            // either retries again or falls back to the untranslated source text - a failed
            // repair attempt here is not itself a fatal error for the run.
        }

        return new TranslatedChunk(chunk.ChunkId, previousTranslatedText);
    }
}
