using DocTranslator.Core.Models;
using FluentAssertions;

namespace DocTranslator.Core.Tests;

public class TranslationRunSummaryTests
{
    [Fact]
    public void DescribePreservedContent_NothingTracked_ReturnsNull()
    {
        var summary = new TranslationRunSummary();

        summary.DescribePreservedContent().Should().BeNull();
    }

    [Fact]
    public void DescribePreservedContent_SingularCounts_UseSingularWording()
    {
        var summary = new TranslationRunSummary { PreservedCodeBlocks = 1, PreservedInlineCode = 1, PreservedLinks = 1 };

        var description = summary.DescribePreservedContent();

        description.Should().Contain("1 code block").And.NotContain("1 code blocks");
        description.Should().Contain("1 inline code span").And.NotContain("1 inline code spans");
        description.Should().Contain("1 link").And.NotContain("1 links");
    }

    [Fact]
    public void DescribePreservedContent_GlossaryTerms_ListsThemSorted()
    {
        var summary = new TranslationRunSummary();
        summary.PreservedGlossaryTerms.Add("GitHub");
        summary.PreservedGlossaryTerms.Add("API");

        var description = summary.DescribePreservedContent();

        description.Should().Contain("2 glossary terms (API, GitHub)");
    }

    [Fact]
    public void TotalChunkPairs_SumsAcrossAllLanguages()
    {
        var summary = new TranslationRunSummary();
        summary.Languages.Add(new LanguageSummary("tr", ChunksTranslated: 3, ChunksFromCache: 2));
        summary.Languages.Add(new LanguageSummary("de", ChunksTranslated: 1, ChunksFromCache: 4));

        summary.TotalChunkPairs.Should().Be(10);
    }

    [Fact]
    public void CacheHitRatePercent_NoPairs_ReturnsNull()
    {
        var summary = new TranslationRunSummary();

        summary.CacheHitRatePercent.Should().BeNull();
    }

    [Fact]
    public void CacheHitRatePercent_ComputesFractionFromCache()
    {
        var summary = new TranslationRunSummary();
        summary.Languages.Add(new LanguageSummary("tr", ChunksTranslated: 25, ChunksFromCache: 75));

        summary.CacheHitRatePercent.Should().Be(75.0);
    }
}
