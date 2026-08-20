using DocTranslator.Core.Parsing;
using FluentAssertions;

namespace DocTranslator.Core.Tests;

public class ReconstructionScannerTests
{
    private readonly ReconstructionScanner _sut = new();

    [Fact]
    public void Parse_PlainText_ProducesSingleTextRun()
    {
        var nodes = _sut.Parse("hello world");

        nodes.Should().ContainSingle().Which.Should().BeOfType<TextRunNode>()
            .Which.Text.Should().Be("hello world");
    }

    [Fact]
    public void Parse_Placeholder_ProducesPlaceholderRefWithCorrectIndex()
    {
        var nodes = _sut.Parse("before ⟦CODE3⟧ after");

        nodes.Should().HaveCount(3);
        nodes[1].Should().BeOfType<PlaceholderRefNode>().Which.Index.Should().Be(3);
    }

    [Fact]
    public void Parse_NestedTags_ProducesNestedTaggedSpanNodes()
    {
        var nodes = _sut.Parse("<strong0>bold <em1>italic</em1> text</strong0>");

        var strong = nodes.Should().ContainSingle().Which.Should().BeOfType<TaggedSpanNode>().Subject;
        strong.TagName.Should().Be("strong");
        strong.Index.Should().Be(0);
        strong.Children.Should().HaveCount(3);
        var em = strong.Children[1].Should().BeOfType<TaggedSpanNode>().Subject;
        em.TagName.Should().Be("em");
        em.Index.Should().Be(1);
    }

    [Fact]
    public void Parse_LiteralLessThan_NotFollowedByKnownTag_TreatedAsLiteralText()
    {
        var nodes = _sut.Parse("5 < 10 and 20 > 3");

        nodes.Should().ContainSingle().Which.Should().BeOfType<TextRunNode>()
            .Which.Text.Should().Be("5 < 10 and 20 > 3");
    }

    [Fact]
    public void Parse_UnclosedTag_ThrowsReconstructionParseException()
    {
        var act = () => _sut.Parse("<em0>oops, never closed");

        act.Should().Throw<ReconstructionParseException>();
    }

    [Fact]
    public void Parse_MismatchedClosingTag_ThrowsReconstructionParseException()
    {
        var act = () => _sut.Parse("<em0>text</strong0>");

        act.Should().Throw<ReconstructionParseException>();
    }

    [Fact]
    public void Parse_UnmatchedClosingTagAtTopLevel_ThrowsReconstructionParseException()
    {
        var act = () => _sut.Parse("text</em0>");

        act.Should().Throw<ReconstructionParseException>();
    }

    [Fact]
    public void Parse_MalformedPlaceholderMissingClosingBracket_Throws()
    {
        var act = () => _sut.Parse("before ⟦CODE0 after");

        act.Should().Throw<ReconstructionParseException>();
    }

    [Fact]
    public void Parse_LinkTagWrappingText_ProducesCorrectTree()
    {
        var nodes = _sut.Parse("<link0>click here</link0>");

        var link = nodes.Should().ContainSingle().Which.Should().BeOfType<TaggedSpanNode>().Subject;
        link.TagName.Should().Be("link");
        link.Children.Should().ContainSingle().Which.Should().BeOfType<TextRunNode>()
            .Which.Text.Should().Be("click here");
    }
}
