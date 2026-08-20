using DocTranslator.Core.Extensions;
using DocTranslator.Core.Models;
using DocTranslator.Core.Parsing;
using Markdig.Renderers.Normalize;
using Markdig.Syntax.Inlines;

namespace DocTranslator.Core.Reconstruction;

public interface IAstReconstructor
{
    /// <summary>
    /// Validates that every placeholder/tag marker present in each chunk's original source text
    /// also survived in its translation - self-healing any that didn't via
    /// <paramref name="repairChunkAsync"/> (max 2 repair attempts per chunk) - then splices each
    /// chunk back into the exact AST position it was extracted from and renders the whole
    /// document to Markdown. If <paramref name="provenance"/> is supplied, its HTML-comment
    /// header is prepended to the output.
    /// </summary>
    Task<ReconstructionOutcome> ReconstructAsync(
        DocumentTranslationContext context,
        IReadOnlyList<TranslatedChunk> translatedChunks,
        ChunkRepairCallback? repairChunkAsync,
        TranslationProvenance? provenance,
        CancellationToken cancellationToken);
}

public sealed class AstReconstructor : IAstReconstructor
{
    private const int MaxRepairAttempts = 2;

    private readonly ReconstructionScanner _scanner = new();

    public async Task<ReconstructionOutcome> ReconstructAsync(
        DocumentTranslationContext context,
        IReadOnlyList<TranslatedChunk> translatedChunks,
        ChunkRepairCallback? repairChunkAsync,
        TranslationProvenance? provenance,
        CancellationToken cancellationToken)
    {
        var chunksById = context.Chunks.ToDictionary(c => c.ChunkId);
        var repairedIds = new List<string>();
        var unrecoverableIds = new List<string>();

        foreach (var translated in translatedChunks)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!chunksById.TryGetValue(translated.ChunkId, out var originalChunk))
            {
                throw new InvalidOperationException(
                    $"Translated chunk '{translated.ChunkId}' does not correspond to any known source chunk in '{context.SourceFilePath}'.");
            }

            var validated = await EnsureMarkersSurvivedAsync(originalChunk, translated, repairChunkAsync, repairedIds, unrecoverableIds, cancellationToken)
                .ConfigureAwait(false);

