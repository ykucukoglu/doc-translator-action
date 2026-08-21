using Markdig.Extensions.CustomContainers;
using Markdig.Renderers.Normalize;

namespace DocTranslator.Core.Reconstruction;

/// <summary>
/// Markdig's CustomContainers extension (<c>::: note ... :::</c> admonitions) only registers a
/// renderer for <c>HtmlRenderer</c> - without this, <see cref="NormalizeRenderer"/> (the
/// Markdown-to-Markdown renderer this codebase re-renders every file with) has no renderer for
/// <see cref="CustomContainer"/> at all, and the fence lines vanish entirely on output even though
/// they were never sent to the LLM in the first place. Registered manually in
/// <see cref="AstReconstructor"/>.
/// </summary>
internal sealed class CustomContainerNormalizeRenderer : NormalizeObjectRenderer<CustomContainer>
{
    protected override void Write(NormalizeRenderer renderer, CustomContainer obj)
    {
        // TextRendererBase's char-repeat Write(char, int) overload is internal to Markdig, so the
        // fence is built as a plain string instead.
        renderer.Write(Fence(obj.FencedChar, obj.OpeningFencedCharCount));
        if (obj.Info is not null)
        {
            renderer.Write(obj.Info);
        }

        if (!string.IsNullOrEmpty(obj.Arguments))
        {
            renderer.Write(' ').Write(obj.Arguments);
        }

        renderer.WriteLine();
        renderer.WriteChildren(obj);
        renderer.EnsureLine();
        renderer.Write(Fence(obj.FencedChar, obj.ClosingFencedCharCount > 0 ? obj.ClosingFencedCharCount : obj.OpeningFencedCharCount));
        renderer.FinishBlock(true);
    }

    private static string Fence(char fencedChar, int count) => new(fencedChar, count);
}
