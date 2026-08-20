using DocTranslator.Core.Models;

namespace DocTranslator.LLM.Batching;

public interface IChunkBatcher
{
    /// <summary>Groups chunks into batches that stay under an approximate token budget per request.</summary>
    IReadOnlyList<IReadOnlyList<TranslationChunk>> Batch(IReadOnlyList<TranslationChunk> chunks);
}

/// <summary>
/// Batches by an approximate char/4 token-count heuristic (v1). A single oversized chunk is
/// still sent alone rather than dropped or truncated - it's simply its own batch.
/// </summary>
public sealed class ChunkBatcher(int maxTokensPerBatch = 4000) : IChunkBatcher
{
    public IReadOnlyList<IReadOnlyList<TranslationChunk>> Batch(IReadOnlyList<TranslationChunk> chunks)
    {
        var batches = new List<IReadOnlyList<TranslationChunk>>();
        var current = new List<TranslationChunk>();
        var currentTokens = 0;

        foreach (var chunk in chunks)
        {
            var chunkTokens = EstimateTokens(chunk.SourceText);

            if (current.Count > 0 && currentTokens + chunkTokens > maxTokensPerBatch)
            {
                batches.Add(current);
                current = [];
                currentTokens = 0;
            }

            current.Add(chunk);
            currentTokens += chunkTokens;
        }

        if (current.Count > 0)
        {
            batches.Add(current);
        }

        return batches;
    }

    private static int EstimateTokens(string text) => Math.Max(1, text.Length / 4);
}
