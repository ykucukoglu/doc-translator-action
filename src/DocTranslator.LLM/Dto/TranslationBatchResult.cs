namespace DocTranslator.LLM.Dto;

/// <summary>
/// The structured-output shape requested from every provider via
/// <c>IChatClient.GetResponseAsync&lt;TranslationBatchResult&gt;</c>. Microsoft.Extensions.AI
/// generates and enforces the JSON schema for this shape per-provider automatically (native
/// schema-constrained mode where supported, prompt-based JSON fallback otherwise) - one shape,
/// no per-vendor schema code.
/// </summary>
public sealed record TranslationBatchResult(IReadOnlyList<TranslationItem> Translations);

public sealed record TranslationItem(string ChunkId, string TranslatedText);
