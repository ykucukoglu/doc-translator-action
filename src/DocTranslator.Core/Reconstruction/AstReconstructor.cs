using DocTranslator.Core.Extensions;
using DocTranslator.Core.Models;
using DocTranslator.Core.Parsing;
using Markdig.Renderers.Normalize;
using Markdig.Syntax.Inlines;

namespace DocTranslator.Core.Reconstruction;

public interface IAstReconstructor
{
    /// <summary>
    /// Splices each translated chunk back into the exact AST position it was extracted from and
    /// renders the whole document to Markdown. If <paramref name="provenance"/> is supplied, its
    /// HTML-comment header is prepended to the output (see the drift-marker feature in the plan).
    /// </summary>
    string Reconstruct(
        DocumentTranslationContext context,
        IReadOnlyList<TranslatedChunk> translatedChunks,
        TranslationProvenance? provenance = null);
}

public sealed class AstReconstructor : IAstReconstructor
{
    private readonly ReconstructionScanner _scanner = new();

    public string Reconstruct(
        DocumentTranslationContext context,
        IReadOnlyList<TranslatedChunk> translatedChunks,
        TranslationProvenance? provenance = null)
    {
        foreach (var translated in translatedChunks)
        {
            SpliceChunk(context, translated);
        }

        var rendered = RenderDocument(context);

        return provenance is null
            ? rendered
            : provenance.ToHeaderComment() + Environment.NewLine + Environment.NewLine + rendered;
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
