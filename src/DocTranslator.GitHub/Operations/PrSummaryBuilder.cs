using System.Globalization;
using System.Text;
using DocTranslator.Core.Models;

namespace DocTranslator.GitHub.Operations;

public interface IPrSummaryBuilder
{
    string Build(TranslationRunSummary summary);
}

/// <summary>
/// Pure markdown-building from a <see cref="TranslationRunSummary"/> - no Octokit dependency, so
/// it's testable in isolation. <see cref="OctokitGitHubService"/> posts the result as a PR comment.
/// </summary>
public sealed class PrSummaryBuilder : IPrSummaryBuilder
{
    public string Build(TranslationRunSummary summary)
    {
        var sb = new StringBuilder();
        sb.AppendLine("## doc-translator-action summary").AppendLine();

        var actor = Environment.GetEnvironmentVariable("GITHUB_ACTOR");
        if (!string.IsNullOrWhiteSpace(actor))
        {
            sb.AppendLine(CultureInfo.InvariantCulture, $"Thanks @{actor} - here's what changed.").AppendLine();
        }

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

        AppendPreservedSection(sb, summary);
        AppendListSection(sb, "Stale translations detected", summary.DriftWarnings);
        AppendListSection(sb, "Glossary warnings", summary.GlossaryWarnings);
        AppendListSection(sb, "Self-healed (marker repaired after retry)", summary.SelfHealedChunks);
        AppendListSection(sb, "Left untranslated (markers kept dropping after repair attempts)", summary.UnrecoverableChunks);
        AppendListSection(sb, "Skipped (unexpected reconstruction failure)", summary.Errors);
        AppendPreviews(sb, summary.Previews);

        return sb.ToString();
    }

    /// <summary>Reports what was structurally protected this run - the positive counterpart to the warning sections below, so a reviewer sees confirmation, not just problems.</summary>
    private static void AppendPreservedSection(StringBuilder sb, TranslationRunSummary summary)
    {
        var description = summary.DescribePreservedContent();
        if (description is not null)
        {
            sb.Append("**Preserved untouched:** ").Append(description).AppendLine(".").AppendLine();
        }
    }

    private static void AppendPreviews(StringBuilder sb, List<TranslationPreview> previews)
    {
        if (previews.Count == 0)
        {
            return;
        }

        sb.AppendLine("### Preview").AppendLine();
        foreach (var preview in previews)
        {
            sb.Append("<details>\n<summary>").Append(preview.FilePath).Append(" &rarr; ").Append(preview.TargetLanguage).AppendLine("</summary>").AppendLine();
            sb.AppendLine("| Original | Translated |");
            sb.AppendLine("| --- | --- |");
            foreach (var (original, translated) in preview.Paragraphs)
            {
                sb.Append("| ").Append(TableCell(original)).Append(" | ").Append(TableCell(translated)).AppendLine(" |");
            }

            sb.AppendLine().AppendLine("</details>").AppendLine();
        }
    }

    /// <summary>Markdown table cells can't contain a raw pipe or a literal newline - both would break the row.</summary>
    private static string TableCell(string text)
    {
        const int maxLength = 200;
        var oneLine = text.Replace("\r\n", " ").Replace('\n', ' ').Replace("|", "\\|").Trim();
        return oneLine.Length > maxLength ? string.Concat(oneLine.AsSpan(0, maxLength), "…") : oneLine;
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
