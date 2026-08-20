using DocTranslator.Core.Models;
using DocTranslator.LLM.Batching;
using DocTranslator.LLM.Exceptions;
using DocTranslator.LLM.Prompting;
using DocTranslator.LLM.Retry;
using DocTranslator.LLM.Services;
using FluentAssertions;
using Microsoft.Extensions.AI;
using Moq;

namespace DocTranslator.LLM.Tests;

public class ChatClientLlmTranslationServiceTests
{
    private static TranslationChunk Chunk(string id, string text = "hello") =>
        new(id, text, ContentHash: "hash-" + id, BlockKind.Paragraph, "doc.md");

    private static Mock<IChatClient> MockClientReturning(string responseText)
    {
        var mock = new Mock<IChatClient>();
        mock.Setup(c => c.GetResponseAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatResponse(new ChatMessage(ChatRole.Assistant, responseText)));
        return mock;
    }

    private static ChatClientLlmTranslationService BuildService(IChatClient client, int maxRetries = 3) =>
        new(client, "test-provider", new PromptBuilder(new DocTranslator.Core.Glossary.GlossaryService()), new ChunkBatcher(), new LlmResponseValidator(), maxRetries);

    [Fact]
    public async Task TranslateAsync_WellFormedResponse_ReturnsMatchingTranslatedChunks()
    {
        var chunks = new[] { Chunk("c1"), Chunk("c2") };
        var json = """{"translations":[{"chunkId":"c1","translatedText":"hallo"},{"chunkId":"c2","translatedText":"welt"}]}""";
        var client = MockClientReturning(json);
        var sut = BuildService(client.Object);

        var result = await sut.TranslateAsync(chunks, "de", GlossaryContext.Empty, CancellationToken.None);

        result.Should().BeEquivalentTo(
        [
            new TranslatedChunk("c1", "hallo"),
            new TranslatedChunk("c2", "welt"),
        ]);
    }

    [Fact]
    public async Task TranslateAsync_NoChunks_ReturnsEmptyWithoutCallingProvider()
    {
        var client = new Mock<IChatClient>();
        var sut = BuildService(client.Object);

        var result = await sut.TranslateAsync([], "de", GlossaryContext.Empty, CancellationToken.None);

        result.Should().BeEmpty();
        client.Verify(c => c.GetResponseAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task TranslateAsync_MalformedJson_RetriesThenThrows()
    {
        var chunks = new[] { Chunk("c1") };
        var client = MockClientReturning("not valid json at all");
        var sut = BuildService(client.Object, maxRetries: 2);

        var act = () => sut.TranslateAsync(chunks, "de", GlossaryContext.Empty, CancellationToken.None);

        await act.Should().ThrowAsync<LlmTranslationException>();
        client.Verify(c => c.GetResponseAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task TranslateAsync_MissingChunkIdInResponse_RetriesThenThrows()
    {
        var chunks = new[] { Chunk("c1"), Chunk("c2") };
        var json = """{"translations":[{"chunkId":"c1","translatedText":"hallo"}]}"""; // c2 missing
        var client = MockClientReturning(json);
        var sut = BuildService(client.Object, maxRetries: 2);

        var act = () => sut.TranslateAsync(chunks, "de", GlossaryContext.Empty, CancellationToken.None);

        await act.Should().ThrowAsync<LlmTranslationException>();
        client.Verify(c => c.GetResponseAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task TranslateAsync_ExtraChunkIdInResponse_RetriesThenThrows()
    {
        var chunks = new[] { Chunk("c1") };
        var json = """{"translations":[{"chunkId":"c1","translatedText":"hallo"},{"chunkId":"c-not-requested","translatedText":"???"}]}""";
        var client = MockClientReturning(json);
        var sut = BuildService(client.Object, maxRetries: 1);

        var act = () => sut.TranslateAsync(chunks, "de", GlossaryContext.Empty, CancellationToken.None);

        await act.Should().ThrowAsync<LlmTranslationException>();
    }

    [Fact]
    public async Task TranslateAsync_SucceedsOnSecondAttemptAfterMalformedFirst_ReturnsResult()
    {
        var chunks = new[] { Chunk("c1") };
        var goodJson = """{"translations":[{"chunkId":"c1","translatedText":"hallo"}]}""";

        var client = new Mock<IChatClient>();
        client.SetupSequence(c => c.GetResponseAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatResponse(new ChatMessage(ChatRole.Assistant, "garbage")))
            .ReturnsAsync(new ChatResponse(new ChatMessage(ChatRole.Assistant, goodJson)));

        var sut = BuildService(client.Object, maxRetries: 2);

        var result = await sut.TranslateAsync(chunks, "de", GlossaryContext.Empty, CancellationToken.None);

        result.Should().ContainSingle().Which.TranslatedText.Should().Be("hallo");
    }

    [Fact]
    public async Task TranslateAsync_ManyChunks_SplitsAcrossMultipleProviderCalls()
    {
        var bigText = new string('x', 20_000); // forces multiple batches under the default token budget
        var chunks = Enumerable.Range(0, 3).Select(i => Chunk($"c{i}", bigText)).ToArray();

        var client = new Mock<IChatClient>();
        client.Setup(c => c.GetResponseAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IEnumerable<ChatMessage> messages, ChatOptions? _, CancellationToken _) =>
            {
                // echo back a translation for whichever chunk ids were actually requested in this call
                var userMessage = messages.Last().Text;
                var ids = chunks.Select(c => c.ChunkId).Where(id => userMessage.Contains($"[{id}]"));
                var translations = string.Join(',', ids.Select(id => $$"""{"chunkId":"{{id}}","translatedText":"x"}"""));
                return new ChatResponse(new ChatMessage(ChatRole.Assistant, $$"""{"translations":[{{translations}}]}"""));
            });

        var sut = BuildService(client.Object);

        var result = await sut.TranslateAsync(chunks, "de", GlossaryContext.Empty, CancellationToken.None);

        result.Should().HaveCount(3);
        client.Verify(c => c.GetResponseAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions>(), It.IsAny<CancellationToken>()), Times.AtLeast(2));
    }
}
