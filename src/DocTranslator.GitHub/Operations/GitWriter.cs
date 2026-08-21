using LibGit2Sharp;

namespace DocTranslator.GitHub.Operations;

public interface IGitWriter
{
    /// <summary>
    /// Creates (or reuses) a local branch, writes the given files, stages, commits, and pushes to
    /// 'origin' using token-based credentials. The Octokit PR call is a separate step - this type
    /// only does the LibGit2Sharp mechanics.
    /// </summary>
    void CommitAndPush(
        string repositoryPath,
        string branchName,
        IReadOnlyDictionary<string, string> filesToWrite,
        string commitMessage,
        string authorName,
        string authorEmail,
        string remoteToken);

    /// <summary>
    /// Writes, stages, and commits directly onto whatever branch is already checked out, then
    /// pushes it back to 'origin' with the same name - no new branch, no PR. Meant for a workflow
    /// that already has a specific existing branch checked out for a reason of its own (e.g. a
    /// PR-comment-triggered "/translate" run pushing straight back onto that PR's own branch - see
    /// the "Comment-triggered (ChatOps)" recipe in the README). The push is never forced: unlike
    /// <see cref="CommitAndPush"/>'s deterministic, this-action-only branch, this one is real,
    /// shared work someone else could also be pushing to, so a non-fast-forward push is left to
    /// fail naturally rather than risk discarding their commits.
    /// </summary>
    void CommitAndPushToCurrentBranch(
        string repositoryPath,
        IReadOnlyDictionary<string, string> filesToWrite,
        string commitMessage,
        string authorName,
        string authorEmail,
        string remoteToken);
}

public sealed class GitWriter : IGitWriter
{
    public void CommitAndPush(
        string repositoryPath,
        string branchName,
        IReadOnlyDictionary<string, string> filesToWrite,
        string commitMessage,
        string authorName,
        string authorEmail,
        string remoteToken)
    {
        using var repo = new Repository(repositoryPath);

        // repositoryPath is the same /github/workspace the runner's own job (and any steps after
        // this action) uses - checking out the translation branch here would leave that shared
        // working directory pointed at it once this call returns. Restored in `finally` regardless
        // of outcome, so a step after this action still sees whatever was checked out before it ran.
        var wasDetached = repo.Info.IsHeadDetached;
        var originalHeadRef = wasDetached ? repo.Head.Tip.Sha : repo.Head.FriendlyName;

        try
        {
            var branch = repo.Branches[branchName] ?? repo.CreateBranch(branchName);
            Commands.Checkout(repo, branch);

            WriteStageCommit(repo, repositoryPath, filesToWrite, commitMessage, authorName, authorEmail);

            // Force-push (the leading '+'): branchName is deterministically derived from the
            // triggering commit SHA (doc-translator/{shortSha}) and this action is the only writer
            // of that ref, by design, for idempotent re-runs. Without the '+', a re-run of the same
            // workflow run - e.g. after fixing an unrelated failure like a missing PR permission -
            // starts from a fresh checkout with no knowledge of the branch a previous attempt
            // already pushed, so a plain push is rejected as non-fast-forward even though
            // regenerating that exact branch's content is exactly what a re-run is supposed to do.
            Push(repo, $"+refs/heads/{branchName}:refs/heads/{branchName}", remoteToken, repositoryPath);
        }
        finally
        {
            Commands.Checkout(repo, originalHeadRef);
        }
    }

    public void CommitAndPushToCurrentBranch(
        string repositoryPath,
        IReadOnlyDictionary<string, string> filesToWrite,
        string commitMessage,
        string authorName,
        string authorEmail,
        string remoteToken)
    {
        using var repo = new Repository(repositoryPath);

        if (repo.Info.IsHeadDetached)
        {
            throw new InvalidOperationException(
                "push-to-current-branch requires the checkout to be on a real branch, not a detached HEAD. "
                + "Check out the target branch by name (e.g. via 'gh pr checkout' or actions/checkout's 'ref' input) before running this action.");
        }

        var branchName = repo.Head.FriendlyName;

        WriteStageCommit(repo, repositoryPath, filesToWrite, commitMessage, authorName, authorEmail);

        // Not forced, unlike CommitAndPush: this is a real, possibly-shared branch (e.g. someone
        // else's PR), not one this action deterministically owns - a non-fast-forward push fails
        // naturally here rather than risk discarding a commit that landed on it in the meantime.
        Push(repo, $"refs/heads/{branchName}:refs/heads/{branchName}", remoteToken, repositoryPath);
    }

    private static void WriteStageCommit(
        Repository repo,
        string repositoryPath,
        IReadOnlyDictionary<string, string> filesToWrite,
        string commitMessage,
        string authorName,
        string authorEmail)
    {
        foreach (var (relativePath, content) in filesToWrite)
        {
            var fullPath = Path.Combine(repositoryPath, relativePath);
            var directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(fullPath, content);
            Commands.Stage(repo, relativePath);
        }

        var signature = new Signature(authorName, authorEmail, DateTimeOffset.Now);
        repo.Commit(commitMessage, signature, signature);
    }

    private static void Push(Repository repo, string refSpec, string remoteToken, string repositoryPath)
    {
        var remote = repo.Network.Remotes["origin"]
            ?? throw new InvalidOperationException($"No 'origin' remote configured in the repository at '{repositoryPath}'.");

        var pushOptions = new PushOptions
        {
            CredentialsProvider = (_, _, _) => new UsernamePasswordCredentials
            {
                Username = "x-access-token",
                Password = remoteToken,
            },
        };

        repo.Network.Push(remote, refSpec, pushOptions);
    }
}
