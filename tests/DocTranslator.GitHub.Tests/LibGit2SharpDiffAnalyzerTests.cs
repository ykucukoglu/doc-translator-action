using DocTranslator.GitHub.Diff;
using FluentAssertions;
using LibGit2Sharp;

namespace DocTranslator.GitHub.Tests;

public sealed class LibGit2SharpDiffAnalyzerTests : IDisposable
{
    private readonly string _repoRoot = Path.Combine(Path.GetTempPath(), "doc-translator-github-tests-" + Guid.NewGuid().ToString("N"));
    private readonly LibGit2SharpDiffAnalyzer _sut = new();

    public LibGit2SharpDiffAnalyzerTests()
    {
        Repository.Init(_repoRoot);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_repoRoot, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    [Fact]
    public void GetChangedFiles_NullBaseRef_DiffsAgainstPreviousCommit()
    {
        WriteAndCommit("docs/guide.md", "v1", "base");
        WriteAndCommit("docs/other.md", "v1", "unrelated");
        WriteAndCommit("docs/guide.md", "v2", "revise");

        var changed = _sut.GetChangedFiles(_repoRoot, baseRef: null, "docs/**/*.md");

        changed.Should().ContainSingle(f => f.Path == "docs/guide.md");
    }

    [Fact]
    public void GetChangedFiles_FirstCommitInRepo_TreatsEveryFileAsChanged()
    {
        WriteAndCommit("docs/guide.md", "v1", "only commit");

        var changed = _sut.GetChangedFiles(_repoRoot, baseRef: null, "docs/**/*.md");

        changed.Should().ContainSingle(f => f.Path == "docs/guide.md" && f.Kind == FileChangeKind.Added);
    }

    [Fact]
    public void GetChangedFiles_UnresolvableBaseRef_Throws()
    {
        WriteAndCommit("docs/guide.md", "v1", "base");

        var act = () => _sut.GetChangedFiles(_repoRoot, baseRef: "does-not-exist", "docs/**/*.md");

        act.Should().Throw<InvalidOperationException>().WithMessage("*does-not-exist*");
    }

    [Fact]
    public void GetChangedFiles_FileOutsideIncludeGlob_IsExcluded()
    {
        WriteAndCommit("docs/guide.md", "v1", "base");
        WriteAndCommit("README.md", "changed", "revise");
        WriteAndCommit("docs/guide.md", "v2", "revise docs too");

        var changed = _sut.GetChangedFiles(_repoRoot, baseRef: null, "docs/**/*.md");

        changed.Should().OnlyContain(f => f.Path.StartsWith("docs/"));
    }

    [Fact]
    public void GetChangedFiles_DeletedFile_IsExcluded()
    {
        WriteAndCommit("docs/guide.md", "v1", "base");
        using (var repo = new Repository(_repoRoot))
        {
            File.Delete(Path.Combine(_repoRoot, "docs", "guide.md"));
            Commands.Stage(repo, "docs/guide.md");
            var sig = new Signature("test", "test@test.com", DateTimeOffset.Now);
            repo.Commit("delete", sig, sig);
        }

        var changed = _sut.GetChangedFiles(_repoRoot, baseRef: null, "docs/**/*.md");

        changed.Should().BeEmpty();
    }

    [Fact]
    public void GetHeadShortSha_ReturnsSevenCharacterPrefix()
    {
        WriteAndCommit("docs/guide.md", "v1", "base");

        using var repo = new Repository(_repoRoot);
        var fullSha = repo.Head.Tip.Sha;

        _sut.GetHeadShortSha(_repoRoot).Should().Be(fullSha[..7]);
    }

    private void WriteAndCommit(string relativePath, string content, string message)
    {
        var fullPath = Path.Combine(_repoRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, content);

        using var repo = new Repository(_repoRoot);
        Commands.Stage(repo, relativePath);
        var signature = new Signature("test", "test@test.com", DateTimeOffset.Now);
        repo.Commit(message, signature, signature);
    }
}
