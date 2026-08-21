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

    private Task<ReconstructionOutcome> ReconstructAsync(
        DocumentTranslationContext context, IReadOnlyList<TranslatedChunk> translated, TranslationProvenance? provenance = null) =>
        _reconstructor.ReconstructAsync(context, translated, repairChunkAsync: null, provenance, CancellationToken.None);

    [Theory]
    [InlineData("simple-paragraph.md")]
    [InlineData("mixed-inline-formatting.md")]
    [InlineData("fenced-code-blocks.md")]
    [InlineData("links-and-urls.md")]
    [InlineData("tables.md")]
    [InlineData("lists-nested.md")]
    [InlineData("blockquotes.md")]
    [InlineData("html-blocks.md")]
    [InlineData("frontmatter.md")]
    [InlineData("admonitions.md")]
    public async Task Reconstruct_AnyFixture_DoesNotThrowAndProducesNonEmptyOutput(string fixtureName)
    {
        var markdown = Fixtures.Load(fixtureName);
        var context = _parser.ParseAndExtractChunks(fixtureName, markdown);
        var translated = FakeTranslate(context.Chunks);

        var outcome = await ReconstructAsync(context, translated);

        outcome.Markdown.Should().NotBeNullOrWhiteSpace();
        outcome.RepairedChunkIds.Should().BeEmpty();
        outcome.UnrecoverableChunkIds.Should().BeEmpty();
    }

    [Fact]
    public async Task Reconstruct_FencedCodeBlocks_CodeContentIsByteIdentical()
    {
        var markdown = Fixtures.Load("fenced-code-blocks.md");
        var context = _parser.ParseAndExtractChunks("fenced-code-blocks.md", markdown);
        var translated = FakeTranslate(context.Chunks);

        var outcome = await ReconstructAsync(context, translated);

        outcome.Markdown.Should().Contain("console.log(`Hello, ${name}! The value of x < y is ${x < y}.`);");
        outcome.Markdown.Should().Contain("var items = new List<string> { \"a\", \"b\" };");
        outcome.Markdown.Should().Contain("if (items.Count > 0 && 1 < 2)");
    }

    [Fact]
    public async Task Reconstruct_LinksAndUrls_UrlsAreByteIdentical()
    {
        var markdown = Fixtures.Load("links-and-urls.md");
        var context = _parser.ParseAndExtractChunks("links-and-urls.md", markdown);
        var translated = FakeTranslate(context.Chunks);

        var outcome = await ReconstructAsync(context, translated);

        outcome.Markdown.Should().Contain("https://docs.example.com/api/v2/reference?token=abc123&lang=en");
        outcome.Markdown.Should().Contain("https://example.com/autolink/path");
        outcome.Markdown.Should().Contain("https://cdn.example.com/images/diagram.png");
    }

    [Fact]
    public async Task Reconstruct_InlineCodeNestedInsideLink_DoesNotThrow()
    {
        // Regression test: a CodeInline's original parent (from Markdig's own parse) is the
        // LinkInline wrapping it, not the paragraph's top-level Inline container - reconstruction
        // must detach the placeholder from that nested parent, not just the block's direct
        // children, or AppendChild throws "Inline has already a parent".
        var markdown = Fixtures.Load("links-and-urls.md");
        var context = _parser.ParseAndExtractChunks("links-and-urls.md", markdown);
        var translated = FakeTranslate(context.Chunks);

        var act = async () => await ReconstructAsync(context, translated);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Reconstruct_InlineCodeNestedInsideLink_PreservesCodeAndUrl()
    {
        var markdown = Fixtures.Load("links-and-urls.md");
        var context = _parser.ParseAndExtractChunks("links-and-urls.md", markdown);
        var translated = FakeTranslate(context.Chunks);

        var outcome = await ReconstructAsync(context, translated);

        outcome.Markdown.Should().Contain("`.doc-terms.json`");
        outcome.Markdown.Should().Contain("../.doc-terms.json");
        outcome.Markdown.Should().Contain("`AstReconstructor`");
    }

    [Fact]
    public async Task Reconstruct_SameContextTwiceForDifferentLanguages_NeitherPassThrows()
    {
        // Mirrors how TranslationOrchestrator actually uses this: the same DocumentTranslationContext
        // (and its live Markdig AST) is reconstructed once per target language, reusing the same
        // stashed placeholder objects each time.
        var markdown = Fixtures.Load("links-and-urls.md");
        var context = _parser.ParseAndExtractChunks("links-and-urls.md", markdown);
        var translated = FakeTranslate(context.Chunks);

        var firstPass = await ReconstructAsync(context, translated);
        var secondPass = await ReconstructAsync(context, translated);

        firstPass.Markdown.Should().Contain("`.doc-terms.json`");
        secondPass.Markdown.Should().Contain("`.doc-terms.json`");
    }

    [Fact]
    public async Task Reconstruct_MixedInlineFormatting_InlineCodeIsByteIdentical()
    {
        var markdown = Fixtures.Load("mixed-inline-formatting.md");
        var context = _parser.ParseAndExtractChunks("mixed-inline-formatting.md", markdown);
        var translated = FakeTranslate(context.Chunks);

        var outcome = await ReconstructAsync(context, translated);

        outcome.Markdown.Should().Contain("`inline code`");
    }

    [Fact]
    public async Task Reconstruct_HtmlBlocks_RawHtmlBlockIsByteIdentical()
    {
        var markdown = Fixtures.Load("html-blocks.md");
        var context = _parser.ParseAndExtractChunks("html-blocks.md", markdown);
        var translated = FakeTranslate(context.Chunks);

        var outcome = await ReconstructAsync(context, translated);

        outcome.Markdown.Should().Contain("<div class=\"warning\">");
        outcome.Markdown.Should().Contain("<strong>Warning:</strong>");
    }

    [Fact]
    public async Task Reconstruct_TranslationMarkers_NeverAppearInsideFencedCodeBlock()
    {
        var markdown = Fixtures.Load("fenced-code-blocks.md");
        var context = _parser.ParseAndExtractChunks("fenced-code-blocks.md", markdown);
        var translated = FakeTranslate(context.Chunks);

        var outcome = await ReconstructAsync(context, translated);

        var codeBlockStart = outcome.Markdown.IndexOf("```javascript", StringComparison.Ordinal);
        var codeBlockEnd = outcome.Markdown.IndexOf("```", codeBlockStart + "```javascript".Length, StringComparison.Ordinal);
        var codeBlockBody = outcome.Markdown[codeBlockStart..(codeBlockEnd + 3)];

        codeBlockBody.Should().NotContain("⟪");
        codeBlockBody.Should().NotContain("⟫");
    }

    [Fact]
    public async Task Reconstruct_EveryChunkIsActuallySpliced_MarkersAppearOnceEachInOutput()
    {
        var markdown = Fixtures.Load("simple-paragraph.md");
        var context = _parser.ParseAndExtractChunks("simple-paragraph.md", markdown);
        var translated = FakeTranslate(context.Chunks);

        var outcome = await ReconstructAsync(context, translated);

        var openCount = outcome.Markdown.Count(c => c == '⟪');
        openCount.Should().Be(context.Chunks.Count);
    }

    [Fact]
    public async Task Reconstruct_WithProvenance_PrependsHeaderComment()
    {
        var markdown = Fixtures.Load("simple-paragraph.md");
        var context = _parser.ParseAndExtractChunks("simple-paragraph.md", markdown);
        var translated = FakeTranslate(context.Chunks);
        var provenance = new TranslationProvenance("abc123", "simple-paragraph.md", "de", DateTimeOffset.UtcNow);

        var outcome = await ReconstructAsync(context, translated, provenance);

        outcome.Markdown.Should().StartWith("<!-- doc-translator: source-hash=abc123;");
    }

    [Fact]
    public async Task Reconstruct_WithoutProvenance_DoesNotPrependHeaderComment()
    {
        var markdown = Fixtures.Load("simple-paragraph.md");
        var context = _parser.ParseAndExtractChunks("simple-paragraph.md", markdown);
        var translated = FakeTranslate(context.Chunks);

        var outcome = await ReconstructAsync(context, translated);

        outcome.Markdown.Should().NotContain("doc-translator: source-hash");
    }

    [Fact]
    public async Task Reconstruct_Frontmatter_SurvivesVerbatimAndStaysBeforeProvenanceHeader()
    {
        // Regression test: Markdig's normalizing renderer doesn't round-trip the YAML frontmatter
        // block's `---` delimiters correctly, and frontmatter must stay the file's first bytes for
        // a static site generator to recognize it - both are handled outside the normal render path
        // (see MarkdigParserService.FrontmatterRawText / AstReconstructor.ReconstructAsync).
        var markdown = Fixtures.Load("frontmatter.md");
        var context = _parser.ParseAndExtractChunks("frontmatter.md", markdown);
        var translated = FakeTranslate(context.Chunks);
        var provenance = new TranslationProvenance("abc123", "frontmatter.md", "de", DateTimeOffset.UtcNow);

        var outcome = await ReconstructAsync(context, translated, provenance);
        var normalized = outcome.Markdown.Replace("\r\n", "\n");

        normalized.Should().StartWith("---\ntitle: Getting Started");
        normalized.Should().Contain("sidebar_position: 1\n---");
        normalized.IndexOf("sidebar_position", StringComparison.Ordinal)
            .Should().BeLessThan(normalized.IndexOf("doc-translator: source-hash", StringComparison.Ordinal));
        normalized.Should().NotContain("⟪title"); // frontmatter itself was never sent for translation
    }

    [Fact]
    public async Task Reconstruct_CustomContainerAdmonition_FenceMarkersSurviveRoundTrip()
    {
        // Regression test: Markdig's CustomContainers extension only registers an HTML renderer,
        // not a Markdown-to-Markdown one - see CustomContainerNormalizeRenderer.
        var markdown = Fixtures.Load("admonitions.md");
        var context = _parser.ParseAndExtractChunks("admonitions.md", markdown);
        var translated = FakeTranslate(context.Chunks);

        var outcome = await ReconstructAsync(context, translated);

        outcome.Markdown.Should().Contain(":::note");
        outcome.Markdown.Should().Contain(":::tip Custom Title");
        outcome.Markdown.Should().Contain("⟪This admonition text");
        outcome.Markdown.Should().Contain("⟪Tip content here.⟫");

        // The closing fence must land on its own line, not glued onto the translated text.
        outcome.Markdown.Replace("\r\n", "\n").Should().Contain("⟫\n:::");
    }
}
