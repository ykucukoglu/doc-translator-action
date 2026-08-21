using DocTranslator.Cli.Logging;
using DocTranslator.Core.Models;
using DocTranslator.Core.Telemetry;
using FluentAssertions;

namespace DocTranslator.Cli.Tests;

/// <summary>
/// GITHUB_STEP_SUMMARY is read directly from the environment (see JobSummaryWriter), so each test
/// points it at its own temp file and cleans up afterward.
/// </summary>
public sealed class JobSummaryWriterTests : IDisposable
{
    private readonly string _summaryFile = Path.Combine(Path.GetTempPath(), $"doc-translator-summary-{Guid.NewGuid():N}.md");
    private readonly string? _originalEnvValue = Environment.GetEnvironmentVariable("GITHUB_STEP_SUMMARY");

    public JobSummaryWriterTests() => Environment.SetEnvironmentVariable("GITHUB_STEP_SUMMARY", _summaryFile);

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("GITHUB_STEP_SUMMARY", _originalEnvValue);
        if (File.Exists(_summaryFile))
        {
            File.Delete(_summaryFile);
        }
    }

    [Fact]
    public async Task WriteAsync_IncludesExecutionTimeAndCacheHitRate()
    {
        var tokenUsageTracker = new TokenUsageTracker();
        var sut = new JobSummaryWriter(tokenUsageTracker);
        var summary = new TranslationRunSummary();
        summary.Languages.Add(new LanguageSummary("tr", ChunksTranslated: 1, ChunksFromCache: 3));

        await sut.WriteAsync(summary, translatedFilesCount: 1, pullRequestUrl: null, dryRun: true, TimeSpan.FromSeconds(4.2), CancellationToken.None);

        var content = await File.ReadAllTextAsync(_summaryFile);
        content.Should().Contain("4.2s");
        content.Should().Contain("75% from cache"); // 3 of 4 pairs
    }

    [Fact]
    public async Task WriteAsync_PreservedContent_AppearsInTheMetricsTable()
    {
        var tokenUsageTracker = new TokenUsageTracker();
        var sut = new JobSummaryWriter(tokenUsageTracker);
        var summary = new TranslationRunSummary { PreservedCodeBlocks = 5 };

        await sut.WriteAsync(summary, translatedFilesCount: 1, pullRequestUrl: null, dryRun: true, TimeSpan.FromSeconds(1), CancellationToken.None);

        var content = await File.ReadAllTextAsync(_summaryFile);
        content.Should().Contain("5 code blocks");
    }

    [Fact]
    public async Task WriteAsync_NoStepSummaryEnvVar_DoesNothing()
    {
        Environment.SetEnvironmentVariable("GITHUB_STEP_SUMMARY", null);
        var tokenUsageTracker = new TokenUsageTracker();
        var sut = new JobSummaryWriter(tokenUsageTracker);

        var act = () => sut.WriteAsync(new TranslationRunSummary(), 0, null, true, TimeSpan.Zero, CancellationToken.None);

        await act.Should().NotThrowAsync();
        File.Exists(_summaryFile).Should().BeFalse();
    }
}
