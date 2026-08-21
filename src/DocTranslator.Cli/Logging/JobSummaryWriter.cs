using System.Globalization;
using System.Text;
using DocTranslator.Core.Models;
using DocTranslator.Core.Telemetry;

namespace DocTranslator.Cli.Logging;

public interface IJobSummaryWriter
{
    Task WriteAsync(
        TranslationRunSummary summary,
        int translatedFilesCount,
        string? pullRequestUrl,
        bool dryRun,
        TimeSpan elapsed,
        CancellationToken cancellationToken);

    Task WriteCostEstimateAsync(CostEstimate estimate, CancellationToken cancellationToken);
}

/// <summary>
/// Appends a rich Markdown execution report to <c>GITHUB_STEP_SUMMARY</c> - the file GitHub
/// Actions renders as the job's "Summary" tab. A no-op when that env var isn't set (local dev,
/// non-Actions CI), so this is always safe to call unconditionally.
/// </summary>
public sealed class JobSummaryWriter(ITokenUsageTracker tokenUsageTracker) : IJobSummaryWriter
{
    public async Task WriteCostEstimateAsync(CostEstimate estimate, CancellationToken cancellationToken)
    {
        var summaryFile = GetSummaryFileOrNull();
        if (summaryFile is null)
        {
            return;
        }

        var sb = new StringBuilder();
        sb.AppendLine("## doc-translator-action cost estimate").AppendLine();
        sb.Append("- Files: ").AppendLine(estimate.Files.ToString(CultureInfo.InvariantCulture));
        sb.Append("- Chunk/language pairs: ").Append(estimate.TotalPairs.ToString(CultureInfo.InvariantCulture))
          .Append(" (").Append(estimate.CachedPairs.ToString(CultureInfo.InvariantCulture)).AppendLine(" already cached)");
        sb.Append("- Estimated input tokens: ~").AppendLine(estimate.EstimatedTokens.ToString(CultureInfo.InvariantCulture));
        sb.AppendLine().AppendLine(CostEstimate.Note);

        await File.AppendAllTextAsync(summaryFile, sb.ToString(), cancellationToken).ConfigureAwait(false);
    }

    public async Task WriteAsync(
        TranslationRunSummary summary,
        int translatedFilesCount,
        string? pullRequestUrl,
        bool dryRun,
        TimeSpan elapsed,
        CancellationToken cancellationToken)
    {
        var summaryFile = GetSummaryFileOrNull();
        if (summaryFile is null)
        {
            return;
        }

        var sb = new StringBuilder();
        sb.AppendLine("## 🚀 doc-translator-action summary").AppendLine();

        sb.AppendLine("| Metric | Value |");
        sb.AppendLine("| --- | --- |");
        sb.AppendLine(CultureInfo.InvariantCulture, $"| Files translated | {translatedFilesCount} |");
        if (summary.Languages.Count > 0)
        {
            sb.AppendLine(CultureInfo.InvariantCulture, $"| Languages | {string.Join(", ", summary.Languages.Select(l => l.TargetLanguage))} |");
        }

        if (summary.TotalChunkPairs > 0)
        {
            sb.AppendLine(CultureInfo.InvariantCulture,
                $"| Chunk/language pairs | {summary.TotalChunkPairs} ({summary.CacheHitRatePercent:F0}% from cache) |");
        }

        if (tokenUsageTracker.TotalTokens > 0)
        {
            sb.AppendLine(CultureInfo.InvariantCulture,
                $"| Token usage | {tokenUsageTracker.TotalTokens} ({tokenUsageTracker.TotalPromptTokens} prompt / {tokenUsageTracker.TotalCompletionTokens} completion) |");
        }

        var preserved = summary.DescribePreservedContent();
        if (preserved is not null)
        {
            sb.AppendLine(CultureInfo.InvariantCulture, $"| Preserved untouched | {preserved} |");
        }

        sb.AppendLine(CultureInfo.InvariantCulture, $"| Execution time | {ConsoleSummaryWriter.FormatElapsed(elapsed)} |");
        sb.AppendLine(CultureInfo.InvariantCulture, $"| Mode | {(dryRun ? "dry run" : "push + pull request")} |");
        if (!string.IsNullOrEmpty(pullRequestUrl))
        {
            sb.AppendLine(CultureInfo.InvariantCulture, $"| Pull request | {pullRequestUrl} |");
        }

        sb.AppendLine();

        if (summary.Languages.Count > 0)
        {
            sb.AppendLine("### Per-language breakdown").AppendLine();
            sb.AppendLine("| Language | Chunks translated | From cache |");
            sb.AppendLine("| --- | --- | --- |");
            foreach (var language in summary.Languages)
            {
                sb.AppendLine(CultureInfo.InvariantCulture, $"| {language.TargetLanguage} | {language.ChunksTranslated} | {language.ChunksFromCache} |");
            }

            sb.AppendLine();
        }

        AppendListSection(sb, "Stale translations detected", summary.DriftWarnings);
        AppendListSection(sb, "Glossary warnings", summary.GlossaryWarnings);
        AppendListSection(sb, "Self-healed (marker repaired after retry)", summary.SelfHealedChunks);
        AppendListSection(sb, "Left untranslated (markers kept dropping after repair attempts)", summary.UnrecoverableChunks);
        AppendListSection(sb, "Skipped (unexpected reconstruction failure)", summary.Errors);

        await File.AppendAllTextAsync(summaryFile, sb.ToString(), cancellationToken).ConfigureAwait(false);
    }

    private static string? GetSummaryFileOrNull()
    {
        var summaryFile = Environment.GetEnvironmentVariable("GITHUB_STEP_SUMMARY");
        return string.IsNullOrWhiteSpace(summaryFile) ? null : summaryFile;
    }

    private static void AppendListSection(StringBuilder sb, string title, List<string> items)
    {
        if (items.Count == 0)
        {
            return;
        }

        sb.Append("### ").AppendLine(title).AppendLine();
        foreach (var item in items)
        {
            sb.Append("- ").AppendLine(item);
        }

        sb.AppendLine();
    }
}
