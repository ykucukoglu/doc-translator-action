using DocTranslator.Core.Models;
using DocTranslator.Core.Telemetry;
using DocTranslator.LLM.Batching;
using DocTranslator.LLM.Exceptions;
using DocTranslator.LLM.Prompting;
using DocTranslator.LLM.Retry;
using DocTranslator.LLM.Services;
using FluentAssertions;
using Microsoft.Extensions.AI;
using Moq;
using Polly;
using Polly.Retry;

namespace DocTranslator.LLM.Tests;

public class ChatClientLlmTranslationServiceTests
{
    // A no-op pipeline (no strategies registered) for tests that aren't exercising resilience -
    // it just invokes the callback directly, so retry timing never slows the test suite down.
    private static readonly ResiliencePipeline NoOpPipeline = new ResiliencePipelineBuilder().Build();

    private static TranslationChunk Chunk(string id, string text = "hello") =>
        new(id, text, ContentHash: "hash-" + id, BlockKind.Paragraph, "doc.md");

    private static Mock<IChatClient> MockClientReturning(string responseText)
    {
        var mock = new Mock<IChatClient>();
        mock.Setup(c => c.GetResponseAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatResponse(new ChatMessage(ChatRole.Assistant, responseText)));
        return mock;
    }

    private static ChatClientLlmTranslationService BuildService(
        IChatClient client,
        int maxRetries = 3,
        ResiliencePipeline? resiliencePipeline = null,
        ITokenUsageTracker? tokenUsageTracker = null,
        int maxParallelRequests = 4) =>
        new(
            client,
            "test-provider",
            new PromptBuilder(new DocTranslator.Core.Glossary.GlossaryService()),
            new ChunkBatcher(),
            new LlmResponseValidator(),
            resiliencePipeline ?? NoOpPipeline,
            tokenUsageTracker ?? new TokenUsageTracker(),
            maxParallelRequests,
            maxRetries);

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

    [Fact]
    public async Task TranslateAsync_TransientHttpFailure_IsRetriedByResiliencePipelineWithoutConsumingSemanticAttempts()
    {
        var chunks = new[] { Chunk("c1") };
        var goodJson = """{"translations":[{"chunkId":"c1","translatedText":"hallo"}]}""";

        var client = new Mock<IChatClient>();
        client.SetupSequence(c => c.GetResponseAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("service unavailable", null, System.Net.HttpStatusCode.ServiceUnavailable))
            .ReturnsAsync(new ChatResponse(new ChatMessage(ChatRole.Assistant, goodJson)));

        // A fast test-only pipeline (no real delay) so this test doesn't sleep for the production backoff.
        var fastPipeline = new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions { ShouldHandle = new PredicateBuilder().Handle<HttpRequestException>(), Delay = TimeSpan.Zero, MaxRetryAttempts = 2 })
            .Build();

        var sut = BuildService(client.Object, maxRetries: 1, resiliencePipeline: fastPipeline);

        var result = await sut.TranslateAsync(chunks, "de", GlossaryContext.Empty, CancellationToken.None);

