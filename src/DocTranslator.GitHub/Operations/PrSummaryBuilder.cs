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

        if (summary.DriftWarnings.Count > 0)
        {
            sb.AppendLine("### Stale translations detected").AppendLine();
            foreach (var warning in summary.DriftWarnings)
            {
                sb.Append("- ").AppendLine(warning);
            }

            sb.AppendLine();
        }

        if (summary.GlossaryWarnings.Count > 0)
        {
            sb.AppendLine("### Glossary warnings").AppendLine();
            foreach (var warning in summary.GlossaryWarnings)
            {
                sb.Append("- ").AppendLine(warning);
            }

            sb.AppendLine();
        }

        if (summary.Errors.Count > 0)
        {
            sb.AppendLine("### Skipped (malformed translation response)").AppendLine();
            foreach (var error in summary.Errors)
            {
                sb.Append("- ").AppendLine(error);
            }
        }

        return sb.ToString();
    }
}
