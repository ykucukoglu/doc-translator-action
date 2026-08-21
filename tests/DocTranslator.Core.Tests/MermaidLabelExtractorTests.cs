using DocTranslator.Core.Diagrams;
using FluentAssertions;

namespace DocTranslator.Core.Tests;

public class MermaidLabelExtractorTests
{
    [Fact]
    public void IsSupportedDiagram_Flowchart_ReturnsTrue()
    {
        MermaidLabelExtractor.IsSupportedDiagram("flowchart TD\nA[Start] --> B[End]").Should().BeTrue();
    }

    [Fact]
    public void IsSupportedDiagram_Graph_ReturnsTrue()
    {
        MermaidLabelExtractor.IsSupportedDiagram("graph LR\nA --> B").Should().BeTrue();
    }

    [Fact]
    public void IsSupportedDiagram_SequenceDiagram_ReturnsFalse()
    {
        MermaidLabelExtractor.IsSupportedDiagram("sequenceDiagram\nAlice->>Bob: Hello").Should().BeFalse();
    }

    [Fact]
    public void IsSupportedDiagram_SkipsLeadingCommentDirective()
    {
        MermaidLabelExtractor.IsSupportedDiagram("%%{init: {'theme':'dark'}}%%\nflowchart TD\nA-->B").Should().BeTrue();
    }

    [Fact]
    public void ExtractLabels_RectangleNodes_FindsBothLabelsNotIds()
    {
        var spans = MermaidLabelExtractor.ExtractLabels("flowchart TD\nA[Start Here] --> B[End Here]");

        spans.Select(s => s.Text).Should().BeEquivalentTo(["Start Here", "End Here"]);
        spans.Should().NotContain(s => s.Text == "A" || s.Text == "B");
    }

    [Theory]
    [InlineData("flowchart TD\nA([Stadium Label]) --> B", "Stadium Label")]
    [InlineData("flowchart TD\nA[[Subroutine Label]] --> B", "Subroutine Label")]
    [InlineData("flowchart TD\nA[(Cylinder Label)] --> B", "Cylinder Label")]
    [InlineData("flowchart TD\nA((Circle Label)) --> B", "Circle Label")]
    [InlineData("flowchart TD\nA{{Hexagon Label}} --> B", "Hexagon Label")]
    [InlineData("flowchart TD\nA(Round Label) --> B", "Round Label")]
    [InlineData("flowchart TD\nA{Diamond Label} --> B", "Diamond Label")]
    public void ExtractLabels_EachNodeShape_ExtractsItsLabel(string diagram, string expectedLabel)
    {
        var spans = MermaidLabelExtractor.ExtractLabels(diagram);

        spans.Should().Contain(s => s.Text == expectedLabel);
    }

    [Fact]
    public void ExtractLabels_EdgeLabel_IsExtracted()
    {
        var spans = MermaidLabelExtractor.ExtractLabels("flowchart TD\nA -->|Yes, proceed| B");

        spans.Should().Contain(s => s.Text == "Yes, proceed");
    }

    [Fact]
    public void ExtractLabels_QuotedLabelContainingDelimiterChars_IsExtractedWithoutQuotes()
    {
        var spans = MermaidLabelExtractor.ExtractLabels("flowchart TD\nA[\"Label (with parens)\"] --> B");

        spans.Should().ContainSingle(s => s.Text == "Label (with parens)");
    }

    [Fact]
    public void ExtractLabels_BareSubgraphTitle_IsExtracted()
    {
        var spans = MermaidLabelExtractor.ExtractLabels("flowchart TD\nsubgraph My Subgraph Title\nA --> B\nend");

        spans.Should().ContainSingle(s => s.Text == "My Subgraph Title");
    }

    [Fact]
    public void ExtractLabels_SubgraphWithIdAndBracket_ExtractsOnlyTheBracketLabel()
    {
        var spans = MermaidLabelExtractor.ExtractLabels("flowchart TD\nsubgraph sub1 [Bracket Title]\nA --> B\nend");

        spans.Select(s => s.Text).Should().BeEquivalentTo(["Bracket Title"]);
    }

    [Fact]
    public void ExtractLabels_UnsupportedDiagramType_ReturnsEmpty()
    {
        var spans = MermaidLabelExtractor.ExtractLabels("classDiagram\nAnimal <|-- Duck");

        spans.Should().BeEmpty();
    }

    [Fact]
    public void ExtractLabels_CommentLine_IsSkipped()
    {
        var spans = MermaidLabelExtractor.ExtractLabels("flowchart TD\n%% A[Should not be extracted]\nB[Should be extracted]");

        spans.Select(s => s.Text).Should().Equal(["Should be extracted"]);
    }

    [Fact]
    public void ExtractLabels_EmptyLabel_IsSkipped()
    {
        var spans = MermaidLabelExtractor.ExtractLabels("flowchart TD\nA[\"\"] --> B[Real Label]");

        spans.Select(s => s.Text).Should().Equal(["Real Label"]);
    }

    [Fact]
    public void ExtractLabels_SpanOffsets_PointExactlyAtLabelTextWithinRawText()
    {
        var raw = "flowchart TD\nA[Start Here] --> B[End]";

        var spans = MermaidLabelExtractor.ExtractLabels(raw);

        foreach (var span in spans)
        {
            raw.Substring(span.Start, span.Length).Should().Be(span.Text);
        }
    }

    [Fact]
    public void ExtractLabels_MultipleNodesOnOneLine_FindsAllOfThem()
    {
        var spans = MermaidLabelExtractor.ExtractLabels("flowchart LR\nA[First] --> B[Second] --> C[Third]");

        spans.Select(s => s.Text).Should().BeEquivalentTo(["First", "Second", "Third"]);
    }
}
