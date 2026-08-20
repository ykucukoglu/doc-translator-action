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

        AppendListSection(sb, "Stale translations detected", summary.DriftWarnings);
        AppendListSection(sb, "Glossary warnings", summary.GlossaryWarnings);
        AppendListSection(sb, "Self-healed (marker repaired after retry)", summary.SelfHealedChunks);
        AppendListSection(sb, "Left untranslated (markers kept dropping after repair attempts)", summary.UnrecoverableChunks);
        AppendListSection(sb, "Skipped (unexpected reconstruction failure)", summary.Errors);

        return sb.ToString();
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
