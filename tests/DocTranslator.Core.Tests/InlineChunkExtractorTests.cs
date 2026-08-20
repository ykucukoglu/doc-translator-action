using DocTranslator.Core.Extensions;
using DocTranslator.Core.Models;
using DocTranslator.Core.Parsing;
using FluentAssertions;
using Markdig.Syntax;

namespace DocTranslator.Core.Tests;

public class InlineChunkExtractorTests
{
    private static (string Encoded, BlockReconstructionContext Context) EncodeParagraph(string markdown)
    {
        var document = Markdig.Markdown.Parse(markdown, MarkdigConfiguration.Pipeline);
        var paragraph = document.Descendants<ParagraphBlock>().First();
        var context = new BlockReconstructionContext { TargetBlock = paragraph };
        var extractor = new InlineChunkExtractor();
        var encoded = extractor.Encode(paragraph.Inline!, context);
        return (encoded, context);
    }

    [Fact]
    public void Encode_NestedEmphasis_ProducesNestedTagsWithCorrectMetadata()
    {
        var (encoded, context) = EncodeParagraph("**bold with *italic* inside**");

        encoded.Should().Be("<strong0>bold with <em1>italic</em1> inside</strong0>");
        context.EmphasisTags[0].DelimiterCount.Should().Be(2);
        context.EmphasisTags[1].DelimiterCount.Should().Be(1);
    }

    [Fact]
    public void Encode_LinkContainingCode_WrapsLinkTagAroundCodePlaceholder()
    {
        var (encoded, context) = EncodeParagraph("[`code`](https://example.com)");

        encoded.Should().Be("<link0>⟦CODE0⟧</link0>");
        context.LinkTags[0].Url.Should().Be("https://example.com");
        context.LinkTags[0].IsImage.Should().BeFalse();
    }

    [Fact]
    public void Encode_Image_SetsIsImageTrue()
    {
        var (encoded, context) = EncodeParagraph("![alt text](https://example.com/img.png)");

        encoded.Should().Be("<link0>alt text</link0>");
        context.LinkTags[0].IsImage.Should().BeTrue();
        context.LinkTags[0].Url.Should().Be("https://example.com/img.png");
    }

    [Fact]
    public void Encode_Autolink_ProducesAtomicPlaceholder()
    {
        var (encoded, context) = EncodeParagraph("<https://example.com>");

        encoded.Should().Be("⟦AUTOLINK0⟧");
        context.AtomicPlaceholders.Should().ContainKey(0);
    }

    [Fact]
    public void Encode_RawInlineHtml_ProducesOneAtomicPlaceholderPerTag()
    {
        var (encoded, context) = EncodeParagraph("before <span class=\"x\">middle</span> after");

        encoded.Should().Contain("⟦HTML0⟧middle⟦HTML1⟧");
        context.AtomicPlaceholders.Should().HaveCount(2);
    }

    [Fact]
    public void Encode_PlainText_PassesThroughUnchanged()
    {
        var (encoded, _) = EncodeParagraph("just plain text");

        encoded.Should().Be("just plain text");
    }

    [Fact]
    public void Encode_InlineCode_ProducesAtomicPlaceholderNotTranslatableText()
    {
        var (encoded, context) = EncodeParagraph("run `dotnet build` now");

        encoded.Should().Be("run ⟦CODE0⟧ now");
        context.AtomicPlaceholders[0].Should().BeOfType<Markdig.Syntax.Inlines.CodeInline>();
    }
}
