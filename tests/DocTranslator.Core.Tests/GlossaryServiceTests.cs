using DocTranslator.Core.Glossary;
using DocTranslator.Core.Models;
using FluentAssertions;

namespace DocTranslator.Core.Tests;

public class GlossaryServiceTests
{
    private readonly GlossaryService _sut = new();

    [Fact]
    public void Load_MissingFile_ReturnsEmptyContext()
    {
        var context = _sut.Load(Path.Combine(AppContext.BaseDirectory, "does-not-exist.json"));

        context.Should().Be(GlossaryContext.Empty);
    }

    [Fact]
    public void Load_SampleFile_ParsesDontTranslateAndCustomMappings()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "sample-doc-terms.json");

        var context = _sut.Load(path);

        context.DontTranslate.Should().Contain("API");
        context.MappingsFor("de").Should().ContainKey("repository").WhoseValue.Should().Be("Repository");
    }

    [Fact]
    public void BuildPromptHint_IncludesDontTranslateTermsAndMappingsForLanguage()
    {
        var glossary = new GlossaryContext(
            new HashSet<string> { "GitHub" },
            new Dictionary<string, IReadOnlyDictionary<string, string>>
            {
                ["de"] = new Dictionary<string, string> { ["repository"] = "Repository" },
            },
            CaseSensitive: false);

        var hint = _sut.BuildPromptHint(glossary, "de");

        hint.Should().Contain("GitHub");
        hint.Should().Contain("repository").And.Contain("Repository");
    }

    [Fact]
    public void BuildPromptHint_EmptyGlossary_ReturnsEmptyString()
    {
        var hint = _sut.BuildPromptHint(GlossaryContext.Empty, "de");

        hint.Should().BeEmpty();
    }

    [Fact]
    public void BuildPromptHint_StyleGuideOnly_IsIncludedEvenWithNoGlossaryTerms()
    {
        var glossary = new GlossaryContext(
            new HashSet<string>(),
            new Dictionary<string, IReadOnlyDictionary<string, string>>(),
            CaseSensitive: false,
            StyleGuide: "Use a formal tone.");

        var hint = _sut.BuildPromptHint(glossary, "de");

        hint.Should().Contain("Use a formal tone.");
    }

    [Fact]
    public void Load_SampleFileWithStyleGuide_ParsesIt()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "sample-doc-terms.json");

        var context = _sut.Load(path);

        context.StyleGuide.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Validate_TermSurvivesAsWholeWord_NoWarning()
    {
        var glossary = new GlossaryContext(new HashSet<string> { "API" }, new Dictionary<string, IReadOnlyDictionary<string, string>>(), CaseSensitive: false);

        var warnings = _sut.Validate("Use the API to connect.", "Benutzen Sie die API zum Verbinden.", glossary, "de");

        warnings.Should().BeEmpty();
    }

    [Fact]
    public void Validate_TermMissingFromTranslation_ProducesWarning()
    {
        var glossary = new GlossaryContext(new HashSet<string> { "API" }, new Dictionary<string, IReadOnlyDictionary<string, string>>(), CaseSensitive: false);

        var warnings = _sut.Validate("Use the API to connect.", "Benutzen Sie die Schnittstelle zum Verbinden.", glossary, "de");

        warnings.Should().ContainSingle().Which.Should().Contain("API");
    }

    [Fact]
    public void Validate_TermOnlyPresentAsSubstringOfAnotherWord_StillProducesWarning()
    {
        // "API" appears inside "CAPITAL" but not as a whole word - must not count as a match,
        // which is exactly why word-boundary matching (not string.Contains) is required.
        var glossary = new GlossaryContext(new HashSet<string> { "API" }, new Dictionary<string, IReadOnlyDictionary<string, string>>(), CaseSensitive: false);

        var warnings = _sut.Validate("Use the API to connect.", "This mentions CAPITAL letters only.", glossary, "de");

        warnings.Should().ContainSingle();
    }

    [Fact]
    public void Validate_CustomMappingRenderingMissing_ProducesWarning()
    {
        var mappings = new Dictionary<string, IReadOnlyDictionary<string, string>>
        {
            ["de"] = new Dictionary<string, string> { ["repository"] = "Repository" },
        };
        var glossary = new GlossaryContext(new HashSet<string>(), mappings, CaseSensitive: false);

        var warnings = _sut.Validate("Clone the repository first.", "Klonen Sie das Depot zuerst.", glossary, "de");

        warnings.Should().ContainSingle().Which.Should().Contain("repository").And.Contain("Repository");
    }

    [Fact]
    public void Validate_CustomMappingTargetAppearsWithAgglutinatedSuffix_NoWarning()
    {
        // Turkish glues case suffixes directly onto native words with no separator - "depo" survives
        // in real translations as "depoya"/"deposunu"/etc. A trailing word-boundary requirement would
        // false-positive on every one of these even though the translation correctly used the term.
        var mappings = new Dictionary<string, IReadOnlyDictionary<string, string>>
        {
            ["tr"] = new Dictionary<string, string> { ["repository"] = "depo" },
        };
        var glossary = new GlossaryContext(new HashSet<string>(), mappings, CaseSensitive: false);

        var warnings = _sut.Validate("Clone the repository first.", "Önce depoyu klonlayın.", glossary, "tr");

        warnings.Should().BeEmpty();
    }

    [Fact]
    public void Validate_TermNotPresentInSourceChunk_IsSkipped()
    {
        var glossary = new GlossaryContext(new HashSet<string> { "API" }, new Dictionary<string, IReadOnlyDictionary<string, string>>(), CaseSensitive: false);

        var warnings = _sut.Validate("Nothing relevant here.", "Nichts Relevantes hier.", glossary, "de");

        warnings.Should().BeEmpty();
    }
}
