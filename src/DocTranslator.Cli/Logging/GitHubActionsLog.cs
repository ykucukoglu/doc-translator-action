namespace DocTranslator.Cli.Logging;

public interface IGitHubActionsLog
{
    /// <summary>Opens a collapsible <c>::group::</c> log section; dispose the result to close it.</summary>
    IDisposable BeginGroup(string title);

    void LogWarning(string message, string? file = null);

    void LogError(string message, string? file = null);
}

/// <summary>
/// Emits GitHub Actions workflow commands
/// (https://docs.github.com/actions/using-workflows/workflow-commands-for-github-actions) to
/// stdout: <c>::group::</c>/<c>::endgroup::</c> collapse per-file processing into a foldable log
/// section, and <c>::warning file=...::</c>/<c>::error file=...::</c> turn glossary and
/// reconstruction issues into PR-visible annotations instead of console lines nobody scrolls to.
/// Outside GitHub Actions these commands just print as harmless plain text.
/// </summary>
public sealed class GitHubActionsLog : IGitHubActionsLog
{
    public IDisposable BeginGroup(string title)
    {
        Console.WriteLine($"::group::{Escape(title)}");
        return new GroupScope();
    }

    public void LogWarning(string message, string? file = null) =>
        Console.WriteLine($"::warning{FileParam(file)}::{Escape(message)}");

    public void LogError(string message, string? file = null) =>
        Console.WriteLine($"::error{FileParam(file)}::{Escape(message)}");

    private static string FileParam(string? file) =>
        string.IsNullOrEmpty(file) ? string.Empty : $" file={file}";

    // Workflow command values can't contain raw '%'/CR/LF - these are the three escapes GitHub documents.
    private static string Escape(string text) =>
        text.Replace("%", "%25").Replace("\r", "%0D").Replace("\n", "%0A");

    private sealed class GroupScope : IDisposable
    {
        public void Dispose() => Console.WriteLine("::endgroup::");
    }
}
