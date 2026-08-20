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
}

public sealed record LanguageSummary(string TargetLanguage, int ChunksTranslated, int ChunksFromCache);
