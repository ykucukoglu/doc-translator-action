using DocTranslator.Core.Models;
using DocTranslator.GitHub.Operations;
using FluentAssertions;

namespace DocTranslator.GitHub.Tests;

/// <summary>
/// GITHUB_ACTOR is read directly from the environment (see PrSummaryBuilder), so tests that care
/// about it set/clear it explicitly and restore it afterward - no other test in this class touches
/// that variable, but IDisposable keeps this self-contained regardless.
/// </summary>
public sealed class PrSummaryBuilderTests : IDisposable
{
    private readonly PrSummaryBuilder _sut = new();
    private readonly string? _originalActor = Environment.GetEnvironmentVariable("GITHUB_ACTOR");

    public void Dispose() => Environment.SetEnvironmentVariable("GITHUB_ACTOR", _originalActor);

    [Fact]
    public void Build_LanguagesPresent_RendersTable()
    {
        var summary = new TranslationRunSummary();
        summary.Languages.Add(new LanguageSummary("tr", ChunksTranslated: 5, ChunksFromCache: 2));

        var result = _sut.Build(summary);

        result.Should().Contain("| tr | 5 | 2 |");
    }

    [Fact]
    public void Build_GithubActorSet_ThanksThem()
    {
        Environment.SetEnvironmentVariable("GITHUB_ACTOR", "octocat");

        var result = _sut.Build(new TranslationRunSummary());

        result.Should().Contain("@octocat");
    }

    [Fact]
    public void Build_GithubActorNotSet_OmitsThanksLine()
    {
        Environment.SetEnvironmentVariable("GITHUB_ACTOR", null);

        var result = _sut.Build(new TranslationRunSummary());

        result.Should().NotContain("Thanks @");
    }

    [Fact]
    public void Build_PreservedCounts_RendersPreservedSection()
    {
        var summary = new TranslationRunSummary
        {
            PreservedCodeBlocks = 14,
            PreservedInlineCode = 3,
            PreservedLinks = 7,
        };
        summary.PreservedGlossaryTerms.Add("OpenAI");
        summary.PreservedGlossaryTerms.Add("Docusaurus");

        var result = _sut.Build(summary);

        result.Should().Contain("14 code blocks");
        result.Should().Contain("3 inline code spans");
        result.Should().Contain("7 links");
        result.Should().Contain("OpenAI").And.Contain("Docusaurus");
    }

    [Fact]
    public void Build_NothingPreserved_OmitsPreservedSection()
    {
        var result = _sut.Build(new TranslationRunSummary());

        result.Should().NotContain("Preserved untouched");
    }

    [Fact]
    public void Build_SingularCounts_UseSingularWording()
    {
        var summary = new TranslationRunSummary { PreservedCodeBlocks = 1 };

        var result = _sut.Build(summary);

        result.Should().Contain("1 code block").And.NotContain("1 code blocks");
    }

    [Fact]
    public void Build_Previews_RendersDetailsBlockWithOriginalAndTranslatedColumns()
    {
        var summary = new TranslationRunSummary();
        summary.Previews.Add(new TranslationPreview(
            "docs/guide.md", "tr", [("Getting Started", "Başlarken"), ("Body text.", "Gövde metni.")]));

        var result = _sut.Build(summary);

        result.Should().Contain("<details>");
        result.Should().Contain("docs/guide.md");
        result.Should().Contain("tr");
        result.Should().Contain("| Getting Started | Başlarken |");
        result.Should().Contain("</details>");
    }

    [Fact]
    public void Build_NoPreviews_OmitsPreviewSection()
    {
        var result = _sut.Build(new TranslationRunSummary());

        result.Should().NotContain("### Preview");
    }

    [Fact]
    public void Build_PreviewTextContainsPipe_EscapesItSoTableRowStaysIntact()
    {
        var summary = new TranslationRunSummary();
        summary.Previews.Add(new TranslationPreview("docs/guide.md", "tr", [("a | b", "c | d")]));

        var result = _sut.Build(summary);

        result.Should().Contain("a \\| b").And.Contain("c \\| d");
    }

    [Fact]
    public void Build_LongPreviewText_IsTruncated()
    {
        var longText = new string('x', 500);
        var summary = new TranslationRunSummary();
        summary.Previews.Add(new TranslationPreview("docs/guide.md", "tr", [(longText, longText)]));

        var result = _sut.Build(summary);

        result.Should().Contain("…");
        result.Should().NotContain(longText);
    }

    [Fact]
    public void Build_WarningSections_OnlyRenderedWhenNonEmpty()
    {
        var result = _sut.Build(new TranslationRunSummary());

        result.Should().NotContain("Stale translations detected");
        result.Should().NotContain("Glossary warnings");
    }
}
