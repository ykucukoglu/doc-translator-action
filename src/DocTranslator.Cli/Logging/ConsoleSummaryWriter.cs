using DocTranslator.Core.Models;
using DocTranslator.Core.Telemetry;

namespace DocTranslator.Cli.Logging;

public interface IConsoleSummaryWriter
{
    void Write(TranslationRunSummary summary, int translatedFilesCount, string? pullRequestUrl, bool dryRun);

    void WriteCostEstimate(CostEstimate estimate);
}

/// <summary>Human-readable summary printed at the end of every run, dry-run or not.</summary>
public sealed class ConsoleSummaryWriter(ITokenUsageTracker tokenUsageTracker) : IConsoleSummaryWriter
{
    public void WriteCostEstimate(CostEstimate estimate)
    {
        Console.WriteLine("=== doc-translator-action cost estimate ===");
        Console.WriteLine($"Files: {estimate.Files}");
        Console.WriteLine($"Chunk/language pairs: {estimate.TotalPairs} ({estimate.CachedPairs} already cached)");
        Console.WriteLine($"Estimated input tokens for the LLM calls this run would make: ~{estimate.EstimatedTokens}");
        Console.WriteLine(CostEstimate.Note);
    }

    public void Write(TranslationRunSummary summary, int translatedFilesCount, string? pullRequestUrl, bool dryRun)
    {
        Console.WriteLine();
        Console.WriteLine("=== doc-translator-action summary ===");
        Console.WriteLine($"Files translated: {translatedFilesCount}");

        foreach (var language in summary.Languages)
        {
            Console.WriteLine($"  [{language.TargetLanguage}] {language.ChunksTranslated} chunk(s) translated, {language.ChunksFromCache} from cache");
        }

        if (tokenUsageTracker.TotalTokens > 0)
        {
            Console.WriteLine(
                $"Token usage: {tokenUsageTracker.TotalPromptTokens} prompt + {tokenUsageTracker.TotalCompletionTokens} completion = {tokenUsageTracker.TotalTokens} total");
        }

        foreach (var warning in summary.GlossaryWarnings)
        {
            Console.WriteLine($"  glossary warning: {warning}");
        }

        foreach (var warning in summary.DriftWarnings)
        {
            Console.WriteLine($"  drift warning: {warning}");
        }

        foreach (var chunkId in summary.SelfHealedChunks)
        {
            Console.WriteLine($"  self-healed: {chunkId}");
        }

        foreach (var warning in summary.UnrecoverableChunks)
        {
            Console.WriteLine($"  left untranslated: {warning}");
        }

        foreach (var error in summary.Errors)
        {
            Console.WriteLine($"  skipped (unexpected reconstruction failure): {error}");
        }

        if (dryRun)
        {
            Console.WriteLine("Dry run - no branch was pushed and no pull request was opened.");
        }
        else if (pullRequestUrl is not null)
        {
            Console.WriteLine($"Pull request: {pullRequestUrl}");
        }
    }
}
