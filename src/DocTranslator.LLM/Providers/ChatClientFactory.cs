using System.ClientModel;
using Azure.AI.OpenAI;
using Microsoft.Extensions.AI;

namespace DocTranslator.LLM.Providers;

public interface IChatClientFactory
{
    IChatClient CreateGemini(string apiKey, string model);

    // baseUrl points the official OpenAI SDK at any OpenAI-compatible endpoint instead of
    // api.openai.com - Ollama, LM Studio, vLLM, OpenRouter, and others all speak this same
    // request/response shape, so this is the seam that lets the action run against a free/local
    // model with no real OpenAI account at all.
    IChatClient CreateOpenAi(string apiKey, string model, string? baseUrl = null);

    IChatClient CreateClaude(string apiKey, string model);

    IChatClient CreateAzureOpenAi(string apiKey, string endpoint, string deploymentName);
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

    public IChatClient CreateOpenAi(string apiKey, string model, string? baseUrl = null)
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            return new OpenAI.Chat.ChatClient(model, apiKey).AsIChatClient();
        }

        var options = new OpenAI.OpenAIClientOptions { Endpoint = new Uri(baseUrl) };
        return new OpenAI.Chat.ChatClient(model, new ApiKeyCredential(apiKey), options).AsIChatClient();
    }

    public IChatClient CreateClaude(string apiKey, string model) =>
        new Anthropic.AnthropicClient(new Anthropic.Core.ClientOptions { ApiKey = apiKey }).AsIChatClient(model);

    // deploymentName is the name chosen when the model was deployed in the Azure OpenAI resource,
    // not necessarily the same string as the underlying model id (e.g. "gpt-4o").
    public IChatClient CreateAzureOpenAi(string apiKey, string endpoint, string deploymentName) =>
        new AzureOpenAIClient(new Uri(endpoint), new ApiKeyCredential(apiKey))
            .GetChatClient(deploymentName)
            .AsIChatClient();
}
