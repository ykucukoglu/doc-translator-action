using Polly;
using Polly.Retry;
using System.ClientModel;
using System.Net.Http;

namespace DocTranslator.LLM.Resilience;

/// <summary>
/// Builds the Polly v8 resilience pipeline used to retry transient HTTP-level failures (429 rate
/// limits, 5xx server errors, connection failures) with exponential backoff. This is distinct
/// from - and wraps only the raw provider call inside - the semantic retry loop in
/// <see cref="DocTranslator.LLM.Services.ChatClientLlmTranslationService"/>, which retries
/// malformed/ChunkId-mismatched *responses* by repairing the prompt, not by waiting out a rate
/// limit.
/// </summary>
public static class ChatClientResiliencePipeline
{
    public static ResiliencePipeline Create(int maxRetryAttempts = 3) =>
        new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                ShouldHandle = new PredicateBuilder()
                    // Covers the OpenAI SDK, which is built on System.ClientModel and surfaces the
                    // HTTP status via ClientResultException.Status.
                    .Handle<ClientResultException>(ex => IsTransientStatus(ex.Status))
                    // Covers SDKs built directly on HttpClient (Anthropic, Google.GenAI): a null
                    // StatusCode means the request never got a response at all (DNS/connection
                    // failure), which is just as worth retrying as a 429/5xx.
                    .Handle<HttpRequestException>(ex => ex.StatusCode is null || IsTransientStatus((int)ex.StatusCode.Value)),
                MaxRetryAttempts = maxRetryAttempts,
                BackoffType = DelayBackoffType.Exponential,
                Delay = TimeSpan.FromSeconds(1),
                UseJitter = true,
            })
            .Build();

    private static bool IsTransientStatus(int statusCode) => statusCode == 429 || statusCode >= 500;
}
