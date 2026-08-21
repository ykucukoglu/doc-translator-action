using DocTranslator.Cli.Options;
using FluentAssertions;

namespace DocTranslator.Cli.Tests;

/// <summary>
/// Env-var-driven config resolution, tested by actually setting process environment variables -
/// these tests run sequentially within this class (xunit's default), and no other test class in
/// the solution touches INPUT_*/GITHUB_* vars, so this is safe without a shared-fixture lock.
/// </summary>
public sealed class ActionOptionsBinderTests : IDisposable
{
    private readonly List<string> _touchedVars = [];
    private readonly List<string> _tempFiles = [];

    [Fact]
    public void Bind_PushEventEmptyGitHubBaseRef_ResolvesBaseBranchToNull()
    {
        // GitHub Actions sets GITHUB_BASE_REF to "" (not unset) on push events - only pull_request
        // events populate it. A plain `??` chain treats "" as present, which is exactly what let
        // an empty baseRef reach Octokit and crash PR creation with "String cannot be empty".
        Set("GITHUB_BASE_REF", "");
        Set("INPUT_PR-MODE", "false");
        Set("INPUT_GITHUB-TOKEN", "dummy");

        var options = ActionOptionsBinder.Bind(new ActionOptionsCliOverrides());

        options.BaseBranch.Should().BeNull();
    }

    [Fact]
    public void Bind_PullRequestEventGitHubBaseRef_ResolvesBaseBranchToItsValue()
    {
        Set("GITHUB_BASE_REF", "main");
        Set("INPUT_PR-MODE", "false");
        Set("INPUT_GITHUB-TOKEN", "dummy");

        var options = ActionOptionsBinder.Bind(new ActionOptionsCliOverrides());

        options.BaseBranch.Should().Be("main");
    }

    [Fact]
    public void Bind_HyphenatedInputName_IsRead()
    {
        // GitHub Actions preserves hyphens in INPUT_* names by convention (INPUT_TARGET-LANGUAGES),
        // not the underscored form IConfiguration's default binder would expect.
        Set("INPUT_TARGET-LANGUAGES", "tr,de,fr");
        Set("INPUT_PR-MODE", "false");
        Set("INPUT_GITHUB-TOKEN", "dummy");

        var options = ActionOptionsBinder.Bind(new ActionOptionsCliOverrides());

        options.TargetLanguages.Should().Equal("tr", "de", "fr");
    }

    [Fact]
    public void Bind_CliOverride_WinsOverEnvironmentInput()
    {
        Set("INPUT_TARGET-LANGUAGES", "de");
        Set("INPUT_PR-MODE", "false");
        Set("INPUT_GITHUB-TOKEN", "dummy");

        var options = ActionOptionsBinder.Bind(new ActionOptionsCliOverrides { TargetLanguages = "tr" });

        options.TargetLanguages.Should().Equal("tr");
    }

    [Fact]
    public void Bind_DryRunFalseAndNoGitHubToken_Throws()
    {
        Set("INPUT_PR-MODE", "true");
        Set("INPUT_DRY-RUN", "false");

        var act = () => ActionOptionsBinder.Bind(new ActionOptionsCliOverrides());

        act.Should().Throw<InvalidOperationException>().WithMessage("*github-token*");
    }

    [Fact]
    public void Bind_ForkPullRequestTarget_ForcesDryRunEvenWithPrModeTrue()
    {
        Set("GITHUB_EVENT_NAME", "pull_request_target");
        Set("GITHUB_EVENT_PATH", WriteEventPayload(fork: true));
        Set("INPUT_PR-MODE", "true");
        Set("INPUT_GITHUB-TOKEN", "dummy");

        var options = ActionOptionsBinder.Bind(new ActionOptionsCliOverrides());

        options.DryRun.Should().BeTrue();
    }

    [Fact]
    public void Bind_ForkPullRequestTargetWithAllowFlag_RespectsRequestedPrMode()
    {
        Set("GITHUB_EVENT_NAME", "pull_request_target");
        Set("GITHUB_EVENT_PATH", WriteEventPayload(fork: true));
        Set("INPUT_PR-MODE", "true");
        Set("INPUT_GITHUB-TOKEN", "dummy");
        Set("INPUT_ALLOW-FORK-PULL-REQUEST-TARGET", "true");

        var options = ActionOptionsBinder.Bind(new ActionOptionsCliOverrides());

        options.DryRun.Should().BeFalse();
    }

    [Fact]
    public void Bind_SameRepoPullRequestTarget_DoesNotForceDryRun()
    {
        Set("GITHUB_EVENT_NAME", "pull_request_target");
        Set("GITHUB_EVENT_PATH", WriteEventPayload(fork: false));
        Set("INPUT_PR-MODE", "true");
        Set("INPUT_GITHUB-TOKEN", "dummy");

        var options = ActionOptionsBinder.Bind(new ActionOptionsCliOverrides());

        options.DryRun.Should().BeFalse();
    }

    [Fact]
    public void Bind_RegularPullRequestEvent_DoesNotForceDryRun()
    {
        Set("GITHUB_EVENT_NAME", "pull_request");
        Set("GITHUB_EVENT_PATH", WriteEventPayload(fork: true));
        Set("INPUT_PR-MODE", "true");
        Set("INPUT_GITHUB-TOKEN", "dummy");

        var options = ActionOptionsBinder.Bind(new ActionOptionsCliOverrides());

        options.DryRun.Should().BeFalse();
    }

    private string WriteEventPayload(bool fork)
    {
        var path = Path.Combine(Path.GetTempPath(), $"doc-translator-event-{Guid.NewGuid():N}.json");
        var forkValue = fork ? "true" : "false";
        File.WriteAllText(path, "{\"pull_request\":{\"head\":{\"repo\":{\"fork\":" + forkValue + "}}}}");
        _tempFiles.Add(path);
        return path;
    }

    private void Set(string name, string value)
    {
        _touchedVars.Add(name);
        Environment.SetEnvironmentVariable(name, value);
    }

    public void Dispose()
    {
        foreach (var name in _touchedVars)
        {
            Environment.SetEnvironmentVariable(name, null);
        }

        foreach (var path in _tempFiles)
        {
            File.Delete(path);
        }
    }
}
