using DocTranslator.Core.Ignore;
using FluentAssertions;

namespace DocTranslator.Core.Tests;

public class DocIgnoreServiceTests : IDisposable
{
    private readonly DocIgnoreService _sut = new();
    private readonly string _tempFile = Path.Combine(Path.GetTempPath(), $"doc-translator-ignore-{Guid.NewGuid():N}.doc-ignore");

    public void Dispose()
    {
        if (File.Exists(_tempFile))
        {
            File.Delete(_tempFile);
        }

        GC.SuppressFinalize(this);
    }

    [Fact]
    public void Load_MissingFile_ReturnsFilterThatIgnoresNothing()
    {
        var filter = _sut.Load(Path.Combine(Path.GetTempPath(), "does-not-exist.doc-ignore"));

        filter.IsIgnored("CHANGELOG.md").Should().BeFalse();
        filter.IsIgnored("docs/guide.md").Should().BeFalse();
    }

    [Fact]
    public void Load_ExactFilenamePattern_MatchesThatFile()
    {
        File.WriteAllText(_tempFile, "CHANGELOG.md\n");

        var filter = _sut.Load(_tempFile);

        filter.IsIgnored("CHANGELOG.md").Should().BeTrue();
        filter.IsIgnored("docs/guide.md").Should().BeFalse();
    }

    [Fact]
    public void Load_WildcardPattern_MatchesAllVariants()
    {
        File.WriteAllText(_tempFile, "DRAFT_*.md\n");

        var filter = _sut.Load(_tempFile);

        filter.IsIgnored("DRAFT_new-feature.md").Should().BeTrue();
        filter.IsIgnored("docs/DRAFT_notes.md").Should().BeTrue();
        filter.IsIgnored("guide.md").Should().BeFalse();
    }

    [Fact]
    public void Load_CommentsAndBlankLines_AreIgnoredAsPatterns()
    {
        File.WriteAllText(_tempFile, "# this is a comment\n\nCHANGELOG.md\n   \n# another comment\n");

        var filter = _sut.Load(_tempFile);

        filter.IsIgnored("CHANGELOG.md").Should().BeTrue();
        filter.IsIgnored("# this is a comment").Should().BeFalse();
    }

    [Fact]
    public void Load_EmptyFile_ReturnsFilterThatIgnoresNothing()
    {
        File.WriteAllText(_tempFile, string.Empty);

        var filter = _sut.Load(_tempFile);

        filter.IsIgnored("anything.md").Should().BeFalse();
    }

    [Fact]
    public void Load_MultiplePatterns_EachAppliesIndependently()
    {
        File.WriteAllText(_tempFile, "CHANGELOG.md\nDRAFT_*.md\narchive/**\n");

        var filter = _sut.Load(_tempFile);

        filter.IsIgnored("CHANGELOG.md").Should().BeTrue();
        filter.IsIgnored("DRAFT_x.md").Should().BeTrue();
        filter.IsIgnored("archive/old-notes.md").Should().BeTrue();
        filter.IsIgnored("docs/guide.md").Should().BeFalse();
    }
}
