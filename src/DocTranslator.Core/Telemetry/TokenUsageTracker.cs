namespace DocTranslator.Core.Telemetry;

public interface ITokenUsageTracker
{
    void Record(long promptTokens, long completionTokens);

    long TotalPromptTokens { get; }

    long TotalCompletionTokens { get; }

    long TotalTokens { get; }
}

/// <summary>
/// Thread-safe accumulator for LLM token usage across a whole run. Batches now translate
/// concurrently (see <c>ActionOptions.MaxParallelRequests</c> / <c>SemaphoreSlim</c> in
/// <c>ChatClientLlmTranslationService</c>), so every write here must tolerate concurrent callers.
/// Lives in Core (not LLM or Cli) so both the LLM layer (writer) and the Cli layer (reader, for
/// console/Job Summary output) can share one instance without a bad dependency direction.
/// </summary>
public sealed class TokenUsageTracker : ITokenUsageTracker
{
    private long _promptTokens;
    private long _completionTokens;

    public void Record(long promptTokens, long completionTokens)
    {
        Interlocked.Add(ref _promptTokens, promptTokens);
        Interlocked.Add(ref _completionTokens, completionTokens);
    }

    public long TotalPromptTokens => Interlocked.Read(ref _promptTokens);

    public long TotalCompletionTokens => Interlocked.Read(ref _completionTokens);

    public long TotalTokens => TotalPromptTokens + TotalCompletionTokens;
}
