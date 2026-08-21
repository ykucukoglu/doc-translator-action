using Markdig;

namespace DocTranslator.Core.Extensions;

/// <summary>
/// Single source of truth for the Markdig pipeline configuration. Every place in this codebase
/// that parses or re-renders Markdown must use <see cref="Pipeline"/>, so extension behavior
/// never drifts between the initial parse and the final render.
/// </summary>
public static class MarkdigConfiguration
{
    public static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UsePipeTables()
        .UseAutoLinks()
        .UseListExtras()
        .UseYamlFrontMatter()
        // Docusaurus/MyST-style ::: note ... ::: admonitions. Without this, the ::: fence lines
        // aren't recognized as anything special and the whole block (fences included) is parsed as
        // one ordinary paragraph, sending "note" / the fence markers to the LLM as translatable
        // text. CustomContainer is a ContainerBlock, so no chunk-extraction change is needed - the
        // existing ContainerBlock case already recurses into it and extracts its child paragraph
        // as a normal chunk, leaving the fences alone.
        .UseCustomContainers()
        .Build();
}