        // maxRetries: 1 at the semantic layer proves this succeeded on the FIRST outer attempt -
        // the transient failure was absorbed entirely by the resilience pipeline underneath it.
        result.Should().ContainSingle().Which.TranslatedText.Should().Be("hallo");
    }

    [Fact]
    public async Task TranslateAsync_SuccessfulResponse_RecordsTokenUsage()
    {
        var chunks = new[] { Chunk("c1") };
        var json = """{"translations":[{"chunkId":"c1","translatedText":"hallo"}]}""";

        var client = new Mock<IChatClient>();
        client.Setup(c => c.GetResponseAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatResponse(new ChatMessage(ChatRole.Assistant, json))
            {
                Usage = new UsageDetails { InputTokenCount = 100, OutputTokenCount = 40, TotalTokenCount = 140 },
            });

        var tracker = new TokenUsageTracker();
        var sut = BuildService(client.Object, tokenUsageTracker: tracker);

        await sut.TranslateAsync(chunks, "de", GlossaryContext.Empty, CancellationToken.None);

        tracker.TotalPromptTokens.Should().Be(100);
        tracker.TotalCompletionTokens.Should().Be(40);
        tracker.TotalTokens.Should().Be(140);
    }

    [Fact]
    public async Task RepairChunkAsync_ProviderReturnsCorrectedTranslation_ReturnsIt()
    {
        var chunk = Chunk("c1");
        var repairedJson = """{"translations":[{"chunkId":"c1","translatedText":"hallo ⟦CODE0⟧"}]}""";
        var client = MockClientReturning(repairedJson);
        var sut = BuildService(client.Object);

        var result = await sut.RepairChunkAsync(chunk, "hallo (dropped the marker)", ["the placeholder at index 0"], "de", GlossaryContext.Empty, CancellationToken.None);

        result.ChunkId.Should().Be("c1");
        result.TranslatedText.Should().Be("hallo ⟦CODE0⟧");
    }

    [Fact]
    public async Task RepairChunkAsync_PromptIncludesPreviousAttemptAndMissingMarkers()
    {
        var chunk = Chunk("c1");
        var client = new Mock<IChatClient>();
        List<ChatMessage>? capturedMessages = null;
        client.Setup(c => c.GetResponseAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions>(), It.IsAny<CancellationToken>()))
            .Callback((IEnumerable<ChatMessage> messages, ChatOptions? _, CancellationToken _) => capturedMessages = messages.ToList())
            .ReturnsAsync(new ChatResponse(new ChatMessage(ChatRole.Assistant, """{"translations":[{"chunkId":"c1","translatedText":"fixed"}]}""")));

        var sut = BuildService(client.Object);

        await sut.RepairChunkAsync(chunk, "the bad attempt", ["the placeholder at index 0"], "de", GlossaryContext.Empty, CancellationToken.None);

        capturedMessages.Should().NotBeNull();
        capturedMessages!.Should().Contain(m => m.Text.Contains("the bad attempt"));
        capturedMessages.Should().Contain(m => m.Text.Contains("the placeholder at index 0"));
    }

    [Fact]
    public async Task RepairChunkAsync_ProviderThrows_FallsBackToPreviousAttemptWithoutThrowing()
    {
        var chunk = Chunk("c1");
        var client = new Mock<IChatClient>();
        client.Setup(c => c.GetResponseAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("provider unavailable"));

        var sut = BuildService(client.Object);

        var result = await sut.RepairChunkAsync(chunk, "the previous attempt", ["the placeholder at index 0"], "de", GlossaryContext.Empty, CancellationToken.None);

        result.ChunkId.Should().Be("c1");
        result.TranslatedText.Should().Be("the previous attempt");
    }

    [Fact]
    public async Task TranslateAsync_MultipleBatches_NeverExceedsMaxParallelRequestsConcurrently()
    {
        var bigText = new string('x', 20_000); // forces multiple batches, same trick as the earlier splitting test
        var chunks = Enumerable.Range(0, 6).Select(i => Chunk($"c{i}", bigText)).ToArray();

        var concurrent = 0;
        var maxObservedConcurrency = 0;
        var gate = new object();

        var client = new Mock<IChatClient>();
        client.Setup(c => c.GetResponseAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions>(), It.IsAny<CancellationToken>()))
            .Returns(async (IEnumerable<ChatMessage> messages, ChatOptions? _, CancellationToken _) =>
            {
                lock (gate)
                {
                    concurrent++;
                    maxObservedConcurrency = Math.Max(maxObservedConcurrency, concurrent);
                }

                await Task.Delay(30, CancellationToken.None); // hold the "slot" long enough for other batches to pile up behind the semaphore

                lock (gate)
                {
                    concurrent--;
                }

                var userMessage = messages.Last().Text;
                var ids = chunks.Select(c => c.ChunkId).Where(id => userMessage.Contains($"[{id}]"));
                var translations = string.Join(',', ids.Select(id => $$"""{"chunkId":"{{id}}","translatedText":"x"}"""));
                return new ChatResponse(new ChatMessage(ChatRole.Assistant, $$"""{"translations":[{{translations}}]}"""));
            });

        var sut = BuildService(client.Object, maxParallelRequests: 2);

        await sut.TranslateAsync(chunks, "de", GlossaryContext.Empty, CancellationToken.None);

        maxObservedConcurrency.Should().BeLessThanOrEqualTo(2);
    }
}
