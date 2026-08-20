using System.Globalization;

namespace DocTranslator.Core.Models;

/// <summary>
/// Recorded at the top of every translated file as an HTML-comment header, so a later run can
/// tell whether the translation is still in sync with its source (see
/// <see cref="DocTranslator.Core.Provenance.IDriftDetector"/>).
/// </summary>
public sealed record TranslationProvenance(
    string SourceContentHash,
    string SourceFilePath,
    string TargetLanguage,
    DateTimeOffset GeneratedAtUtc)
{
    public const string HeaderPrefix = "<!-- doc-translator:";

    public string ToHeaderComment() => string.Create(
        CultureInfo.InvariantCulture,
        $"<!-- doc-translator: source-hash={SourceContentHash}; source-path={SourceFilePath}; target-lang={TargetLanguage}; generated={GeneratedAtUtc:O} -->");
}
