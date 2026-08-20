using Microsoft.Extensions.FileSystemGlobbing;

namespace DocTranslator.Core.Ignore;

public interface IDocIgnoreFilter
{
    /// <summary>True if the given repo-relative path matches any pattern loaded from <c>.doc-ignore</c>.</summary>
    bool IsIgnored(string relativePath);
}

/// <summary>An empty filter (no <c>.doc-ignore</c> file present) - nothing is ever ignored.</summary>
public sealed class DocIgnoreFilter(IReadOnlyList<string> patterns) : IDocIgnoreFilter
{
    public static readonly DocIgnoreFilter Empty = new([]);

    private readonly Matcher? _matcher = BuildMatcher(patterns);

    public bool IsIgnored(string relativePath) =>
        _matcher is not null && _matcher.Match(relativePath.Replace('\\', '/')).HasMatches;

    private static Matcher? BuildMatcher(IReadOnlyList<string> patterns)
    {
        if (patterns.Count == 0)
        {
            return null;
        }

        var matcher = new Matcher();
        foreach (var pattern in patterns)
        {
            matcher.AddInclude(pattern);

            // Microsoft.Extensions.FileSystemGlobbing treats '*' as not crossing '/', so a bare
            // pattern like "DRAFT_*.md" only matches at the search root - unlike .gitignore,
            // where a slash-free pattern matches at any depth. Add a "**/"-prefixed variant too
            // so .doc-ignore behaves the way users expect from .gitignore-style tooling.
            if (!pattern.Contains('/') && !pattern.StartsWith("**/", StringComparison.Ordinal))
            {
                matcher.AddInclude($"**/{pattern}");
            }
        }

        return matcher;
    }
}
