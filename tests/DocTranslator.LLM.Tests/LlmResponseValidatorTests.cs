using DocTranslator.Core.Models;
using DocTranslator.LLM.Dto;
using DocTranslator.LLM.Exceptions;
using DocTranslator.LLM.Retry;
using FluentAssertions;

namespace DocTranslator.LLM.Tests;

public class LlmResponseValidatorTests
{
    private readonly LlmResponseValidator _sut = new();

    private static TranslationChunk Chunk(string id) => new(id, "text", ContentHash: "h", BlockKind.Paragraph, "doc.md");

    [Fact]
    public void ValidateChunkIdsMatch_ExactMatch_DoesNotThrow()
    {
        var requested = new[] { Chunk("c1"), Chunk("c2") };
        var result = new TranslationBatchResult([new TranslationItem("c1", "a"), new TranslationItem("c2", "b")]);

        var act = () => _sut.ValidateChunkIdsMatch(requested, result);

        act.Should().NotThrow();
    }

    [Fact]
    public void ValidateChunkIdsMatch_MissingChunk_Throws()
    {
        var requested = new[] { Chunk("c1"), Chunk("c2") };
        var result = new TranslationBatchResult([new TranslationItem("c1", "a")]);

        var act = () => _sut.ValidateChunkIdsMatch(requested, result);

        act.Should().Throw<LlmTranslationException>().WithMessage("*missing*c2*");
    }

    [Fact]
    public void ValidateChunkIdsMatch_ExtraChunk_Throws()
    {
        var requested = new[] { Chunk("c1") };
        var result = new TranslationBatchResult([new TranslationItem("c1", "a"), new TranslationItem("c2", "b")]);

        var act = () => _sut.ValidateChunkIdsMatch(requested, result);

        act.Should().Throw<LlmTranslationException>().WithMessage("*unexpected*c2*");
    }

    [Fact]
    public void ValidateChunkIdsMatch_DuplicateChunk_Throws()
    {
        var requested = new[] { Chunk("c1") };
        var result = new TranslationBatchResult([new TranslationItem("c1", "a"), new TranslationItem("c1", "a-again")]);

        var act = () => _sut.ValidateChunkIdsMatch(requested, result);

        act.Should().Throw<LlmTranslationException>().WithMessage("*duplicated*c1*");
    }
}
