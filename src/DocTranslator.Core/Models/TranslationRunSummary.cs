namespace DocTranslator.Core.Models;

/// <summary>
/// Accumulator threaded through a full translation run: per-language chunk/cache counts plus
/// glossary and drift warnings. Consumed by the CLI's console summary and by the PR comment
/// builder. Lives in Core (not Cli) because DocTranslator.GitHub - which builds the PR comment -
/// cannot depend on DocTranslator.Cli.
/// </summary>
public sealed class TranslationRunSummary
{
    public List<LanguageSummary> Languages { get; } = [];

    public List<string> GlossaryWarnings { get; } = [];

    public List<string> DriftWarnings { get; } = [];

    /// <summary>
    /// File/language pairs skipped because of an unexpected reconstruction failure (not the common
    /// missing-marker case, which self-heals - see <see cref="SelfHealedChunks"/> and
    /// <see cref="UnrecoverableChunks"/>). One bad response never aborts the whole run - the
    /// offending pair is skipped and reported here instead.
    /// </summary>
    public List<string> Errors { get; } = [];

    /// <summary>Chunks whose translation dropped a required placeholder/tag marker but were successfully repaired by re-translating just that chunk.</summary>
    public List<string> SelfHealedChunks { get; } = [];

    /// <summary>Chunks that kept dropping required markers even after repair attempts (or had none available) and were left in the source language rather than corrupting the document.</summary>
    public List<string> UnrecoverableChunks { get; } = [];

    /// <summary>
    /// A handful of original/translated paragraph pairs per file/language, so a reviewer can sanity-check
    /// translation quality directly from the PR without opening every changed file.
    /// </summary>
    public List<TranslationPreview> Previews { get; } = [];

    /// <summary>Fenced/indented code blocks skipped entirely at the block level, summed across every file processed this run.</summary>
    public int PreservedCodeBlocks { get; set; }

    /// <summary>Inline code spans preserved as atomic placeholders, summed across every processed chunk.</summary>
    public int PreservedInlineCode { get; set; }

    /// <summary>Autolinks and markdown links preserved (URL untouched), summed across every processed chunk.</summary>
    public int PreservedLinks { get; set; }

    /// <summary><c>dont_translate</c> glossary terms confirmed present, verbatim, in at least one translation this run.</summary>
    public HashSet<string> PreservedGlossaryTerms { get; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed record LanguageSummary(string TargetLanguage, int ChunksTranslated, int ChunksFromCache);

public sealed record TranslationPreview(string FilePath, string TargetLanguage, IReadOnlyList<(string Original, string Translated)> Paragraphs);
