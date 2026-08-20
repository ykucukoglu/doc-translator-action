using DocTranslator.Core.Models;

namespace DocTranslator.Cli.Logging;

public interface IConsoleSummaryWriter
{
    void Write(TranslationRunSummary summary, int translatedFilesCount, string? pullRequestUrl, bool dryRun);
}

/// <summary>Human-readable summary printed at the end of every run, dry-run or not.</summary>
public sealed class ConsoleSummaryWriter : IConsoleSummaryWriter
{
    public void Write(TranslationRunSummary summary, int translatedFilesCount, string? pullRequestUrl, bool dryRun)
    {
        Console.WriteLine();
        Console.WriteLine("=== doc-translator-action summary ===");
        Console.WriteLine($"Files translated: {translatedFilesCount}");

        foreach (var language in summary.Languages)
        {
            Console.WriteLine($"  [{language.TargetLanguage}] {language.ChunksTranslated} chunk(s) translated, {language.ChunksFromCache} from cache");
        }

        foreach (var warning in summary.GlossaryWarnings)
        {
            Console.WriteLine($"  glossary warning: {warning}");
        }

        foreach (var warning in summary.DriftWarnings)
        {
            Console.WriteLine($"  drift warning: {warning}");
        }

        foreach (var error in summary.Errors)
        {
            Console.WriteLine($"  skipped (malformed translation response): {error}");
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
