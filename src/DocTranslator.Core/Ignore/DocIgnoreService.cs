namespace DocTranslator.Core.Ignore;

public interface IDocIgnoreService
{
    /// <summary>Loads glob exclusion patterns from a <c>.doc-ignore</c> file (one per line, <c>#</c> comments, blank lines skipped). Returns an empty filter if the file doesn't exist.</summary>
    IDocIgnoreFilter Load(string docIgnorePath);
}

/// <summary>
/// <c>.doc-ignore</c> mirrors <c>.gitignore</c>'s line format (a glob per line) so file/glob
/// exclusions like <c>CHANGELOG.md</c> or <c>DRAFT_*.md</c> can be kept out of the translation
/// pipeline without touching <c>include-glob</c> itself.
/// </summary>
public sealed class DocIgnoreService : IDocIgnoreService
{
    public IDocIgnoreFilter Load(string docIgnorePath)
    {
        if (!File.Exists(docIgnorePath))
        {
            return DocIgnoreFilter.Empty;
        }

        var patterns = File.ReadAllLines(docIgnorePath)
            .Select(line => line.Trim())
            .Where(line => line.Length > 0 && !line.StartsWith('#'))
            .ToList();

        return new DocIgnoreFilter(patterns);
    }
}
