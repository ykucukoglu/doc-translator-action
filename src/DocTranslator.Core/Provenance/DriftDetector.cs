using System.Globalization;
using System.Security.Cryptography;
using DocTranslator.Core.Models;

namespace DocTranslator.Core.Provenance;

public interface IDriftDetector
{
    /// <summary>SHA-256 of a file's current raw bytes, used both for the translation cache and for provenance headers.</summary>
    string HashFile(string filePath);

    /// <summary>Parses the provenance header written by <see cref="TranslationProvenance.ToHeaderComment"/>, if present.</summary>
    TranslationProvenance? TryParseHeader(string translatedFileContent);

    /// <summary>
    /// Compares an existing translated file's provenance header against the current hash of its
    /// source file. Missing or unparseable headers are treated as stale/unknown - never silently
    /// trusted - since there is no positive evidence they are still in sync.
    /// </summary>
    DriftCheckResult CheckDrift(string sourceFilePath, string existingTranslatedFileContent);
}

public sealed class DriftDetector : IDriftDetector
{
    public string HashFile(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        var hash = SHA256.HashData(stream);
        return Convert.ToHexStringLower(hash);
    }

    public TranslationProvenance? TryParseHeader(string translatedFileContent)
    {
        var firstLine = ReadFirstLine(translatedFileContent);
        if (firstLine is null || !firstLine.StartsWith(TranslationProvenance.HeaderPrefix, StringComparison.Ordinal))
        {
            return null;
        }

        const string suffix = "-->";
        if (!firstLine.EndsWith(suffix, StringComparison.Ordinal))
        {
            return null;
        }

        var inner = firstLine[TranslationProvenance.HeaderPrefix.Length..^suffix.Length].Trim();
        var fields = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var part in inner.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            var separatorIndex = part.IndexOf('=');
            if (separatorIndex < 0)
            {
                return null;
            }

            var key = part[..separatorIndex].Trim();
            var value = part[(separatorIndex + 1)..].Trim();
            fields[key] = value;
        }

        if (!fields.TryGetValue("source-hash", out var hash)
            || !fields.TryGetValue("source-path", out var path)
            || !fields.TryGetValue("target-lang", out var lang)
            || !fields.TryGetValue("generated", out var generated)
            || !DateTimeOffset.TryParse(generated, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var generatedAt))
        {
            return null;
        }

        return new TranslationProvenance(hash, path, lang, generatedAt);
    }

    public DriftCheckResult CheckDrift(string sourceFilePath, string existingTranslatedFileContent)
    {
        var provenance = TryParseHeader(existingTranslatedFileContent);
        if (provenance is null)
        {
            return new DriftCheckResult(IsStale: true, Reason: "No provenance header found in existing translation.", ExistingProvenance: null);
        }

        if (!File.Exists(sourceFilePath))
        {
            return new DriftCheckResult(IsStale: true, Reason: $"Source file '{sourceFilePath}' no longer exists.", provenance);
        }

        var currentHash = HashFile(sourceFilePath);
        return currentHash == provenance.SourceContentHash
            ? new DriftCheckResult(IsStale: false, Reason: "Up to date.", provenance)
            : new DriftCheckResult(IsStale: true, Reason: "Source file changed since this translation was generated.", provenance);
    }

    private static string? ReadFirstLine(string content)
    {
        var newlineIndex = content.IndexOf('\n');
        var line = newlineIndex < 0 ? content : content[..newlineIndex];
        return line.TrimEnd('\r');
    }
}
