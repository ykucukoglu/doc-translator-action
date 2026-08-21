using DocTranslator.Core.Glossary;
using DocTranslator.Core.Models;
using DocTranslator.LLM.Prompting;
using FluentAssertions;
using Microsoft.Extensions.AI;

namespace DocTranslator.LLM.Tests;

public class PromptBuilderTests
{
    private readonly PromptBuilder _sut = new(new GlossaryService());

    private static TranslationChunk Chunk(string id, string text) =>
        new(id, text, ContentHash: "h", BlockKind.Paragraph, "doc.md");

    [Fact]
    public void Build_ReturnsSystemMessageThenUserMessage()
    {
        var messages = _sut.Build([Chunk("c1", "hello")], "de", GlossaryContext.Empty);

        messages.Should().HaveCount(2);
        messages[0].Role.Should().Be(ChatRole.System);
        messages[1].Role.Should().Be(ChatRole.User);
    }

    [Fact]
    public void Build_UserMessage_ContainsEveryChunkIdAndSourceText()
    {
        var messages = _sut.Build([Chunk("c1", "first"), Chunk("c2", "second")], "de", GlossaryContext.Empty);

        var userText = messages[1].Text;
        userText.Should().Contain("[c1]").And.Contain("first");
        userText.Should().Contain("[c2]").And.Contain("second");
    }

    [Fact]
    public void Build_SystemMessage_ExplainsPlaceholderAndTagPreservation()
    {
        var messages = _sut.Build([Chunk("c1", "hello")], "de", GlossaryContext.Empty);

        var systemText = messages[0].Text;
        systemText.Should().Contain("⟦CODE0⟧");
        systemText.Should().Contain("<em0>");
    }

    [Fact]
    public void Build_WithGlossary_IncludesGlossaryHintInSystemMessage()
    {
        var glossary = new GlossaryContext(
            new HashSet<string> { "GitHub" },
            new Dictionary<string, IReadOnlyDictionary<string, string>>(),
            CaseSensitive: false);

        var messages = _sut.Build([Chunk("c1", "hello")], "de", glossary);

        messages[0].Text.Should().Contain("GitHub");
    }

    [Fact]
    public void Build_WithStyleGuide_IncludesItInSystemMessage()
    {
        var glossary = new GlossaryContext(
            new HashSet<string>(),
            new Dictionary<string, IReadOnlyDictionary<string, string>>(),
            CaseSensitive: false,
            StyleGuide: "Use a formal tone.");

        var messages = _sut.Build([Chunk("c1", "hello")], "de", glossary);

        messages[0].Text.Should().Contain("Use a formal tone.");
    }

    [Fact]
    public void Build_TargetLanguageCode_AppearsInSystemMessage()
    {
        var messages = _sut.Build([Chunk("c1", "hello")], "fr", GlossaryContext.Empty);

        messages[0].Text.Should().Contain("fr");
    }

    [Fact]
    public void Build_ExplicitSourceLanguage_AppearsInSystemMessage()
    {
        var messages = _sut.Build([Chunk("c1", "hello")], "fr", GlossaryContext.Empty, sourceLanguage: "en");

        messages[0].Text.Should().Contain("from the language with code 'en'");
    }

    [Fact]
    public void Build_AutoSourceLanguage_OmitsFromClause()
    {
        var messages = _sut.Build([Chunk("c1", "hello")], "fr", GlossaryContext.Empty, sourceLanguage: "auto");

        messages[0].Text.Should().NotContain("from the language");
    }
}
