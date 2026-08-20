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
    string Token);

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
        gitWriter.CommitAndPush(
            request.RepositoryPath,
            request.BranchName,
            request.FilesToCommit,
            request.CommitMessage,
            authorName: "doc-translator-action",
            authorEmail: "doc-translator-action@users.noreply.github.com",
            remoteToken: request.Token);

        var client = new GitHubClient(new ProductHeaderValue("doc-translator-action"))
        {
            Credentials = new Credentials(request.Token),
        };

        var existing = await client.PullRequest.GetAllForRepository(
            request.Owner,
            request.RepositoryName,
            new PullRequestRequest { State = ItemStateFilter.Open, Head = $"{request.Owner}:{request.BranchName}" });

        var outcome = existing.Count > 0
            ? new PullRequestOutcome(existing[0].HtmlUrl, existing[0].Number, WasCreated: false)
            : await CreatePullRequestAsync(client, request);

        if (!string.IsNullOrWhiteSpace(request.SummaryComment))
        {
            await client.Issue.Comment.Create(request.Owner, request.RepositoryName, outcome.Number, request.SummaryComment);
        }

        return outcome;
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
