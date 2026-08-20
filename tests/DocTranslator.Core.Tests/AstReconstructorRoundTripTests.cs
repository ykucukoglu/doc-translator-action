using DocTranslator.Core.Models;
using DocTranslator.Core.Parsing;
using DocTranslator.Core.Reconstruction;
using FluentAssertions;

namespace DocTranslator.Core.Tests;

/// <summary>
/// The core correctness gate for the whole system: parse a fixture, extract chunks, apply a
/// "fake translation" that visibly wraps each chunk (proving it was actually touched) without
/// altering its embedded placeholder/tag markers, reconstruct, and render. Code/link/URL content
/// must be byte-identical to the source; whole-file byte equality is NOT asserted, since Markdig's
/// renderer normalizes non-semantic formatting on re-render (see the implementation plan).
/// </summary>
public class AstReconstructorRoundTripTests
{
    private readonly MarkdigParserService _parser = new();
    private readonly AstReconstructor _reconstructor = new();

    private static List<TranslatedChunk> FakeTranslate(IReadOnlyList<TranslationChunk> chunks) =>
        chunks.Select(c => new TranslatedChunk(c.ChunkId, $"⟪{c.SourceText}⟫")).ToList();

    [Theory]
    [InlineData("simple-paragraph.md")]
    [InlineData("mixed-inline-formatting.md")]
    [InlineData("fenced-code-blocks.md")]
    [InlineData("links-and-urls.md")]
    [InlineData("tables.md")]
    [InlineData("lists-nested.md")]
    [InlineData("blockquotes.md")]
    [InlineData("html-blocks.md")]
    public void Reconstruct_AnyFixture_DoesNotThrowAndProducesNonEmptyOutput(string fixtureName)
    {
        var markdown = Fixtures.Load(fixtureName);
        var context = _parser.ParseAndExtractChunks(fixtureName, markdown);
        var translated = FakeTranslate(context.Chunks);

        var output = _reconstructor.Reconstruct(context, translated);

        output.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Reconstruct_FencedCodeBlocks_CodeContentIsByteIdentical()
    {
        var markdown = Fixtures.Load("fenced-code-blocks.md");
        var context = _parser.ParseAndExtractChunks("fenced-code-blocks.md", markdown);
        var translated = FakeTranslate(context.Chunks);

        var output = _reconstructor.Reconstruct(context, translated);

        output.Should().Contain("console.log(`Hello, ${name}! The value of x < y is ${x < y}.`);");
        output.Should().Contain("var items = new List<string> { \"a\", \"b\" };");
        output.Should().Contain("if (items.Count > 0 && 1 < 2)");
    }

    [Fact]
    public void Reconstruct_LinksAndUrls_UrlsAreByteIdentical()
    {
        var markdown = Fixtures.Load("links-and-urls.md");
        var context = _parser.ParseAndExtractChunks("links-and-urls.md", markdown);
        var translated = FakeTranslate(context.Chunks);

        var output = _reconstructor.Reconstruct(context, translated);

        output.Should().Contain("https://docs.example.com/api/v2/reference?token=abc123&lang=en");
        output.Should().Contain("https://example.com/autolink/path");
        output.Should().Contain("https://cdn.example.com/images/diagram.png");
    }

    [Fact]
    public void Reconstruct_MixedInlineFormatting_InlineCodeIsByteIdentical()
    {
        var markdown = Fixtures.Load("mixed-inline-formatting.md");
        var context = _parser.ParseAndExtractChunks("mixed-inline-formatting.md", markdown);
        var translated = FakeTranslate(context.Chunks);

        var output = _reconstructor.Reconstruct(context, translated);

        output.Should().Contain("`inline code`");
    }

    [Fact]
    public void Reconstruct_HtmlBlocks_RawHtmlBlockIsByteIdentical()
    {
        var markdown = Fixtures.Load("html-blocks.md");
        var context = _parser.ParseAndExtractChunks("html-blocks.md", markdown);
        var translated = FakeTranslate(context.Chunks);

        var output = _reconstructor.Reconstruct(context, translated);

        output.Should().Contain("<div class=\"warning\">");
        output.Should().Contain("<strong>Warning:</strong>");
    }

    [Fact]
    public void Reconstruct_TranslationMarkers_NeverAppearInsideFencedCodeBlock()
    {
        var markdown = Fixtures.Load("fenced-code-blocks.md");
        var context = _parser.ParseAndExtractChunks("fenced-code-blocks.md", markdown);
        var translated = FakeTranslate(context.Chunks);

        var output = _reconstructor.Reconstruct(context, translated);

        var codeBlockStart = output.IndexOf("```javascript", StringComparison.Ordinal);
        var codeBlockEnd = output.IndexOf("```", codeBlockStart + "```javascript".Length, StringComparison.Ordinal);
        var codeBlockBody = output[codeBlockStart..(codeBlockEnd + 3)];

        codeBlockBody.Should().NotContain("⟪");
        codeBlockBody.Should().NotContain("⟫");
    }

    [Fact]
    public void Reconstruct_EveryChunkIsActuallySpliced_MarkersAppearOnceEachInOutput()
    {
        var markdown = Fixtures.Load("simple-paragraph.md");
        var context = _parser.ParseAndExtractChunks("simple-paragraph.md", markdown);
        var translated = FakeTranslate(context.Chunks);

        var output = _reconstructor.Reconstruct(context, translated);

        var openCount = output.Count(c => c == '⟪');
        openCount.Should().Be(context.Chunks.Count);
    }

    [Fact]
    public void Reconstruct_WithProvenance_PrependsHeaderComment()
    {
        var markdown = Fixtures.Load("simple-paragraph.md");
        var context = _parser.ParseAndExtractChunks("simple-paragraph.md", markdown);
        var translated = FakeTranslate(context.Chunks);
        var provenance = new TranslationProvenance("abc123", "simple-paragraph.md", "de", DateTimeOffset.UtcNow);

        var output = _reconstructor.Reconstruct(context, translated, provenance);

        output.Should().StartWith("<!-- doc-translator: source-hash=abc123;");
    }

    [Fact]
    public void Reconstruct_WithoutProvenance_DoesNotPrependHeaderComment()
    {
        var markdown = Fixtures.Load("simple-paragraph.md");
        var context = _parser.ParseAndExtractChunks("simple-paragraph.md", markdown);
        var translated = FakeTranslate(context.Chunks);

        var output = _reconstructor.Reconstruct(context, translated);

        output.Should().NotContain("doc-translator: source-hash");
    }
}
