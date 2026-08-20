namespace DocTranslator.Core.Parsing;

/// <summary>
/// A node in the small tree <see cref="ReconstructionScanner"/> parses translated text into:
/// a mix of plain text runs, references to atomic placeholders, and paired synthetic tags.
/// </summary>
public abstract record EncodedNode;

/// <summary>A run of plain, translated natural-language text.</summary>
public sealed record TextRunNode(string Text) : EncodedNode;

/// <summary>A reference to an atomic placeholder (e.g. <c>⟦CODE0⟧</c>) - never touched, only relocated.</summary>
public sealed record PlaceholderRefNode(int Index) : EncodedNode;

/// <summary>A paired synthetic tag (<c>&lt;em0&gt;...&lt;/em0&gt;</c>) wrapping recursively-parsed children.</summary>
public sealed record TaggedSpanNode(string TagName, int Index, IReadOnlyList<EncodedNode> Children) : EncodedNode;
