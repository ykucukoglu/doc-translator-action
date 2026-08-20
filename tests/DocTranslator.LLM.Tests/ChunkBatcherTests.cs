using DocTranslator.Core.Models;
using DocTranslator.LLM.Batching;
using FluentAssertions;

namespace DocTranslator.LLM.Tests;

public class ChunkBatcherTests
{
    private static TranslationChunk Chunk(string id, string text) =>
        new(id, text, ContentHash: "h", BlockKind.Paragraph, "doc.md");

    [Fact]
    public void Batch_SmallChunks_FitInOneBatch()
    {
        var sut = new ChunkBatcher(maxTokensPerBatch: 1000);
        var chunks = new[] { Chunk("c1", "short"), Chunk("c2", "also short") };

        var batches = sut.Batch(chunks);

        batches.Should().ContainSingle();
        batches[0].Should().HaveCount(2);
    }

    [Fact]
    public void Batch_ChunksExceedingBudget_SplitAcrossMultipleBatches()
    {
        var sut = new ChunkBatcher(maxTokensPerBatch: 10); // ~40 chars per batch
        var chunks = new[]
        {
            Chunk("c1", new string('a', 100)),
            Chunk("c2", new string('b', 100)),
        };

        var batches = sut.Batch(chunks);

        batches.Should().HaveCountGreaterThan(1);
    }

    [Fact]
    public void Batch_NeverSplitsASingleChunkAcrossBatches()
    {
        var sut = new ChunkBatcher(maxTokensPerBatch: 10);
        var chunks = new[] { Chunk("c1", new string('a', 500)) };

        var batches = sut.Batch(chunks);

        batches.Should().ContainSingle();
        batches[0].Should().ContainSingle().Which.ChunkId.Should().Be("c1");
    }

    [Fact]
    public void Batch_EmptyInput_ReturnsNoBatches()
    {
        var sut = new ChunkBatcher();

        var batches = sut.Batch([]);

        batches.Should().BeEmpty();
    }

    [Fact]
    public void Batch_EveryChunkAppearsExactlyOnceAcrossAllBatches()
    {
        var sut = new ChunkBatcher(maxTokensPerBatch: 15);
        var chunks = Enumerable.Range(0, 10).Select(i => Chunk($"c{i}", new string('a', 20))).ToArray();

        var batches = sut.Batch(chunks);

        batches.SelectMany(b => b).Select(c => c.ChunkId).Should().BeEquivalentTo(chunks.Select(c => c.ChunkId));
    }
}
