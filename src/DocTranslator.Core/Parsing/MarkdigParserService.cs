using System.Security.Cryptography;
using System.Text;
using DocTranslator.Core.Diagrams;
using DocTranslator.Core.Extensions;
using DocTranslator.Core.Models;
using Markdig;
using Markdig.Extensions.Tables;
using Markdig.Extensions.Yaml;
using Markdig.Syntax;

namespace DocTranslator.Core.Parsing;

public interface IMarkdigParserService
{
    /// <summary>
    /// Parses a Markdown document, walks its AST, and extracts one <see cref="TranslationChunk"/>
    /// per translatable leaf block. Code blocks, fenced code blocks, raw HTML blocks, thematic
    /// breaks, and YAML/TOML frontmatter are skipped entirely and never touched - except a
    /// ```mermaid block's node/edge/subgraph labels when <paramref name="translateMermaidDiagrams"/>
    /// is on (see <see cref="MermaidLabelExtractor"/>), and an allowlisted frontmatter field's value
    /// (e.g. <c>title</c>) when <paramref name="translateFrontmatterFields"/> is on (see
    /// <see cref="FrontmatterFieldExtractor"/>).
    /// </summary>
    DocumentTranslationContext ParseAndExtractChunks(
        string sourceFilePath, string markdownText, bool translateMermaidDiagrams = false, bool translateFrontmatterFields = false);
}

public sealed class MarkdigParserService : IMarkdigParserService
{
    private readonly InlineChunkExtractor _extractor = new();

    public DocumentTranslationContext ParseAndExtractChunks(
        string sourceFilePath, string markdownText, bool translateMermaidDiagrams = false, bool translateFrontmatterFields = false)
    {
        // Markdig's UseYamlFrontMatter() only recognizes `---`-delimited YAML - Hugo's `+++`-delimited
        // TOML frontmatter has no native Markdig support at all, so it's stripped here, before
        // Markdig ever parses the text, or its key/value lines would be parsed as an ordinary
        // paragraph and sent to the LLM as translatable text (the same failure mode YAML frontmatter
        // had before UseYamlFrontMatter() was enabled).
        var (bodyText, tomlFrontmatterRawText) = ExtractTomlFrontmatterIfPresent(markdownText);
        var document = Markdown.Parse(bodyText, MarkdigConfiguration.Pipeline);

        // Markdig's normalizing renderer doesn't round-trip a YamlFrontMatterBlock's `---`
        // delimiters correctly (it's an HtmlBlock subclass, rendered as raw HTML lines with no
        // fence re-added) - captured verbatim here and removed from the tree so RenderDocument
        // never touches it; AstReconstructor splices this text back onto the output directly.
        // Mutually exclusive with TOML frontmatter - a file has one frontmatter format, not both.
        var frontmatterRawText = tomlFrontmatterRawText;
        if (frontmatterRawText is null && document.Count > 0 && document[0] is YamlFrontMatterBlock frontmatter)
        {
            frontmatterRawText = bodyText.Substring(frontmatter.Span.Start, frontmatter.Span.Length);
            document.RemoveAt(0);
        }

        var chunks = new List<TranslationChunk>();
        var reconstructionMap = new Dictionary<string, BlockReconstructionContext>();
        var mermaidBlocks = new List<MermaidBlockContext>();
        var codeBlockCount = 0;

        WalkBlocks(document, sourceFilePath, chunks, reconstructionMap, mermaidBlocks, translateMermaidDiagrams, ref codeBlockCount);

        var frontmatterFields = translateFrontmatterFields && frontmatterRawText is not null
            ? ExtractFrontmatterFields(frontmatterRawText, sourceFilePath, chunks)
            : [];

        return new DocumentTranslationContext
        {
            SourceFilePath = sourceFilePath,
            MarkdownDocument = document,
            Chunks = chunks,
            ReconstructionMap = reconstructionMap,
            FrontmatterRawText = frontmatterRawText,
            CodeBlockCount = codeBlockCount,
            MermaidBlocks = mermaidBlocks,
            FrontmatterFields = frontmatterFields,
        };
    }

    /// <summary>
    /// Mirrors <see cref="TryExtractMermaidLabels"/>'s pattern, one level simpler: there's only ever
    /// one frontmatter block per document, so this returns the chunk id/span pairs directly instead
    /// of building a whole block-context list.
    /// </summary>
    private static List<(string ChunkId, ExtractedTextSpan Span)> ExtractFrontmatterFields(
        string frontmatterRawText, string sourceFilePath, List<TranslationChunk> chunks)
    {
        var fields = new List<(string ChunkId, ExtractedTextSpan Span)>();
        foreach (var span in FrontmatterFieldExtractor.ExtractTranslatableFields(frontmatterRawText))
        {
            var chunkId = Guid.NewGuid().ToString("N");
            chunks.Add(new TranslationChunk(
                ChunkId: chunkId,
                SourceText: span.Text,
                ContentHash: ComputeHash(span.Text),
                BlockKind: BlockKind.FrontmatterField,
                SourceFilePath: sourceFilePath));
            fields.Add((chunkId, span));
        }

        return fields;
    }

