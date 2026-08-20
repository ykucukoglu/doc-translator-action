using System.Net;
using DocTranslator.LLM.Resilience;
using FluentAssertions;

namespace DocTranslator.LLM.Tests;

public class ChatClientResiliencePipelineTests
{
    [Theory]
    [InlineData(429)]
    [InlineData(500)]
    [InlineData(503)]
    public async Task ExecuteAsync_TransientHttpStatus_RetriesUntilSuccess(int statusCode)
    {
        var pipeline = ChatClientResiliencePipeline.Create(maxRetryAttempts: 2);
        var attempts = 0;

        var result = await pipeline.ExecuteAsync(async _ =>
        {
            attempts++;
            if (attempts == 1)
            {
                throw new HttpRequestException("transient", null, (HttpStatusCode)statusCode);
            }

            await Task.Yield();
            return "ok";
        }, CancellationToken.None);

        result.Should().Be("ok");
        attempts.Should().Be(2);
    }

    [Fact]
    public async Task ExecuteAsync_NonTransientHttpStatus_DoesNotRetry()
    {
        var pipeline = ChatClientResiliencePipeline.Create(maxRetryAttempts: 2);
        var attempts = 0;

        var act = async () => await pipeline.ExecuteAsync<string>(_ =>
        {
            attempts++;
            throw new HttpRequestException("bad request", null, HttpStatusCode.BadRequest);
        }, CancellationToken.None);

        await act.Should().ThrowAsync<HttpRequestException>();
        attempts.Should().Be(1);
    }

    [Fact]
    public async Task ExecuteAsync_ExhaustsRetries_ThrowsOriginalException()
    {
        var pipeline = ChatClientResiliencePipeline.Create(maxRetryAttempts: 2);
        var attempts = 0;

        var act = async () => await pipeline.ExecuteAsync<string>(_ =>
        {
            attempts++;
            throw new HttpRequestException("still down", null, HttpStatusCode.ServiceUnavailable);
        }, CancellationToken.None);

        await act.Should().ThrowAsync<HttpRequestException>();
        attempts.Should().Be(3); // 1 initial + 2 retries
    }

    [Fact]
    public async Task ExecuteAsync_NullStatusCode_IsTreatedAsTransientConnectionFailure()
    {
        // A null StatusCode means the request never got a response at all (DNS/connection
        // failure) - just as worth retrying as an explicit 429/5xx.
        var pipeline = ChatClientResiliencePipeline.Create(maxRetryAttempts: 1);
        var attempts = 0;

        var result = await pipeline.ExecuteAsync(async _ =>
        {
            attempts++;
            if (attempts == 1)
            {
                throw new HttpRequestException("connection reset");
            }

            await Task.Yield();
            return "ok";
        }, CancellationToken.None);

        result.Should().Be("ok");
        attempts.Should().Be(2);
    }
}
