using DocTranslator.GitHub.Operations;
using FluentAssertions;
using LibGit2Sharp;

namespace DocTranslator.GitHub.Tests;

public sealed class GitWriterTests : IDisposable
{
    private readonly string _remoteRoot = Path.Combine(Path.GetTempPath(), "doc-translator-remote-" + Guid.NewGuid().ToString("N"));
    private readonly List<string> _workingDirs = [];
    private readonly GitWriter _sut = new();

    public GitWriterTests()
    {
        Repository.Init(_remoteRoot, isBare: true);
    }

    public void Dispose()
    {
        foreach (var dir in _workingDirs.Append(_remoteRoot))
        {
            try
            {
                Directory.Delete(dir, recursive: true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    [Fact]
    public void CommitAndPush_NewBranch_WritesFilesAndPushesToRemote()
    {
        var repoRoot = NewWorkingRepo();

        _sut.CommitAndPush(
            repoRoot, "doc-translator/test",
            new Dictionary<string, string> { ["docs/tr/guide.md"] = "translated" },
            "commit message", "author", "author@test.com", remoteToken: "dummy");

        // The working directory is restored to its pre-call state (see the dedicated test below),
        // so the pushed content is only verifiable from the remote branch, not the local checkout.
        using var remote = new Repository(_remoteRoot);
        var tip = remote.Branches["doc-translator/test"].Tip;
        ((Blob)tip[Path.Combine("docs", "tr", "guide.md").Replace('\\', '/')].Target)
            .GetContentText().Should().Be("translated");
    }

    [Fact]
    public void CommitAndPush_BranchAlreadyPushedFromADifferentCheckout_Overwrites()
    {
        // Mirrors a re-run of the same workflow run after fixing an unrelated failure: a fresh
        // checkout has no knowledge of a branch a previous attempt already pushed.
        var firstCheckout = NewWorkingRepo();
        _sut.CommitAndPush(
            firstCheckout, "doc-translator/test",
            new Dictionary<string, string> { ["docs/tr/guide.md"] = "first attempt" },
            "first", "author", "author@test.com", remoteToken: "dummy");

        var secondCheckout = NewWorkingRepo();
        var act = () => _sut.CommitAndPush(
            secondCheckout, "doc-translator/test",
            new Dictionary<string, string> { ["docs/tr/guide.md"] = "second attempt" },
            "second", "author", "author@test.com", remoteToken: "dummy");

        act.Should().NotThrow();

        using var remote = new Repository(_remoteRoot);
        var tip = remote.Branches["doc-translator/test"].Tip;
        ((Blob)tip[Path.Combine("docs", "tr", "guide.md").Replace('\\', '/')].Target)
            .GetContentText().Should().Be("second attempt");
    }

    [Fact]
    public void CommitAndPush_LeavesWorkingDirectoryOnItsOriginalBranch()
    {
        // repoRoot is the same working directory the calling job's later steps would still use -
        // it must not be left checked out on the translation branch.
        var repoRoot = NewWorkingRepo();
        using var repo = new Repository(repoRoot);
        var originalBranchName = repo.Head.FriendlyName;
        var originalHeadSha = repo.Head.Tip.Sha;

        _sut.CommitAndPush(
            repoRoot, "doc-translator/test",
            new Dictionary<string, string> { ["docs/tr/guide.md"] = "translated" },
            "commit message", "author", "author@test.com", remoteToken: "dummy");

        repo.Head.FriendlyName.Should().Be(originalBranchName);
        repo.Head.Tip.Sha.Should().Be(originalHeadSha);
        File.Exists(Path.Combine(repoRoot, "docs", "tr", "guide.md")).Should().BeFalse();
    }

    [Fact]
    public void CommitAndPush_NestedOutputPath_CreatesDirectories()
    {
        var repoRoot = NewWorkingRepo();

        _sut.CommitAndPush(
            repoRoot, "doc-translator/nested",
            new Dictionary<string, string> { ["docs/de/guides/deep/nested.md"] = "content" },
            "commit", "author", "author@test.com", remoteToken: "dummy");

        using var remote = new Repository(_remoteRoot);
        var tip = remote.Branches["doc-translator/nested"].Tip;
        tip[Path.Combine("docs", "de", "guides", "deep", "nested.md").Replace('\\', '/')].Should().NotBeNull();
    }

    private string NewWorkingRepo()
    {
        var dir = Path.Combine(Path.GetTempPath(), "doc-translator-work-" + Guid.NewGuid().ToString("N"));
        _workingDirs.Add(dir);
        Repository.Init(dir);

        File.WriteAllText(Path.Combine(dir, "README.md"), "base");
        using (var repo = new Repository(dir))
        {
            Commands.Stage(repo, "README.md");
            var sig = new Signature("test", "test@test.com", DateTimeOffset.Now);
            repo.Commit("base", sig, sig);
            repo.Network.Remotes.Add("origin", _remoteRoot);
        }

        return dir;
    }
}
