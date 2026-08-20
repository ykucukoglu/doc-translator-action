using DocTranslator.Cli.Orchestration;
using FluentAssertions;

namespace DocTranslator.Cli.Tests;

public sealed class OutputPathResolverTests
{
    private readonly OutputPathResolver _sut = new();

    [Fact]
    public void Resolve_DefaultTemplate_ProducesLanguageSubfolder()
    {
        var result = _sut.Resolve("docs/{lang}/{relativePath}", "tr", "guide.md");

        result.Should().Be("docs/tr/guide.md");
    }

    [Fact]
    public void Resolve_NestedSourcePath_PreservesSubdirectory()
    {
        var result = _sut.Resolve("docs/{lang}/{relativePath}", "tr", "guides/getting-started.md");

        result.Should().Be("docs/tr/guides/getting-started.md");
    }

    [Fact]
    public void Resolve_CoLocatedTemplate_InsertsLangBeforeExtension()
    {
        var result = _sut.Resolve("{dir}/{filename}.{lang}.{ext}", "de", "docs/guide.md");

        result.Should().Be("docs/guide.de.md");
    }

    [Fact]
    public void Resolve_RootLevelFile_DoesNotLeaveLeadingSlash()
    {
        // {dir} is empty for a root-level file - without collapsing, "{dir}/{filename}..." would
        // resolve to a leading "/".
        var result = _sut.Resolve("{dir}/{filename}.{lang}.{ext}", "de", "guide.md");

        result.Should().Be("guide.de.md");
    }
}
