using Octokit;

namespace DocTranslator.GitHub.Operations;

/// <summary>Everything the orchestrator needs to hand off translated files as an opened/updated PR.</summary>
public sealed record GitHubPushRequest(
    string RepositoryPath,
    string Owner,
    string RepositoryName,
    string BaseBranch,
    string BranchName,
    IReadOnlyDictionary<string, string> FilesToCommit,
    string CommitMessage,
    string PullRequestTitle,
    string PullRequestBody,
    string? SummaryComment,
    string Token,
    bool CleanupStaleBranches = true);

public sealed record PullRequestOutcome(string Url, int Number, bool WasCreated);

public interface IGitHubService
{
    Task<PullRequestOutcome> CommitAndOpenPullRequestAsync(GitHubPushRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// LibGit2Sharp (via <see cref="IGitWriter"/>) does the actual git writes; Octokit is used purely
/// for the PR REST operation - creating it (or reusing an already-open PR for the same head
/// branch, so re-runs on the same commit are idempotent) and posting the summary comment.
/// </summary>
public sealed class OctokitGitHubService(IGitWriter gitWriter) : IGitHubService
{
    public async Task<PullRequestOutcome> CommitAndOpenPullRequestAsync(GitHubPushRequest request, CancellationToken cancellationToken)
    {
        // Octokit 14.x's PullRequest/Issue.Comment client methods used below don't expose
        // CancellationToken overloads at all (confirmed against the installed package - it's a
        // SDK limitation, not an oversight here), so this is the best cooperative-cancellation
        // point available: bail out before starting the irreversible git push/PR sequence, and
        // between each REST call, rather than mid-request.
        cancellationToken.ThrowIfCancellationRequested();

        gitWriter.CommitAndPush(
            request.RepositoryPath,
            request.BranchName,
            request.FilesToCommit,
            request.CommitMessage,
            authorName: "doc-translator-action",
            authorEmail: "doc-translator-action@users.noreply.github.com",
            remoteToken: request.Token);

        cancellationToken.ThrowIfCancellationRequested();

        var client = new GitHubClient(new ProductHeaderValue("doc-translator-action"))
        {
            Credentials = new Credentials(request.Token),
        };

        var existing = await client.PullRequest.GetAllForRepository(
            request.Owner,
            request.RepositoryName,
            new PullRequestRequest { State = ItemStateFilter.Open, Head = $"{request.Owner}:{request.BranchName}" });

        cancellationToken.ThrowIfCancellationRequested();

        var outcome = existing.Count > 0
            ? new PullRequestOutcome(existing[0].HtmlUrl, existing[0].Number, WasCreated: false)
            : await CreatePullRequestAsync(client, request);

        if (!string.IsNullOrWhiteSpace(request.SummaryComment))
        {
            cancellationToken.ThrowIfCancellationRequested();
            await client.Issue.Comment.Create(request.Owner, request.RepositoryName, outcome.Number, request.SummaryComment);
        }

        if (request.CleanupStaleBranches)
        {
            // Best-effort housekeeping, not the point of this run - a failure here (permissions,
            // API hiccup) shouldn't fail a PR that was otherwise opened/updated successfully.
            try
            {
                await CleanupStaleBranchesAsync(client, request, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Console.WriteLine($"::warning::Stale doc-translator/* branch cleanup failed, continuing: {ex.Message}");
            }
        }

        return outcome;
    }

    /// <summary>
    /// Deletes this action's own <c>doc-translator/&lt;sha&gt;</c> branches once their pull request
    /// is closed (merged or declined) - left alone otherwise, this action opens a new branch every
    /// run and nothing ever removes the old ones. Scoped strictly to that name prefix and to
    /// branches with a known closed PR; a branch with no matching PR at all is left untouched
    /// rather than guessed at, and the branch this run just pushed to is never a candidate.
    /// </summary>
    private static async Task CleanupStaleBranchesAsync(GitHubClient client, GitHubPushRequest request, CancellationToken cancellationToken)
    {
        var closedPrHeadBranches = (await client.PullRequest.GetAllForRepository(
                request.Owner,
                request.RepositoryName,
                new PullRequestRequest { State = ItemStateFilter.Closed }))
            .Select(pr => pr.Head.Ref)
            .ToHashSet(StringComparer.Ordinal);

        cancellationToken.ThrowIfCancellationRequested();

        var branches = await client.Repository.Branch.GetAll(request.Owner, request.RepositoryName);

        foreach (var branch in branches)
        {
            if (!branch.Name.StartsWith("doc-translator/", StringComparison.Ordinal)
                || branch.Name == request.BranchName
                || !closedPrHeadBranches.Contains(branch.Name))
            {
                continue;
            }

            cancellationToken.ThrowIfCancellationRequested();
            await client.Git.Reference.Delete(request.Owner, request.RepositoryName, $"heads/{branch.Name}");
        }
    }

    private static async Task<PullRequestOutcome> CreatePullRequestAsync(GitHubClient client, GitHubPushRequest request)
    {
        var newPullRequest = new NewPullRequest(request.PullRequestTitle, request.BranchName, request.BaseBranch)
        {
            Body = request.PullRequestBody,
        };

        var created = await client.PullRequest.Create(request.Owner, request.RepositoryName, newPullRequest);
        return new PullRequestOutcome(created.HtmlUrl, created.Number, WasCreated: true);
    }
}