    private void WalkBlocks(
        ContainerBlock container,
        string sourceFilePath,
        List<TranslationChunk> chunks,
        Dictionary<string, BlockReconstructionContext> reconstructionMap,
        List<MermaidBlockContext> mermaidBlocks,
        bool translateMermaidDiagrams,
        ref int codeBlockCount)
    {
        foreach (var block in container)
        {
            switch (block)
            {
                // Never touched: code/HTML/structural blocks carry no natural language. (A leading
                // YamlFrontMatterBlock, if present, was already removed from the tree entirely -
                // see ParseAndExtractChunks - so it's never seen here.)
                case FencedCodeBlock fenced when translateMermaidDiagrams && string.Equals(fenced.Info, "mermaid", StringComparison.OrdinalIgnoreCase):
                    codeBlockCount++;
                    TryExtractMermaidLabels(fenced, sourceFilePath, chunks, mermaidBlocks);
                    break;

                case CodeBlock:
                    codeBlockCount++;
                    break;

                case HtmlBlock:
                case ThematicBreakBlock:
                    break;

                case HeadingBlock heading:
                    TryExtractLeafBlock(heading, BlockKind.Heading, sourceFilePath, chunks, reconstructionMap);
                    break;

                case ParagraphBlock paragraph:
                    {
                        var kind = container switch
                        {
                            TableCell => BlockKind.TableCell,
                            ListItemBlock => BlockKind.ListItem,
                            QuoteBlock => BlockKind.BlockQuote,
                            _ => BlockKind.Paragraph,
                        };
                        TryExtractLeafBlock(paragraph, kind, sourceFilePath, chunks, reconstructionMap);
                        break;
                    }

                case ContainerBlock nestedContainer:
                    // Document, QuoteBlock, ListBlock, ListItemBlock, Table, TableRow, TableCell -
                    // no chunk of their own, recurse into children.
                    WalkBlocks(nestedContainer, sourceFilePath, chunks, reconstructionMap, mermaidBlocks, translateMermaidDiagrams, ref codeBlockCount);
                    break;

                default:
                    // Other leaf blocks (e.g. LinkReferenceDefinitionGroup) carry no translatable
                    // inline content we handle - skip rather than guess.
                    break;
            }
        }
    }

    private void TryExtractLeafBlock(
        LeafBlock block,
        BlockKind kind,
        string sourceFilePath,
        List<TranslationChunk> chunks,
        Dictionary<string, BlockReconstructionContext> reconstructionMap)
    {
        if (block.Inline is null || block.Inline.FirstChild is null)
        {
            return;
        }

        var reconstructionContext = new BlockReconstructionContext { TargetBlock = block };
        var sourceText = _extractor.Encode(block.Inline, reconstructionContext);

        if (string.IsNullOrWhiteSpace(sourceText))
        {
            return;
        }

        var chunkId = Guid.NewGuid().ToString("N");
        var chunk = new TranslationChunk(
            ChunkId: chunkId,
            SourceText: sourceText,
            ContentHash: ComputeHash(sourceText),
            BlockKind: kind,
            SourceFilePath: sourceFilePath);

        chunks.Add(chunk);
        reconstructionMap[chunkId] = reconstructionContext;
    }

    /// <summary>
    /// Extracts translatable labels from one ```mermaid block via <see cref="MermaidLabelExtractor"/>
    /// and turns each into its own <see cref="TranslationChunk"/>, exactly like any other chunk from
    /// here on (same LLM call, same cache) - only reconstruction treats mermaid chunks differently,
    /// via the <see cref="MermaidBlockContext"/> this also builds (see
    /// <see cref="DocumentTranslationContext.MermaidBlocks"/>). A block with no recognized labels
    /// (unsupported diagram type, or a supported one with nothing this extractor's patterns match)
    /// contributes no chunks and no context entry - left exactly as untouched as it always was.
    /// </summary>
    private static void TryExtractMermaidLabels(
        FencedCodeBlock fenced,
        string sourceFilePath,
        List<TranslationChunk> chunks,
        List<MermaidBlockContext> mermaidBlocks)
    {
        var rawText = fenced.Lines.ToString();
        var labelSpans = MermaidLabelExtractor.ExtractLabels(rawText);
        if (labelSpans.Count == 0)
        {
            return;
        }

        var labels = new List<(string ChunkId, ExtractedTextSpan Span)>(labelSpans.Count);
        foreach (var span in labelSpans)
        {
            var chunkId = Guid.NewGuid().ToString("N");
            chunks.Add(new TranslationChunk(
                ChunkId: chunkId,
                SourceText: span.Text,
                ContentHash: ComputeHash(span.Text),
                BlockKind: BlockKind.MermaidLabel,
                SourceFilePath: sourceFilePath));
            labels.Add((chunkId, span));
        }

        mermaidBlocks.Add(new MermaidBlockContext { OriginalRawText = rawText, Labels = labels });
    }

    /// <summary>
    /// String-based, not a real TOML parse - only needs to recognize the fence shape (a line that
    /// is exactly <c>+++</c>, opening and closing), not validate what's between them. Splitting and
    /// rejoining on '\n' is lossless here: <see cref="string.Split(char[])"/> leaves a trailing '\r'
    /// on each line untouched, so re-joining the captured lines with '\n' reproduces the original
    /// bytes exactly, CRLF included.
    /// </summary>
    private static (string BodyText, string? FrontmatterRawText) ExtractTomlFrontmatterIfPresent(string markdownText)
    {
        var lines = markdownText.Split('\n');
        if (lines.Length == 0 || lines[0].TrimEnd('\r') != "+++")
        {
            return (markdownText, null);
        }

        var closingFenceIndex = Array.FindIndex(lines, 1, line => line.TrimEnd('\r') == "+++");
        if (closingFenceIndex < 0)
        {
            return (markdownText, null);
        }

        var frontmatterRawText = string.Join('\n', lines[..(closingFenceIndex + 1)]);
        var bodyText = string.Join('\n', lines[(closingFenceIndex + 1)..]);
        return (bodyText, frontmatterRawText);
    }

    internal static string ComputeHash(string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexStringLower(hash);
    }
}
