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
}

public sealed record LanguageSummary(string TargetLanguage, int ChunksTranslated, int ChunksFromCache);
