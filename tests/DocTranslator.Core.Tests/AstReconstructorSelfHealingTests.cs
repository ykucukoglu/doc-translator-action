using DocTranslator.Core.Models;
using DocTranslator.Core.Parsing;
using DocTranslator.Core.Reconstruction;
using FluentAssertions;

namespace DocTranslator.Core.Tests;

/// <summary>
/// Covers the self-healing repair loop: a translation that dropped a required placeholder/tag
/// marker is retried (via an injected <see cref="ChunkRepairCallback"/>) up to two times before
/// falling back to the chunk's original source text, so one bad LLM response degrades a single
/// paragraph rather than corrupting the document or aborting the whole file.
/// </summary>
public class AstReconstructorSelfHealingTests
{
    private readonly MarkdigParserService _parser = new();
    private readonly AstReconstructor _reconstructor = new();

    private (DocumentTranslationContext Context, TranslationChunk Chunk) ParseSingleChunkFixture(string markdown)
    {
        var context = _parser.ParseAndExtractChunks("doc.md", markdown);
        return (context, context.Chunks.Single());
    }

    [Fact]
    public async Task ReconstructAsync_TranslationDropsCodePlaceholder_RepairSucceedsOnFirstAttempt()
    {
        var (context, chunk) = ParseSingleChunkFixture("Run `dotnet build` now.\n");
        var badTranslation = new TranslatedChunk(chunk.ChunkId, "Führen Sie es jetzt aus."); // dropped ⟦CODE0⟧ entirely
        var repairCallCount = 0;

        Task<TranslatedChunk> Repair(TranslationChunk original, string previous, IReadOnlyList<string> missing, CancellationToken ct)
        {
            repairCallCount++;
            missing.Should().ContainMatch("*placeholder*");
            return Task.FromResult(new TranslatedChunk(original.ChunkId, $"Führen Sie {original.SourceText} jetzt aus."));
        }

        var outcome = await _reconstructor.ReconstructAsync(context, [badTranslation], Repair, provenance: null, CancellationToken.None);

        repairCallCount.Should().Be(1);
        outcome.RepairedChunkIds.Should().ContainSingle().Which.Should().Be(chunk.ChunkId);
        outcome.UnrecoverableChunkIds.Should().BeEmpty();
        outcome.Markdown.Should().Contain("`dotnet build`");
    }

    [Fact]
    public async Task ReconstructAsync_RepairKeepsFailing_FallsBackToOriginalSourceTextAfterMaxAttempts()
    {
        var (context, chunk) = ParseSingleChunkFixture("Run `dotnet build` now.\n");
        var badTranslation = new TranslatedChunk(chunk.ChunkId, "still missing the marker");
        var repairCallCount = 0;

        Task<TranslatedChunk> AlwaysFailingRepair(TranslationChunk original, string previous, IReadOnlyList<string> missing, CancellationToken ct)
        {
            repairCallCount++;
            return Task.FromResult(new TranslatedChunk(original.ChunkId, "still broken, no marker here either"));
        }

        var outcome = await _reconstructor.ReconstructAsync(context, [badTranslation], AlwaysFailingRepair, provenance: null, CancellationToken.None);

        repairCallCount.Should().Be(2); // max 2 repair attempts
        outcome.UnrecoverableChunkIds.Should().ContainSingle().Which.Should().Be(chunk.ChunkId);
        outcome.RepairedChunkIds.Should().BeEmpty();
        outcome.Markdown.Should().Contain("`dotnet build`"); // fell back to the original, untranslated source text
    }

    [Fact]
    public async Task ReconstructAsync_NoRepairCallbackSupplied_FallsBackImmediatelyWithoutThrowing()
    {
        var (context, chunk) = ParseSingleChunkFixture("Run `dotnet build` now.\n");
        var badTranslation = new TranslatedChunk(chunk.ChunkId, "missing the marker, and no repair callback given");

        var outcome = await _reconstructor.ReconstructAsync(context, [badTranslation], repairChunkAsync: null, provenance: null, CancellationToken.None);

        outcome.UnrecoverableChunkIds.Should().ContainSingle().Which.Should().Be(chunk.ChunkId);
        outcome.Markdown.Should().Contain("`dotnet build`");
    }

    [Fact]
    public async Task ReconstructAsync_ValidTranslationOnFirstAttempt_NeverInvokesRepairCallback()
    {
        var (context, chunk) = ParseSingleChunkFixture("Run `dotnet build` now.\n");
        var goodTranslation = new TranslatedChunk(chunk.ChunkId, chunk.SourceText.Replace("Run", "Führen Sie").Replace("now", "jetzt aus"));
        var repairCallCount = 0;

        Task<TranslatedChunk> Repair(TranslationChunk original, string previous, IReadOnlyList<string> missing, CancellationToken ct)
        {
            repairCallCount++;
            return Task.FromResult(new TranslatedChunk(original.ChunkId, previous));
        }

        var outcome = await _reconstructor.ReconstructAsync(context, [goodTranslation], Repair, provenance: null, CancellationToken.None);

        repairCallCount.Should().Be(0);
        outcome.RepairedChunkIds.Should().BeEmpty();
        outcome.UnrecoverableChunkIds.Should().BeEmpty();
    }

    [Fact]
    public async Task ReconstructAsync_TranslationDropsEmphasisTag_IsDetectedAsMissing()
    {
        var (context, chunk) = ParseSingleChunkFixture("This is **bold** text.\n");
        var badTranslation = new TranslatedChunk(chunk.ChunkId, "Das ist fett Text."); // dropped <strong0>...</strong0>

        var outcome = await _reconstructor.ReconstructAsync(context, [badTranslation], repairChunkAsync: null, provenance: null, CancellationToken.None);

        outcome.UnrecoverableChunkIds.Should().ContainSingle();
        outcome.Markdown.Should().Contain("**bold**"); // fell back to the original, which still has the emphasis
    }

    [Fact]
    public async Task ReconstructAsync_SecondRepairAttemptSucceeds_ReportsAsRepairedNotUnrecoverable()
    {
        var (context, chunk) = ParseSingleChunkFixture("Run `dotnet build` now.\n");
        var badTranslation = new TranslatedChunk(chunk.ChunkId, "no marker on attempt zero");
        var attempt = 0;

        Task<TranslatedChunk> Repair(TranslationChunk original, string previous, IReadOnlyList<string> missing, CancellationToken ct)
        {
            attempt++;
            var text = attempt == 1
                ? "still broken on first repair attempt"
                : $"fixed on second repair attempt: {original.SourceText}";
            return Task.FromResult(new TranslatedChunk(original.ChunkId, text));
        }

        var outcome = await _reconstructor.ReconstructAsync(context, [badTranslation], Repair, provenance: null, CancellationToken.None);

        attempt.Should().Be(2);
        outcome.RepairedChunkIds.Should().ContainSingle();
        outcome.UnrecoverableChunkIds.Should().BeEmpty();
    }
}
