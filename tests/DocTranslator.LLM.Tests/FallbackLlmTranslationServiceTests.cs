using DocTranslator.Core.Glossary;
using DocTranslator.Core.Models;
using DocTranslator.LLM.Exceptions;
using DocTranslator.LLM.Services;
using FluentAssertions;
using Moq;

namespace DocTranslator.LLM.Tests;

public class FallbackLlmTranslationServiceTests
{
    private static TranslationChunk Chunk(string id) => new(id, "text", ContentHash: "h", BlockKind.Paragraph, "doc.md");

    [Fact]
    public async Task TranslateAsync_PrimarySucceeds_FallbackIsNeverCalled()
    {
        var primary = new Mock<ILlmTranslationService>();
        primary.Setup(p => p.TranslateAsync(It.IsAny<IReadOnlyList<TranslationChunk>>(), "de", It.IsAny<GlossaryContext>(), It.IsAny<CancellationToken>(), "auto"))
            .ReturnsAsync((IReadOnlyList<TranslatedChunk>)[new TranslatedChunk("c1", "translated")]);
        var fallback = new Mock<ILlmTranslationService>();
        var sut = new FallbackLlmTranslationService(primary.Object, fallback.Object);

        var result = await sut.TranslateAsync([Chunk("c1")], "de", GlossaryContext.Empty, CancellationToken.None);

        result.Should().ContainSingle(t => t.TranslatedText == "translated");
        fallback.Verify(f => f.TranslateAsync(It.IsAny<IReadOnlyList<TranslationChunk>>(), It.IsAny<string>(), It.IsAny<GlossaryContext>(), It.IsAny<CancellationToken>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task TranslateAsync_PrimaryThrows_FallsBackAndReturnsFallbackResult()
    {
        var primary = new Mock<ILlmTranslationService>();
        primary.Setup(p => p.TranslateAsync(It.IsAny<IReadOnlyList<TranslationChunk>>(), It.IsAny<string>(), It.IsAny<GlossaryContext>(), It.IsAny<CancellationToken>(), It.IsAny<string>()))
            .ThrowsAsync(new LlmTranslationException("primary is down"));
        var fallback = new Mock<ILlmTranslationService>();
        fallback.Setup(f => f.TranslateAsync(It.IsAny<IReadOnlyList<TranslationChunk>>(), "de", It.IsAny<GlossaryContext>(), It.IsAny<CancellationToken>(), "auto"))
            .ReturnsAsync((IReadOnlyList<TranslatedChunk>)[new TranslatedChunk("c1", "fallback translated")]);
        var sut = new FallbackLlmTranslationService(primary.Object, fallback.Object);

        var result = await sut.TranslateAsync([Chunk("c1")], "de", GlossaryContext.Empty, CancellationToken.None);

        result.Should().ContainSingle(t => t.TranslatedText == "fallback translated");
    }

    [Fact]
    public async Task TranslateAsync_BothFail_PropagatesFallbacksException()
    {
        var primary = new Mock<ILlmTranslationService>();
        primary.Setup(p => p.TranslateAsync(It.IsAny<IReadOnlyList<TranslationChunk>>(), It.IsAny<string>(), It.IsAny<GlossaryContext>(), It.IsAny<CancellationToken>(), It.IsAny<string>()))
            .ThrowsAsync(new LlmTranslationException("primary is down"));
        var fallback = new Mock<ILlmTranslationService>();
        fallback.Setup(f => f.TranslateAsync(It.IsAny<IReadOnlyList<TranslationChunk>>(), It.IsAny<string>(), It.IsAny<GlossaryContext>(), It.IsAny<CancellationToken>(), It.IsAny<string>()))
            .ThrowsAsync(new LlmTranslationException("fallback is also down"));
        var sut = new FallbackLlmTranslationService(primary.Object, fallback.Object);

        var act = () => sut.TranslateAsync([Chunk("c1")], "de", GlossaryContext.Empty, CancellationToken.None);

        await act.Should().ThrowAsync<LlmTranslationException>().WithMessage("*fallback is also down*");
    }

    [Fact]
    public void ProviderName_AlwaysReportsPrimarysName()
    {
        var primary = new Mock<ILlmTranslationService>();
        primary.SetupGet(p => p.ProviderName).Returns("gemini");
        var fallback = new Mock<ILlmTranslationService>();
        fallback.SetupGet(f => f.ProviderName).Returns("claude");
        var sut = new FallbackLlmTranslationService(primary.Object, fallback.Object);

        sut.ProviderName.Should().Be("gemini");
    }

    [Fact]
    public void Dispose_DisposesBothServices()
    {
        var primary = new Mock<ILlmTranslationService>();
        var fallback = new Mock<ILlmTranslationService>();
        var sut = new FallbackLlmTranslationService(primary.Object, fallback.Object);

        sut.Dispose();

        primary.Verify(p => p.Dispose(), Times.Once);
        fallback.Verify(f => f.Dispose(), Times.Once);
    }
}
