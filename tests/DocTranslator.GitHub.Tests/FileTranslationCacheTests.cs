using DocTranslator.GitHub.Cache;
using FluentAssertions;

namespace DocTranslator.GitHub.Tests;

public sealed class FileTranslationCacheTests : IDisposable
{
    private readonly string _cacheRoot = Path.Combine(Path.GetTempPath(), "doc-translator-cache-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try
        {
            Directory.Delete(_cacheRoot, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    [Fact]
    public void TryGet_NothingCached_ReturnsNull()
    {
        var sut = new FileTranslationCache(_cacheRoot);

        sut.TryGet("docs/guide.md", "tr", "hash1").Should().BeNull();
    }

    [Fact]
    public void TryGet_SetThenGetInSameInstance_ReturnsTheValue()
    {
        var sut = new FileTranslationCache(_cacheRoot);

        sut.SetTranslation("docs/guide.md", "tr", "hash1", "translated text");

        sut.TryGet("docs/guide.md", "tr", "hash1").Should().Be("translated text");
    }

    [Fact]
    public void Save_ThenNewInstance_PersistsAcrossInstances()
    {
        var first = new FileTranslationCache(_cacheRoot);
        first.SetTranslation("docs/guide.md", "tr", "hash1", "translated text");
        first.Save();

        var second = new FileTranslationCache(_cacheRoot);
        second.TryGet("docs/guide.md", "tr", "hash1").Should().Be("translated text");
    }

    [Fact]
    public void WithoutSave_NewInstanceDoesNotSeeUnsavedEntries()
    {
        var first = new FileTranslationCache(_cacheRoot);
        first.SetTranslation("docs/guide.md", "tr", "hash1", "translated text");

        var second = new FileTranslationCache(_cacheRoot);
        second.TryGet("docs/guide.md", "tr", "hash1").Should().BeNull();
    }

    [Fact]
    public void TryGet_SameFileDifferentLanguage_AreIsolated()
    {
        var sut = new FileTranslationCache(_cacheRoot);
        sut.SetTranslation("docs/guide.md", "tr", "hash1", "türkçe");
        sut.SetTranslation("docs/guide.md", "de", "hash1", "deutsch");

        sut.TryGet("docs/guide.md", "tr", "hash1").Should().Be("türkçe");
        sut.TryGet("docs/guide.md", "de", "hash1").Should().Be("deutsch");
    }

    [Fact]
    public void TryGet_DifferentContentHash_IsAMiss()
    {
        var sut = new FileTranslationCache(_cacheRoot);
        sut.SetTranslation("docs/guide.md", "tr", "hash1", "translated text");

        sut.TryGet("docs/guide.md", "tr", "hash2").Should().BeNull();
    }
}