            SpliceChunkDefensively(context, originalChunk, validated, unrecoverableIds);
        }

        var rendered = RenderDocument(context);
        var markdown = provenance is null
            ? rendered
            : provenance.ToHeaderComment() + Environment.NewLine + Environment.NewLine + rendered;

        return new ReconstructionOutcome(markdown, repairedIds, unrecoverableIds);
    }

    /// <summary>
    /// The self-healing loop: checks that every marker (placeholder or paired tag) present in the
    /// original source text also appears in the translation. On a miss, asks
    /// <paramref name="repairChunkAsync"/> to re-translate just this chunk with a repair prompt,
    /// up to <see cref="MaxRepairAttempts"/> times. If it's still missing markers after that (or
    /// no repair delegate was supplied at all), falls back to the chunk's own original source text
    /// - guaranteed parseable, since it's exactly the format this class's own scanner produces -
    /// so one bad LLM response degrades to "this paragraph stayed in the source language" instead
    /// of corrupting the whole document or aborting the whole file.
    /// </summary>
    private async Task<TranslatedChunk> EnsureMarkersSurvivedAsync(
        TranslationChunk originalChunk,
        TranslatedChunk translated,
        ChunkRepairCallback? repairChunkAsync,
        List<string> repairedIds,
        List<string> unrecoverableIds,
        CancellationToken cancellationToken)
    {
        var current = translated;

        for (var attempt = 0; attempt <= MaxRepairAttempts; attempt++)
        {
            var missing = FindMissingMarkers(originalChunk.SourceText, current.TranslatedText);
            if (missing.Count == 0)
            {
                if (attempt > 0)
                {
                    repairedIds.Add(originalChunk.ChunkId);
                }

                return current;
            }

            if (attempt == MaxRepairAttempts || repairChunkAsync is null)
            {
                unrecoverableIds.Add(originalChunk.ChunkId);
                return new TranslatedChunk(originalChunk.ChunkId, originalChunk.SourceText);
            }

            current = await repairChunkAsync(originalChunk, current.TranslatedText, missing, cancellationToken).ConfigureAwait(false);
        }

        unrecoverableIds.Add(originalChunk.ChunkId);
        return new TranslatedChunk(originalChunk.ChunkId, originalChunk.SourceText);
    }

    /// <summary>
    /// Marker-set comparison, not textual diffing: parses both texts with the same scanner used
    /// for reconstruction and compares the (kind, index) pairs found in each. An unparseable
    /// translation (mismatched/unclosed tags) is treated as "nothing survived", which is exactly
    /// as valid a "needs repair" signal as an individually-dropped marker.
    /// </summary>
    private List<string> FindMissingMarkers(string sourceText, string translatedText)
    {
        var sourceMarkers = TryExtractMarkers(sourceText);
        var translatedMarkers = TryExtractMarkers(translatedText);

        return sourceMarkers
            .Where(marker => !translatedMarkers.Contains(marker))
            .Select(DescribeMarker)
            .ToList();
    }

    private HashSet<(string Kind, int Index)> TryExtractMarkers(string text)
    {
        try
        {
            var markers = new HashSet<(string Kind, int Index)>();
            CollectMarkers(_scanner.Parse(text), markers);
            return markers;
        }
        catch (ReconstructionParseException)
        {
            return [];
        }
    }

    private static void CollectMarkers(IReadOnlyList<EncodedNode> nodes, HashSet<(string Kind, int Index)> markers)
    {
        foreach (var node in nodes)
        {
            switch (node)
            {
                case PlaceholderRefNode placeholder:
                    markers.Add(("placeholder", placeholder.Index));
                    break;

                case TaggedSpanNode tag:
                    markers.Add((tag.TagName, tag.Index));
                    CollectMarkers(tag.Children, markers);
                    break;
            }
        }
    }

    private static string DescribeMarker((string Kind, int Index) marker) =>
        marker.Kind == "placeholder"
            ? $"the placeholder at index {marker.Index}"
            : $"the <{marker.Kind}{marker.Index}>...</{marker.Kind}{marker.Index}> tag pair";

    /// <summary>
    /// Splices a chunk whose markers are already verified to have survived. Wrapped in a
    /// defensive fallback: markers surviving doesn't strictly guarantee valid nesting, so on the
    /// (rare) chance the scanner still rejects it, fall back to the chunk's original source text
    /// exactly as the exhausted-repair path does above, rather than letting a parse exception
    /// escape and abort the whole file.
    /// </summary>
    private void SpliceChunkDefensively(
        DocumentTranslationContext context,
        TranslationChunk originalChunk,
        TranslatedChunk validated,
        List<string> unrecoverableIds)
    {
        try
        {
            SpliceChunk(context, validated);
        }
        catch (ReconstructionParseException)
        {
            if (!unrecoverableIds.Contains(originalChunk.ChunkId))
            {
                unrecoverableIds.Add(originalChunk.ChunkId);
            }

            SpliceChunk(context, new TranslatedChunk(originalChunk.ChunkId, originalChunk.SourceText));
        }
    }

    private void SpliceChunk(DocumentTranslationContext context, TranslatedChunk translated)
    {
        if (!context.ReconstructionMap.TryGetValue(translated.ChunkId, out var reconstructionContext))
        {
            throw new InvalidOperationException(
                $"No reconstruction context found for chunk '{translated.ChunkId}' in '{context.SourceFilePath}'.");
        }

        var nodes = _scanner.Parse(translated.TranslatedText);
        var builtInlines = BuildInlines(nodes, reconstructionContext, translated.ChunkId);

        var targetInline = reconstructionContext.TargetBlock.Inline
            ?? throw new InvalidOperationException(
                $"Target block for chunk '{translated.ChunkId}' has no inline container.");

        targetInline.Clear();
        foreach (var inline in builtInlines)
        {
            targetInline.AppendChild(inline);
        }
    }

    private static List<Inline> BuildInlines(IReadOnlyList<EncodedNode> nodes, BlockReconstructionContext context, string chunkId)
    {
        var result = new List<Inline>(nodes.Count);
        foreach (var node in nodes)
        {
            result.Add(BuildInline(node, context, chunkId));
        }

        return result;
    }

    private static Inline BuildInline(EncodedNode node, BlockReconstructionContext context, string chunkId)
    {
        switch (node)
        {
            case TextRunNode text:
                return new LiteralInline(text.Text);

            case PlaceholderRefNode placeholder:
                if (!context.AtomicPlaceholders.TryGetValue(placeholder.Index, out var originalInline))
                {
                    throw new ReconstructionParseException(
                        $"Unknown placeholder index {placeholder.Index} in translated text for chunk '{chunkId}'.");
                }

                return originalInline;

            case TaggedSpanNode { TagName: "link" } linkSpan:
                {
                    if (!context.LinkTags.TryGetValue(linkSpan.Index, out var linkMeta))
                    {
                        throw new ReconstructionParseException(
                            $"Unknown link tag index {linkSpan.Index} in translated text for chunk '{chunkId}'.");
                    }

                    var link = new LinkInline
                    {
                        Url = linkMeta.Url,
                        Title = linkMeta.Title,
                        IsImage = linkMeta.IsImage,
                    };

                    foreach (var child in BuildInlines(linkSpan.Children, context, chunkId))
                    {
                        link.AppendChild(child);
                    }

                    return link;
                }

            case TaggedSpanNode emphasisSpan: // "em" or "strong"
                {
                    if (!context.EmphasisTags.TryGetValue(emphasisSpan.Index, out var emphasisMeta))
                    {
                        throw new ReconstructionParseException(
                            $"Unknown emphasis tag index {emphasisSpan.Index} in translated text for chunk '{chunkId}'.");
                    }

                    var emphasis = new EmphasisInline
                    {
                        DelimiterChar = emphasisMeta.DelimiterChar,
                        DelimiterCount = emphasisMeta.DelimiterCount,
                    };

                    foreach (var child in BuildInlines(emphasisSpan.Children, context, chunkId))
                    {
                        emphasis.AppendChild(child);
                    }

                    return emphasis;
                }

            default:
                throw new InvalidOperationException($"Unhandled encoded node type '{node.GetType().Name}'.");
        }
    }

    private static string RenderDocument(DocumentTranslationContext context)
    {
        using var writer = new StringWriter();
        var renderer = new NormalizeRenderer(writer);
        MarkdigConfiguration.Pipeline.Setup(renderer);
        renderer.Render(context.MarkdownDocument);
        writer.Flush();
        return writer.ToString();
    }
}
