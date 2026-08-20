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
        CancellationToken cancellationToken);
}

/// <summary>
/// Appends a rich Markdown execution report to <c>GITHUB_STEP_SUMMARY</c> - the file GitHub
/// Actions renders as the job's "Summary" tab. A no-op when that env var isn't set (local dev,
/// non-Actions CI), so this is always safe to call unconditionally.
/// </summary>
public sealed class JobSummaryWriter(ITokenUsageTracker tokenUsageTracker) : IJobSummaryWriter
{
    public async Task WriteAsync(
        TranslationRunSummary summary,
        int translatedFilesCount,
        string? pullRequestUrl,
        bool dryRun,
        CancellationToken cancellationToken)
    {
        var summaryFile = Environment.GetEnvironmentVariable("GITHUB_STEP_SUMMARY");
        if (string.IsNullOrWhiteSpace(summaryFile))
        {
            return;
        }

        var sb = new StringBuilder();
        sb.AppendLine("## doc-translator-action").AppendLine();
        sb.Append("**Files translated:** ").Append(translatedFilesCount.ToString(CultureInfo.InvariantCulture)).AppendLine("  ");
        sb.Append("**Mode:** ").AppendLine(dryRun ? "dry run" : "push + pull request");

        if (!string.IsNullOrEmpty(pullRequestUrl))
        {
            sb.Append("**Pull request:** ").AppendLine(pullRequestUrl);
        }

        sb.AppendLine();

        if (summary.Languages.Count > 0)
        {
            sb.AppendLine("| Language | Chunks translated | From cache |");
            sb.AppendLine("| --- | --- | --- |");
            foreach (var language in summary.Languages)
            {
                sb.AppendLine(CultureInfo.InvariantCulture, $"| {language.TargetLanguage} | {language.ChunksTranslated} | {language.ChunksFromCache} |");
            }

            sb.AppendLine();
        }

        sb.AppendLine("### Token usage").AppendLine();
        sb.AppendLine("| Prompt | Completion | Total |");
        sb.AppendLine("| --- | --- | --- |");
        sb.AppendLine(
            CultureInfo.InvariantCulture,
            $"| {tokenUsageTracker.TotalPromptTokens} | {tokenUsageTracker.TotalCompletionTokens} | {tokenUsageTracker.TotalTokens} |");
        sb.AppendLine();

        AppendListSection(sb, "Stale translations detected", summary.DriftWarnings);
        AppendListSection(sb, "Glossary warnings", summary.GlossaryWarnings);
        AppendListSection(sb, "Self-healed (marker repaired after retry)", summary.SelfHealedChunks);
        AppendListSection(sb, "Left untranslated (markers kept dropping after repair attempts)", summary.UnrecoverableChunks);
        AppendListSection(sb, "Skipped (unexpected reconstruction failure)", summary.Errors);

        await File.AppendAllTextAsync(summaryFile, sb.ToString(), cancellationToken).ConfigureAwait(false);
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
