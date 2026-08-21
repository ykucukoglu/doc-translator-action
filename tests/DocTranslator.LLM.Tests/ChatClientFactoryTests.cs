using DocTranslator.LLM.Providers;
using FluentAssertions;

namespace DocTranslator.LLM.Tests;

/// <summary>
/// Each vendor SDK validates API keys lazily (on the first real network call), so construction
/// with a dummy key is expected to succeed offline - these tests only confirm the adapter wiring
/// (model id gets passed through, a non-null IChatClient comes back) rather than making live calls.
/// </summary>
public class ChatClientFactoryTests
{
    private readonly ChatClientFactory _sut = new();

    [Fact]
    public void CreateGemini_ReturnsNonNullChatClient()
    {
        var client = _sut.CreateGemini("dummy-key", "gemini-2.5-flash");

        client.Should().NotBeNull();
    }

    [Fact]
    public void CreateOpenAi_ReturnsNonNullChatClient()
    {
        var client = _sut.CreateOpenAi("dummy-key", "gpt-5-mini");

        client.Should().NotBeNull();
    }

    [Fact]
    public void CreateClaude_ReturnsNonNullChatClient()
    {
        var client = _sut.CreateClaude("dummy-key", "claude-sonnet-5");

        client.Should().NotBeNull();
    }

    [Fact]
    public void CreateOpenAi_WithBaseUrl_ReturnsNonNullChatClient()
    {
        // Points at an OpenAI-compatible endpoint (Ollama, LM Studio, vLLM, OpenRouter, ...)
        // instead of api.openai.com - construction succeeds offline the same way the others do.
        var client = _sut.CreateOpenAi("dummy-key", "llama3", "http://localhost:11434/v1");

        client.Should().NotBeNull();
    }

    [Fact]
    public void CreateAzureOpenAi_ReturnsNonNullChatClient()
    {
        var client = _sut.CreateAzureOpenAi("dummy-key", "https://my-resource.openai.azure.com/", "my-deployment");

        client.Should().NotBeNull();
    }
}
