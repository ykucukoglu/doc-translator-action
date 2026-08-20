using DocTranslator.Core.Models;
using DocTranslator.Core.Parsing;
using FluentAssertions;

namespace DocTranslator.Core.Tests;

public class MarkdigParserServiceTests
{
    private readonly MarkdigParserService _sut = new();

    [Fact]
    public void ParseAndExtractChunks_SimpleParagraph_ExtractsHeadingAndParagraphChunks()
    {
        var markdown = Fixtures.Load("simple-paragraph.md");

        var context = _sut.ParseAndExtractChunks("simple-paragraph.md", markdown);

        context.Chunks.Should().HaveCount(2);
        context.Chunks[0].BlockKind.Should().Be(BlockKind.Heading);
        context.Chunks[0].SourceText.Should().Be("Welcome");
        context.Chunks[1].BlockKind.Should().Be(BlockKind.Paragraph);
        context.Chunks[1].SourceText.Should().Contain("simple paragraph");
    }

    [Fact]
    public void ParseAndExtractChunks_FencedCodeBlocks_NeverLeakCodeContentIntoChunks()
    {
        var markdown = Fixtures.Load("fenced-code-blocks.md");

        var context = _sut.ParseAndExtractChunks("fenced-code-blocks.md", markdown);

        var allSourceText = string.Join('\n', context.Chunks.Select(c => c.SourceText));
        allSourceText.Should().NotContain("console.log");
        allSourceText.Should().NotContain("function greet");
        allSourceText.Should().NotContain("List<string>");
        allSourceText.Should().NotContain("Console.WriteLine");
    }

    [Fact]
    public void ParseAndExtractChunks_HtmlBlocks_NeverLeakRawHtmlBlockContentIntoChunks()
    {
        var markdown = Fixtures.Load("html-blocks.md");

        var context = _sut.ParseAndExtractChunks("html-blocks.md", markdown);

        var allSourceText = string.Join('\n', context.Chunks.Select(c => c.SourceText));
        allSourceText.Should().NotContain("class=\"warning\"");
        allSourceText.Should().NotContain("<div");
    }

    [Fact]
    public void ParseAndExtractChunks_Table_ExtractsOneChunkPerCell()
    {
        var markdown = Fixtures.Load("tables.md");

        var context = _sut.ParseAndExtractChunks("tables.md", markdown);

        context.Chunks.Where(c => c.BlockKind == BlockKind.TableCell).Should().HaveCountGreaterThan(0);
        context.Chunks.Should().Contain(c => c.SourceText == "Build");
    }

    [Fact]
    public void ParseAndExtractChunks_EveryChunk_HasAMatchingReconstructionContext()
    {
        var markdown = Fixtures.Load("mixed-inline-formatting.md");

        var context = _sut.ParseAndExtractChunks("mixed-inline-formatting.md", markdown);

        foreach (var chunk in context.Chunks)
        {
            context.ReconstructionMap.Should().ContainKey(chunk.ChunkId);
        }
    }

    [Fact]
    public void ParseAndExtractChunks_SameSourceText_ProducesSameContentHash()
    {
        var context = _sut.ParseAndExtractChunks("a.md", "# Title\n\nSame text.\n");
        var context2 = _sut.ParseAndExtractChunks("b.md", "# Title\n\nSame text.\n");

        context.Chunks[1].ContentHash.Should().Be(context2.Chunks[1].ContentHash);
    }
}
