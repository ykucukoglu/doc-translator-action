using System.Text.Json;

namespace DocTranslator.GitHub.Cache;

public interface ITranslationCache
{
    /// <summary>Looks up a previously-translated chunk by content hash. Null on a cache miss.</summary>
    string? TryGet(string sourceFilePath, string targetLanguage, string chunkContentHash);

    /// <summary>Records a freshly-translated chunk. Call <see cref="Save"/> to persist.</summary>
    void SetTranslation(string sourceFilePath, string targetLanguage, string chunkContentHash, string translatedText);

    /// <summary>Flushes every manifest touched since the cache was constructed (or last saved) to disk.</summary>
    void Save();
}

/// <summary>
/// The primary paragraph-level cost-optimization mechanism: a per-source-file, per-target-language
/// JSON manifest (<c>{cacheRoot}/{lang}/{relativePath}.json</c>, ChunkContentHash -&gt; TranslatedText)
/// checked before every LLM call. Keyed by content hash rather than git line ranges, so it is
/// immune to line-number drift from unrelated edits elsewhere in a file.
/// </summary>
public sealed class FileTranslationCache(string cacheRootDirectory) : ITranslationCache
{
    private readonly Dictionary<string, Dictionary<string, string>> _manifests = new(StringComparer.Ordinal);
    private readonly HashSet<string> _dirty = new(StringComparer.Ordinal);

    public string? TryGet(string sourceFilePath, string targetLanguage, string chunkContentHash) =>
        LoadManifest(sourceFilePath, targetLanguage).GetValueOrDefault(chunkContentHash);

    public void SetTranslation(string sourceFilePath, string targetLanguage, string chunkContentHash, string translatedText)
    {
        var manifestPath = GetManifestPath(sourceFilePath, targetLanguage);
        LoadManifest(sourceFilePath, targetLanguage)[chunkContentHash] = translatedText;
        _dirty.Add(manifestPath);
    }

    public void Save()
    {
        foreach (var manifestPath in _dirty)
        {
            var directory = Path.GetDirectoryName(manifestPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var json = JsonSerializer.Serialize(_manifests[manifestPath]);
            File.WriteAllText(manifestPath, json);
        }

        _dirty.Clear();
    }

    private Dictionary<string, string> LoadManifest(string sourceFilePath, string targetLanguage)
    {
        var manifestPath = GetManifestPath(sourceFilePath, targetLanguage);
        if (_manifests.TryGetValue(manifestPath, out var cached))
        {
            return cached;
        }

        var manifest = File.Exists(manifestPath)
            ? JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(manifestPath)) ?? []
            : [];

        _manifests[manifestPath] = manifest;
        return manifest;
    }

    private string GetManifestPath(string sourceFilePath, string targetLanguage)
    {
        var normalizedRelativePath = sourceFilePath.Replace('\\', '/').TrimStart('/');
        return Path.Combine(cacheRootDirectory, targetLanguage, normalizedRelativePath) + ".json";
    }
}
