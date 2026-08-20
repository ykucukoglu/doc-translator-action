namespace DocTranslator.GitHub.Diff;

public enum FileChangeKind
{
    Added,
    Modified,
    Renamed,
}

/// <summary>One file changed between the base ref and HEAD that matched the include glob.</summary>
public sealed record ChangedFile(string Path, FileChangeKind Kind);
