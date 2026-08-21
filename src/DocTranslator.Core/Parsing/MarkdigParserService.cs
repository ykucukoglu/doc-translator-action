using System.Security.Cryptography;
using System.Text;
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
    /// breaks, and YAML frontmatter are skipped entirely and never touched.
    /// </summary>
    DocumentTranslationContext ParseAndExtractChunks(string sourceFilePath, string markdownText);
}

public sealed class MarkdigParserService : IMarkdigParserService
{
    private readonly InlineChunkExtractor _extractor = new();

    public DocumentTranslationContext ParseAndExtractChunks(string sourceFilePath, string markdownText)
    {
        var document = Markdown.Parse(markdownText, MarkdigConfiguration.Pipeline);

        // Markdig's normalizing renderer doesn't round-trip a YamlFrontMatterBlock's `---`
        // delimiters correctly (it's an HtmlBlock subclass, rendered as raw HTML lines with no
        // fence re-added) - captured verbatim here and removed from the tree so RenderDocument
        // never touches it; AstReconstructor splices this text back onto the output directly.
        string? frontmatterRawText = null;
        if (document.Count > 0 && document[0] is YamlFrontMatterBlock frontmatter)
        {
            frontmatterRawText = markdownText.Substring(frontmatter.Span.Start, frontmatter.Span.Length);
            document.RemoveAt(0);
        }

        var chunks = new List<TranslationChunk>();
        var reconstructionMap = new Dictionary<string, BlockReconstructionContext>();

        WalkBlocks(document, sourceFilePath, chunks, reconstructionMap);

        return new DocumentTranslationContext
        {
            SourceFilePath = sourceFilePath,
            MarkdownDocument = document,
            Chunks = chunks,
            ReconstructionMap = reconstructionMap,
            FrontmatterRawText = frontmatterRawText,
        };
    }

    private void WalkBlocks(
        ContainerBlock container,
        string sourceFilePath,
        List<TranslationChunk> chunks,
        Dictionary<string, BlockReconstructionContext> reconstructionMap)
    {
        foreach (var block in container)
        {
            switch (block)
            {
                // Never touched: code/HTML/structural blocks carry no natural language. (A leading
                // YamlFrontMatterBlock, if present, was already removed from the tree entirely -
                // see ParseAndExtractChunks - so it's never seen here.)
                case CodeBlock:
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
                    WalkBlocks(nestedContainer, sourceFilePath, chunks, reconstructionMap);
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

    internal static string ComputeHash(string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexStringLower(hash);
    }
}
