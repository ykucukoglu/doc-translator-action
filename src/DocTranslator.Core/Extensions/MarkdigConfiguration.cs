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
        .Build();
}
