using DocTranslator.Core.Models;

namespace DocTranslator.Core.Reconstruction;

/// <summary>
/// Re-translates one chunk whose previous translation dropped required placeholder/tag markers.
/// Implemented by the LLM layer (<c>ChatClientLlmTranslationService.RepairChunkAsync</c>) and
/// handed into <see cref="IAstReconstructor.ReconstructAsync"/> by the orchestration layer -
/// Core itself never calls out to an LLM, it only defines the extension point.
/// </summary>
/// <param name="originalChunk">The chunk as originally extracted (its <c>SourceText</c> is the ground truth for which markers must survive).</param>
/// <param name="previousTranslatedText">The invalid translation attempt, so the repair prompt can show the model what it got wrong.</param>
/// <param name="missingMarkers">Human-readable descriptions of the markers that were dropped.</param>
public delegate Task<TranslatedChunk> ChunkRepairCallback(
    TranslationChunk originalChunk,
    string previousTranslatedText,
    IReadOnlyList<string> missingMarkers,
    CancellationToken cancellationToken);
