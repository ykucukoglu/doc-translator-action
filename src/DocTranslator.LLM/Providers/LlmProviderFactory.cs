using DocTranslator.LLM.Batching;
using DocTranslator.LLM.Exceptions;
using DocTranslator.LLM.Prompting;
using DocTranslator.LLM.Retry;
using DocTranslator.LLM.Services;
using Microsoft.Extensions.AI;

namespace DocTranslator.LLM.Providers;

public interface ILlmProviderFactory
{
    ILlmTranslationService Create();
}

/// <summary>
/// Selects a provider from GEMINI_API_KEY / OPENAI_API_KEY / ANTHROPIC_API_KEY and the optional
/// INPUT_LLM_PROVIDER override, builds that provider's <see cref="IChatClient"/> via
/// <see cref="IChatClientFactory"/>, and wraps it in the shared <see cref="ChatClientLlmTranslationService"/>.
/// More than one key configured with no explicit provider is a hard configuration error - it is
/// never silently resolved, since guessing wrong on a cost-bearing provider is worse than failing fast.
/// </summary>
public sealed class LlmProviderFactory(
    IEnvironmentProvider environment,
    IChatClientFactory chatClientFactory,
    IPromptBuilder promptBuilder,
    IChunkBatcher chunkBatcher,
    ILlmResponseValidator validator) : ILlmProviderFactory
{
    private const string DefaultGeminiModel = "gemini-2.5-flash";
    private const string DefaultOpenAiModel = "gpt-5-mini";
    private const string DefaultClaudeModel = "claude-sonnet-5";

    public ILlmTranslationService Create()
    {
        var explicitProvider = Normalize(environment.GetEnvironmentVariable("INPUT_LLM_PROVIDER"));

        if (explicitProvider == "fake")
        {
            return new FakeTranslationService();
        }

        var geminiKey = environment.GetEnvironmentVariable("GEMINI_API_KEY");
        var openAiKey = environment.GetEnvironmentVariable("OPENAI_API_KEY");
        var claudeKey = environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");

        var configuredProviders = new List<string>();
        if (!string.IsNullOrWhiteSpace(geminiKey))
        {
            configuredProviders.Add("gemini");
        }

        if (!string.IsNullOrWhiteSpace(openAiKey))
        {
            configuredProviders.Add("openai");
        }

        if (!string.IsNullOrWhiteSpace(claudeKey))
        {
            configuredProviders.Add("claude");
        }

        var resolvedProvider = ResolveProvider(explicitProvider, configuredProviders);

        return resolvedProvider switch
        {
            "gemini" => BuildService("gemini", chatClientFactory.CreateGemini(
                RequireKey(geminiKey, "GEMINI_API_KEY"), ModelOrDefault("INPUT_GEMINI_MODEL", DefaultGeminiModel))),

            "openai" => BuildService("openai", chatClientFactory.CreateOpenAi(
                RequireKey(openAiKey, "OPENAI_API_KEY"), ModelOrDefault("INPUT_OPENAI_MODEL", DefaultOpenAiModel))),

            "claude" => BuildService("claude", chatClientFactory.CreateClaude(
                RequireKey(claudeKey, "ANTHROPIC_API_KEY"), ModelOrDefault("INPUT_CLAUDE_MODEL", DefaultClaudeModel))),

            _ => throw new LlmTranslationException(
                $"Unknown 'llm-provider' value '{resolvedProvider}'. Expected one of: auto, gemini, openai, claude, fake."),
        };
    }

    private static string ResolveProvider(string? explicitProvider, List<string> configuredProviders)
    {
        if (!string.IsNullOrEmpty(explicitProvider) && explicitProvider != "auto")
        {
            return explicitProvider;
        }

        return configuredProviders.Count switch
        {
            1 => configuredProviders[0],
            0 => throw new LlmTranslationException(
                "No LLM provider is configured. Set one of GEMINI_API_KEY, OPENAI_API_KEY, or ANTHROPIC_API_KEY."),
            _ => throw new LlmTranslationException(
                $"Multiple LLM provider API keys are configured ({string.Join(", ", configuredProviders)}) but 'llm-provider' was not set explicitly. "
                + "Set the 'llm-provider' input to one of: gemini, openai, claude."),
        };
    }

    private ChatClientLlmTranslationService BuildService(string providerName, IChatClient chatClient) =>
        new(chatClient, providerName, promptBuilder, chunkBatcher, validator);

    private string ModelOrDefault(string envVarName, string defaultModel)
    {
        var value = environment.GetEnvironmentVariable(envVarName);
        return string.IsNullOrWhiteSpace(value) ? defaultModel : value;
    }

    private static string RequireKey(string? key, string envVarName) =>
        !string.IsNullOrWhiteSpace(key) ? key : throw new LlmTranslationException($"{envVarName} is required but was not set.");

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToLowerInvariant();
}
