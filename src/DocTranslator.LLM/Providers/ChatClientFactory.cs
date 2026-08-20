using Microsoft.Extensions.AI;

namespace DocTranslator.LLM.Providers;

public interface IChatClientFactory
{
    IChatClient CreateGemini(string apiKey, string model);

    IChatClient CreateOpenAi(string apiKey, string model);

    IChatClient CreateClaude(string apiKey, string model);
}

/// <summary>
/// The only per-vendor code in this codebase: each method just constructs the vendor's official
/// SDK client and adapts it to <see cref="IChatClient"/> via that SDK's own <c>AsIChatClient</c>
/// extension. Everything downstream of this factory is provider-agnostic.
/// </summary>
public sealed class ChatClientFactory : IChatClientFactory
{
    public IChatClient CreateGemini(string apiKey, string model) =>
        new Google.GenAI.Client(apiKey: apiKey).AsIChatClient(model);

    public IChatClient CreateOpenAi(string apiKey, string model) =>
        new OpenAI.Chat.ChatClient(model, apiKey).AsIChatClient();

    public IChatClient CreateClaude(string apiKey, string model) =>
        new Anthropic.AnthropicClient(new Anthropic.Core.ClientOptions { ApiKey = apiKey }).AsIChatClient(model);
}
