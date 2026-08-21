using DocTranslator.Core.Diagrams;
using FluentAssertions;

namespace DocTranslator.Core.Tests;

public class FrontmatterFieldExtractorTests
{
    [Fact]
    public void ExtractTranslatableFields_YamlTitleQuoted_ExtractsWithoutQuotes()
    {
        var raw = "---\ntitle: \"Installation Guide\"\nsidebar_position: 1\n---";

        var spans = FrontmatterFieldExtractor.ExtractTranslatableFields(raw);

        spans.Should().ContainSingle(s => s.Text == "Installation Guide");
    }

    [Fact]
    public void ExtractTranslatableFields_YamlTitleUnquoted_IsExtracted()
    {
        var raw = "---\ntitle: Installation Guide\n---";

        var spans = FrontmatterFieldExtractor.ExtractTranslatableFields(raw);

        spans.Should().ContainSingle(s => s.Text == "Installation Guide");
    }

    [Fact]
    public void ExtractTranslatableFields_SidebarPositionNumber_IsNeverExtracted()
    {
        var raw = "---\ntitle: \"Installation Guide\"\nsidebar_position: 1\n---";

        var spans = FrontmatterFieldExtractor.ExtractTranslatableFields(raw);

        spans.Should().NotContain(s => s.Text == "1");
    }

    [Fact]
    public void ExtractTranslatableFields_SlugNotOnAllowlist_IsNeverExtracted()
    {
        var raw = "---\ntitle: \"Installation Guide\"\nslug: \"/docs/install\"\n---";

        var spans = FrontmatterFieldExtractor.ExtractTranslatableFields(raw);

        spans.Should().ContainSingle();
        spans.Should().NotContain(s => s.Text == "/docs/install");
    }

    [Fact]
    public void ExtractTranslatableFields_TagsArray_IsNeverExtracted()
    {
        var raw = "---\ntitle: \"Installation Guide\"\ntags: [setup, quickstart]\n---";

        var spans = FrontmatterFieldExtractor.ExtractTranslatableFields(raw);

        spans.Should().ContainSingle(s => s.Text == "Installation Guide");
    }

    [Fact]
    public void ExtractTranslatableFields_BooleanValue_IsNeverExtracted()
    {
        // "title" is on the allowlist - this proves a bare boolean value is rejected by
        // LooksLikeTranslatableScalar even when its key would otherwise qualify.
        var raw = "---\ntitle: true\n---";

        var spans = FrontmatterFieldExtractor.ExtractTranslatableFields(raw);

        spans.Should().BeEmpty();
    }

    [Fact]
    public void ExtractTranslatableFields_AllFourAllowlistedKeys_AreAllExtracted()
    {
        var raw = "---\ntitle: Title\ndescription: Description\nsidebar_label: Sidebar Label\nlabel: Label\n---";

        var spans = FrontmatterFieldExtractor.ExtractTranslatableFields(raw);

        spans.Select(s => s.Text).Should().BeEquivalentTo(["Title", "Description", "Sidebar Label", "Label"]);
    }

    [Fact]
    public void ExtractTranslatableFields_TomlFrontmatter_UsesEqualsSyntax()
    {
        var raw = "+++\ntitle = \"Installation Guide\"\nweight = 1\n+++";

        var spans = FrontmatterFieldExtractor.ExtractTranslatableFields(raw);

        spans.Should().ContainSingle(s => s.Text == "Installation Guide");
        spans.Should().NotContain(s => s.Text == "1");
    }

    [Fact]
    public void ExtractTranslatableFields_NoFrontmatter_ReturnsEmpty()
    {
        var spans = FrontmatterFieldExtractor.ExtractTranslatableFields("not frontmatter at all");

        spans.Should().BeEmpty();
    }

    [Fact]
    public void ExtractTranslatableFields_ContentAfterClosingFence_IsIgnored()
    {
        // Guards against accidentally treating the body of the document (which can legitimately
        // contain a line that is exactly "title: something" inside a code block, for example) as
        // more frontmatter once the real closing fence has already been seen.
        var raw = "---\ntitle: \"Real Title\"\n---\n\ntitle: this is body text, not frontmatter";

        var spans = FrontmatterFieldExtractor.ExtractTranslatableFields(raw);

        spans.Should().ContainSingle(s => s.Text == "Real Title");
    }

    [Fact]
    public void ExtractTranslatableFields_SpanOffsets_PointExactlyAtValueTextWithinRawText()
    {
        var raw = "---\ntitle: \"Installation Guide\"\ndescription: A guide\n---";

        var spans = FrontmatterFieldExtractor.ExtractTranslatableFields(raw);

        foreach (var span in spans)
        {
            raw.Substring(span.Start, span.Length).Should().Be(span.Text);
        }
    }
}
