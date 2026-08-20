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
}
