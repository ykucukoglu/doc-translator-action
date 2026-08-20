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
    public static ResiliencePipeline Create(int maxRetryAttempts = 5) =>
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
                // A 1s base (the original value) tops out around 7s of total backoff across 3
                // attempts - nowhere near enough to survive a free-tier per-minute rate limit
                // window, which is exactly what a real dogfooding run against Gemini's free tier
                // hit: a burst of concurrent requests drew several 429s in a row, and by the time
                // the (short) backoff elapsed the same 60s window was still active. A 3s base over
                // 5 attempts spans roughly a minute (3+6+12+24+48s before jitter), which actually
                // has a chance of crossing into the next quota window.
                Delay = TimeSpan.FromSeconds(3),
                UseJitter = true,
                // Providers that return a Retry-After header on 429 are telling us exactly how
                // long the current quota window has left - waiting that long (plus a little
                // headroom) beats guessing with exponential backoff, which just as easily waits
                // too little (retries land in the same window) or too much. Only ClientResultException
                // (the OpenAI SDK, built on System.ClientModel) exposes response headers on the
                // exception; HttpRequestException (Anthropic, Google.GenAI) doesn't carry them, so
                // those fall through to the exponential default below.
                DelayGenerator = args =>
                {
                    if (args.Outcome.Exception is ClientResultException clientEx
                        && TryGetRetryAfter(clientEx, out var retryAfter))
                    {
                        return ValueTask.FromResult<TimeSpan?>(retryAfter);
                    }

                    return ValueTask.FromResult<TimeSpan?>(null);
                },
            })
            .Build();

    private static bool IsTransientStatus(int statusCode) => statusCode == 429 || statusCode >= 500;

    private static bool TryGetRetryAfter(ClientResultException exception, out TimeSpan delay)
    {
        delay = default;

        var headers = exception.GetRawResponse()?.Headers;
        if (headers is null || !headers.TryGetValue("Retry-After", out var value))
        {
            return false;
        }

        // Retry-After is either delay-seconds or an HTTP-date (RFC 9110 §10.2.3) - try both forms.
        if (int.TryParse(value, out var seconds) && seconds >= 0)
        {
            delay = TimeSpan.FromSeconds(seconds);
            return true;
        }

        if (DateTimeOffset.TryParse(value, out var when))
        {
            var untilThen = when - DateTimeOffset.UtcNow;
            delay = untilThen > TimeSpan.Zero ? untilThen : TimeSpan.Zero;
            return true;
        }

        return false;
    }
}
