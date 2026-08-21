using System.Globalization;
using DocTranslator.Core.Telemetry;
using DocTranslator.LLM.Batching;
using DocTranslator.LLM.Exceptions;
using DocTranslator.LLM.Prompting;
using DocTranslator.LLM.Resilience;
using DocTranslator.LLM.Retry;
using DocTranslator.LLM.Services;
using Microsoft.Extensions.AI;

namespace DocTranslator.LLM.Providers;

public interface ILlmProviderFactory
{
    ILlmTranslationService Create();
}

/// <summary>
/// Selects a provider from GEMINI_API_KEY / OPENAI_API_KEY / ANTHROPIC_API_KEY / AZURE_OPENAI_API_KEY
/// and the optional INPUT_LLM_PROVIDER override, builds that provider's <see cref="IChatClient"/>
/// via <see cref="IChatClientFactory"/>, and wraps it in the shared <see cref="ChatClientLlmTranslationService"/>.
/// More than one key configured with no explicit provider is a hard configuration error - it is
/// never silently resolved, since guessing wrong on a cost-bearing provider is worse than failing fast.
///
/// If INPUT_LLM_FALLBACK_PROVIDER names a second, different provider, the result is wrapped in
/// <see cref="FallbackLlmTranslationService"/> instead - opt-in, since which provider a run ends up
/// billing against should never be a silent inference from which keys happen to be configured.
/// </summary>
public sealed class LlmProviderFactory(
    IEnvironmentProvider environment,
    IChatClientFactory chatClientFactory,
    IPromptBuilder promptBuilder,
    IChunkBatcher chunkBatcher,
    ILlmResponseValidator validator,
    ITokenUsageTracker tokenUsageTracker) : ILlmProviderFactory
{
    private const string DefaultGeminiModel = "gemini-2.5-flash";
    private const string DefaultOpenAiModel = "gpt-5-mini";
    private const string DefaultClaudeModel = "claude-sonnet-5";
    private const int DefaultMaxParallelRequests = 4;

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
        var azureOpenAiKey = environment.GetEnvironmentVariable("AZURE_OPENAI_API_KEY");

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

        if (!string.IsNullOrWhiteSpace(azureOpenAiKey))
        {
            configuredProviders.Add("azure-openai");
        }

        var resolvedProvider = ResolveProvider(explicitProvider, configuredProviders);
        var primary = BuildProviderService(resolvedProvider, geminiKey, openAiKey, claudeKey, azureOpenAiKey);

        var fallbackProvider = Normalize(environment.GetEnvironmentVariable("INPUT_LLM_FALLBACK_PROVIDER"));
        if (string.IsNullOrEmpty(fallbackProvider))
        {
            return primary;
        }

        if (fallbackProvider == resolvedProvider)
        {
            throw new LlmTranslationException(
                $"'llm-fallback-provider' ('{fallbackProvider}') must be different from the primary provider ('{resolvedProvider}').");
        }

        ILlmTranslationService fallback = fallbackProvider == "fake"
            ? new FakeTranslationService()
            : BuildProviderService(fallbackProvider, geminiKey, openAiKey, claudeKey, azureOpenAiKey);

        return new FallbackLlmTranslationService(primary, fallback);
    }

    private ChatClientLlmTranslationService BuildProviderService(
        string providerName, string? geminiKey, string? openAiKey, string? claudeKey, string? azureOpenAiKey) =>
        providerName switch
        {
            "gemini" => BuildService("gemini", chatClientFactory.CreateGemini(
                RequireKey(geminiKey, "GEMINI_API_KEY"), ModelOrDefault("INPUT_GEMINI_MODEL", DefaultGeminiModel))),

            "openai" => BuildService("openai", chatClientFactory.CreateOpenAi(
                RequireKey(openAiKey, "OPENAI_API_KEY"), ModelOrDefault("INPUT_OPENAI_MODEL", DefaultOpenAiModel))),

            "claude" => BuildService("claude", chatClientFactory.CreateClaude(
                RequireKey(claudeKey, "ANTHROPIC_API_KEY"), ModelOrDefault("INPUT_CLAUDE_MODEL", DefaultClaudeModel))),

            "azure-openai" => BuildService("azure-openai", chatClientFactory.CreateAzureOpenAi(
                RequireKey(azureOpenAiKey, "AZURE_OPENAI_API_KEY"),
                RequireKey(environment.GetEnvironmentVariable("INPUT_AZURE_OPENAI_ENDPOINT"), "azure-openai-endpoint"),
                RequireKey(environment.GetEnvironmentVariable("INPUT_AZURE_OPENAI_DEPLOYMENT"), "azure-openai-deployment"))),

            _ => throw new LlmTranslationException(
                $"Unknown provider '{providerName}'. Expected one of: gemini, openai, claude, azure-openai."),
        };

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
        new(
            chatClient,
            providerName,
            promptBuilder,
            chunkBatcher,
            validator,
            ChatClientResiliencePipeline.Create(),
            tokenUsageTracker,
            MaxParallelRequestsOrDefault());

    private string ModelOrDefault(string envVarName, string defaultModel)
    {
        var value = environment.GetEnvironmentVariable(envVarName);
        return string.IsNullOrWhiteSpace(value) ? defaultModel : value;
    }

    private int MaxParallelRequestsOrDefault()
    {
        var value = environment.GetEnvironmentVariable("INPUT_MAX_PARALLEL_REQUESTS");
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) && parsed > 0
            ? parsed
            : DefaultMaxParallelRequests;
    }

    private static string RequireKey(string? key, string envVarName) =>
        !string.IsNullOrWhiteSpace(key) ? key : throw new LlmTranslationException($"{envVarName} is required but was not set.");

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToLowerInvariant();
}
