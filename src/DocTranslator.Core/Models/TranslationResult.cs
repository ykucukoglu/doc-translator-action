namespace DocTranslator.Core.Models;

/// <summary>
/// The outcome of translating one source file into one target language.
/// </summary>
public sealed record TranslationResult(
    string FilePath,
    string TargetLanguage,
    int ChunksTranslated,
    IReadOnlyList<string> Warnings,
    string OutputMarkdown);
