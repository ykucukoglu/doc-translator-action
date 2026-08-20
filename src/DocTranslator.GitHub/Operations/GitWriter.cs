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

        var branch = repo.Branches[branchName] ?? repo.CreateBranch(branchName);
        Commands.Checkout(repo, branch);

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

        // Push the branch ref directly - avoids requiring upstream tracking to already be
        // configured, which a freshly-created local branch never has.
        repo.Network.Push(remote, $"refs/heads/{branchName}:refs/heads/{branchName}", pushOptions);
    }
}
