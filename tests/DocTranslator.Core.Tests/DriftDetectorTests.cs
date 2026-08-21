using DocTranslator.Core.Models;
using DocTranslator.Core.Provenance;
using FluentAssertions;

namespace DocTranslator.Core.Tests;

public class DriftDetectorTests : IDisposable
{
    private readonly DriftDetector _sut = new();
    private readonly string _tempFile = Path.Combine(Path.GetTempPath(), $"doc-translator-drift-{Guid.NewGuid():N}.md");

    public void Dispose()
    {
        if (File.Exists(_tempFile))
        {
            File.Delete(_tempFile);
        }

        GC.SuppressFinalize(this);
    }

    [Fact]
    public void CheckDrift_HashMatches_IsNotStale()
    {
        File.WriteAllText(_tempFile, "# Hello\n\nSource content.\n");
        var hash = _sut.HashFile(_tempFile);
        var provenance = new TranslationProvenance(hash, _tempFile, "de", DateTimeOffset.UtcNow);
        var translatedContent = provenance.ToHeaderComment() + "\n\n# Hallo\n\nÜbersetzter Inhalt.\n";

        var result = _sut.CheckDrift(_tempFile, translatedContent);

        result.IsStale.Should().BeFalse();
        result.ExistingProvenance.Should().NotBeNull();
    }

    [Fact]
    public void CheckDrift_SourceChangedSinceGeneration_IsStale()
    {
        File.WriteAllText(_tempFile, "# Hello\n\nOriginal content.\n");
        var originalHash = _sut.HashFile(_tempFile);
        var provenance = new TranslationProvenance(originalHash, _tempFile, "de", DateTimeOffset.UtcNow);
        var translatedContent = provenance.ToHeaderComment() + "\n\n# Hallo\n\nÜbersetzter Inhalt.\n";

        // source file changes after the translation was generated
        File.WriteAllText(_tempFile, "# Hello\n\nContent has changed since translation.\n");

        var result = _sut.CheckDrift(_tempFile, translatedContent);

        result.IsStale.Should().BeTrue();
        result.Reason.Should().Contain("changed");
    }

    [Fact]
    public void CheckDrift_MissingHeader_TreatedAsStale()
    {
        File.WriteAllText(_tempFile, "# Hello\n\nSource content.\n");

        var result = _sut.CheckDrift(_tempFile, "# Hallo\n\nKein Header hier.\n");

        result.IsStale.Should().BeTrue();
        result.ExistingProvenance.Should().BeNull();
    }

    [Fact]
    public void TryParseHeader_WellFormedHeader_RoundTripsAllFields()
    {
        var generatedAt = new DateTimeOffset(2026, 1, 15, 10, 30, 0, TimeSpan.Zero);
        var provenance = new TranslationProvenance("abc123", "docs/guide.md", "fr", generatedAt);

        var parsed = _sut.TryParseHeader(provenance.ToHeaderComment() + "\n\nrest of the file");

        parsed.Should().NotBeNull();
        parsed!.SourceContentHash.Should().Be("abc123");
        parsed.SourceFilePath.Should().Be("docs/guide.md");
        parsed.TargetLanguage.Should().Be("fr");
        parsed.GeneratedAtUtc.Should().Be(generatedAt);
    }

    [Fact]
    public void TryParseHeader_NoHeaderPresent_ReturnsNull()
    {
        var parsed = _sut.TryParseHeader("# Just a normal document\n\nNo header here.\n");

        parsed.Should().BeNull();
    }

    [Fact]
    public void TryParseHeader_HeaderPrecededByFrontmatter_StillFound()
    {
        // AstReconstructor always keeps frontmatter as the file's first bytes and writes the
        // header just after it - the header search must skip past the frontmatter fence, not just
        // look at line 1.
        var provenance = new TranslationProvenance("abc123", "docs/guide.md", "fr", DateTimeOffset.UtcNow);
        var content = "---\ntitle: Foo\n---\n\n" + provenance.ToHeaderComment() + "\n\n# Foo\n\nBody.\n";

        var parsed = _sut.TryParseHeader(content);

        parsed.Should().NotBeNull();
        parsed!.SourceContentHash.Should().Be("abc123");
    }

    [Fact]
    public void TryParseHeader_HeaderPrecededByTomlFrontmatter_StillFound()
    {
        var provenance = new TranslationProvenance("abc123", "docs/guide.md", "fr", DateTimeOffset.UtcNow);
        var content = "+++\ntitle = \"Foo\"\n+++\n\n" + provenance.ToHeaderComment() + "\n\n# Foo\n\nBody.\n";

        var parsed = _sut.TryParseHeader(content);

        parsed.Should().NotBeNull();
        parsed!.SourceContentHash.Should().Be("abc123");
    }

    [Fact]
    public void CheckDrift_HashMatchesWithFrontmatterPresent_IsNotStale()
    {
        File.WriteAllText(_tempFile, "# Hello\n\nSource content.\n");
        var hash = _sut.HashFile(_tempFile);
        var provenance = new TranslationProvenance(hash, _tempFile, "de", DateTimeOffset.UtcNow);
        var translatedContent = "---\ntitle: Foo\n---\n\n" + provenance.ToHeaderComment() + "\n\n# Hallo\n\nÜbersetzter Inhalt.\n";

        var result = _sut.CheckDrift(_tempFile, translatedContent);

        result.IsStale.Should().BeFalse();
    }
}
