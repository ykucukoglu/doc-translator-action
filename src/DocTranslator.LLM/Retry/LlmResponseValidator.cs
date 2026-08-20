using DocTranslator.Core.Models;
using DocTranslator.LLM.Dto;
using DocTranslator.LLM.Exceptions;

namespace DocTranslator.LLM.Retry;

public interface ILlmResponseValidator
{
    /// <summary>
    /// Verifies the returned ChunkId set is exactly equal (no missing, no extra, no duplicates)
    /// to the requested batch. Throws <see cref="LlmTranslationException"/> on any mismatch -
    /// the caller is expected to retry with a repair prompt, not to patch around the gap.
    /// </summary>
    void ValidateChunkIdsMatch(IReadOnlyList<TranslationChunk> requested, TranslationBatchResult result);
}

public sealed class LlmResponseValidator : ILlmResponseValidator
{
    public void ValidateChunkIdsMatch(IReadOnlyList<TranslationChunk> requested, TranslationBatchResult result)
    {
        var requestedIds = requested.Select(c => c.ChunkId).ToHashSet();
        var returnedIds = result.Translations.Select(t => t.ChunkId).ToList();

        var missing = requestedIds.Except(returnedIds).ToList();
        var extra = returnedIds.Except(requestedIds).ToList();
        var duplicates = returnedIds.GroupBy(id => id).Where(g => g.Count() > 1).Select(g => g.Key).ToList();

        if (missing.Count == 0 && extra.Count == 0 && duplicates.Count == 0)
        {
            return;
        }

        var problems = new List<string>();
        if (missing.Count > 0)
        {
            problems.Add($"missing: {string.Join(", ", missing)}");
        }

        if (extra.Count > 0)
        {
            problems.Add($"unexpected: {string.Join(", ", extra)}");
        }

        if (duplicates.Count > 0)
        {
            problems.Add($"duplicated: {string.Join(", ", duplicates)}");
        }

        throw new LlmTranslationException($"Chunk id mismatch in provider response ({string.Join("; ", problems)}).");
    }
}
