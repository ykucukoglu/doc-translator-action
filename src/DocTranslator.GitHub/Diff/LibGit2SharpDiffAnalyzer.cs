using LibGit2Sharp;
using Microsoft.Extensions.FileSystemGlobbing;

namespace DocTranslator.GitHub.Diff;

public interface IGitDiffAnalyzer
{
    /// <summary>
    /// Lists files changed between a base ref and HEAD, filtered by <paramref name="includeGlob"/>.
    /// This is the file-level cost-optimization layer required by the spec - it decides *which
    /// files* get parsed/translated at all; the content-hash translation cache (see
    /// DocTranslator.GitHub.Cache) is the paragraph-level layer on top of it.
    /// </summary>
    /// <param name="baseRef">
    /// Branch name, tag, or commit-ish to diff against. When null, diffs against HEAD's first
    /// parent (i.e. the single most recent commit - the push-event case). For pull-request
    /// events, callers should pass the PR's base branch (from GITHUB_BASE_REF).
    /// </param>
    IReadOnlyList<ChangedFile> GetChangedFiles(string repositoryPath, string? baseRef, string includeGlob);
}

public sealed class LibGit2SharpDiffAnalyzer : IGitDiffAnalyzer
{
    public IReadOnlyList<ChangedFile> GetChangedFiles(string repositoryPath, string? baseRef, string includeGlob)
    {
        using var repo = new Repository(repositoryPath);

        var head = repo.Head.Tip
            ?? throw new InvalidOperationException($"Repository at '{repositoryPath}' has no commits at HEAD.");

        var baseCommit = ResolveBaseCommit(repo, baseRef, head);
        var changes = repo.Diff.Compare<TreeChanges>(baseCommit?.Tree, head.Tree);

        var matcher = new Matcher();
        matcher.AddInclude(includeGlob);

        var results = new List<ChangedFile>();
        foreach (var change in changes)
        {
            if (change.Status == ChangeKind.Deleted)
            {
                continue; // nothing to (re-)translate for a file that no longer exists
            }

            if (!matcher.Match(change.Path).HasMatches)
            {
                continue;
            }

            var kind = change.Status switch
            {
                ChangeKind.Added => FileChangeKind.Added,
                ChangeKind.Renamed => FileChangeKind.Renamed,
                _ => FileChangeKind.Modified,
            };

            results.Add(new ChangedFile(change.Path, kind));
        }

        return results;
    }

    private static Commit? ResolveBaseCommit(Repository repo, string? baseRef, Commit head)
    {
        if (string.IsNullOrWhiteSpace(baseRef))
        {
            // Push-event default: diff against the single previous commit.
            return head.Parents.FirstOrDefault();
        }

        var branch = repo.Branches[baseRef] ?? repo.Branches[$"origin/{baseRef}"];
        if (branch?.Tip is not null)
        {
            return branch.Tip;
        }

        var commit = repo.Lookup<Commit>(baseRef);
        if (commit is not null)
        {
            return commit;
        }

        throw new InvalidOperationException($"Could not resolve base ref '{baseRef}' in repository at '{repo.Info.Path}'.");
    }
}
