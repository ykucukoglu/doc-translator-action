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
    /// breaks, and YAML/TOML frontmatter are skipped entirely and never touched.
    /// </summary>
    DocumentTranslationContext ParseAndExtractChunks(string sourceFilePath, string markdownText);
}

public sealed class MarkdigParserService : IMarkdigParserService
{
    private readonly InlineChunkExtractor _extractor = new();

    public DocumentTranslationContext ParseAndExtractChunks(string sourceFilePath, string markdownText)
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
