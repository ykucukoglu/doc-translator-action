using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace DocTranslator.Core.Models;

/// <summary>
/// Stashed metadata for one paired synthetic emphasis tag (<c>&lt;em0&gt;</c> / <c>&lt;strong0&gt;</c>),
/// captured at extraction time so the exact original delimiter is restored on reconstruction.
/// </summary>
public readonly record struct EmphasisMetadata(char DelimiterChar, int DelimiterCount);

/// <summary>
/// Stashed metadata for one paired synthetic <c>&lt;link0&gt;</c> tag (covers both links and
/// images), captured at extraction time so the URL/title never has to pass through the LLM.
/// </summary>
public readonly record struct LinkMetadata(string? Url, string? Title, bool IsImage);

/// <summary>
/// Everything needed to splice a chunk's translated text back into the exact AST position it was
/// extracted from. Holds a direct reference to the live Markdig block plus the placeholder/tag
/// side tables built during extraction. Deliberately never serialized - this only ever exists
/// in-memory for the lifetime of a single parse-translate-reconstruct pass.
/// </summary>
public sealed class BlockReconstructionContext
{
    public required LeafBlock TargetBlock { get; init; }

    /// <summary>Atomic placeholder index -&gt; the original inline node (code, autolink, raw HTML, line break).</summary>
    public Dictionary<int, Inline> AtomicPlaceholders { get; } = new();

    /// <summary>Emphasis tag index -&gt; delimiter metadata.</summary>
    public Dictionary<int, EmphasisMetadata> EmphasisTags { get; } = new();

    /// <summary>Link tag index -&gt; link/image metadata.</summary>
    public Dictionary<int, LinkMetadata> LinkTags { get; } = new();
}
