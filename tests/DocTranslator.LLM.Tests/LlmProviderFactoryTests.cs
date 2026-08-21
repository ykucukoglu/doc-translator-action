using DocTranslator.Core.Glossary;
using DocTranslator.Core.Telemetry;
using DocTranslator.LLM.Batching;
using DocTranslator.LLM.Exceptions;
using DocTranslator.LLM.Prompting;
using DocTranslator.LLM.Providers;
using DocTranslator.LLM.Retry;
using DocTranslator.LLM.Services;
using FluentAssertions;
using Microsoft.Extensions.AI;
using Moq;

namespace DocTranslator.LLM.Tests;

public class LlmProviderFactoryTests
{
    private readonly Mock<IChatClientFactory> _chatClientFactory = new();
    private readonly IPromptBuilder _promptBuilder = new PromptBuilder(new GlossaryService());
    private readonly IChunkBatcher _chunkBatcher = new ChunkBatcher();
    private readonly ILlmResponseValidator _validator = new LlmResponseValidator();
    private readonly ITokenUsageTracker _tokenUsageTracker = new TokenUsageTracker();

    public LlmProviderFactoryTests()
    {
        _chatClientFactory.Setup(f => f.CreateGemini(It.IsAny<string>(), It.IsAny<string>())).Returns(Mock.Of<IChatClient>());
        _chatClientFactory.Setup(f => f.CreateOpenAi(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>())).Returns(Mock.Of<IChatClient>());
        _chatClientFactory.Setup(f => f.CreateClaude(It.IsAny<string>(), It.IsAny<string>())).Returns(Mock.Of<IChatClient>());
        _chatClientFactory.Setup(f => f.CreateAzureOpenAi(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>())).Returns(Mock.Of<IChatClient>());
    }

    private LlmProviderFactory BuildSut(IReadOnlyDictionary<string, string> env) =>
        new(new FakeEnvironmentProvider(env), _chatClientFactory.Object, _promptBuilder, _chunkBatcher, _validator, _tokenUsageTracker);

    [Fact]
    public void Create_OnlyGeminiKeySet_ResolvesToGemini()
    {
        var sut = BuildSut(new Dictionary<string, string> { ["GEMINI_API_KEY"] = "g-key" });

        var service = sut.Create();

        service.ProviderName.Should().Be("gemini");
        _chatClientFactory.Verify(f => f.CreateGemini("g-key", It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public void Create_OnlyOpenAiKeySet_ResolvesToOpenAi()
    {
        var sut = BuildSut(new Dictionary<string, string> { ["OPENAI_API_KEY"] = "o-key" });

        var service = sut.Create();

        service.ProviderName.Should().Be("openai");
    }

    [Fact]
    public void Create_OnlyAnthropicKeySet_ResolvesToClaude()
    {
        var sut = BuildSut(new Dictionary<string, string> { ["ANTHROPIC_API_KEY"] = "a-key" });

        var service = sut.Create();

        service.ProviderName.Should().Be("claude");
    }

    [Fact]
    public void Create_AzureOpenAiKeyEndpointAndDeploymentSet_ResolvesToAzureOpenAi()
    {
        var sut = BuildSut(new Dictionary<string, string>
        {
            ["AZURE_OPENAI_API_KEY"] = "az-key",
            ["INPUT_AZURE_OPENAI_ENDPOINT"] = "https://my-resource.openai.azure.com/",
            ["INPUT_AZURE_OPENAI_DEPLOYMENT"] = "my-deployment",
        });

        var service = sut.Create();

        service.ProviderName.Should().Be("azure-openai");
        _chatClientFactory.Verify(f => f.CreateAzureOpenAi("az-key", "https://my-resource.openai.azure.com/", "my-deployment"), Times.Once);
    }

    [Fact]
    public void Create_AzureOpenAiKeySetButEndpointMissing_Throws()
    {
        var sut = BuildSut(new Dictionary<string, string>
        {
            ["AZURE_OPENAI_API_KEY"] = "az-key",
            ["INPUT_AZURE_OPENAI_DEPLOYMENT"] = "my-deployment",
        });

        var act = sut.Create;

        act.Should().Throw<LlmTranslationException>().WithMessage("*azure-openai-endpoint*");
    }

    [Fact]
    public void Create_FallbackProviderConfigured_ReturnsFallbackWrapperReportingPrimaryProviderName()
    {
        var sut = BuildSut(new Dictionary<string, string>
        {
            ["GEMINI_API_KEY"] = "g-key",
            ["ANTHROPIC_API_KEY"] = "a-key",
            ["INPUT_LLM_PROVIDER"] = "gemini",
            ["INPUT_LLM_FALLBACK_PROVIDER"] = "claude",
        });

        var service = sut.Create();

        service.Should().BeOfType<FallbackLlmTranslationService>();
        service.ProviderName.Should().Be("gemini");
    }

    [Fact]
    public void Create_FallbackProviderSameAsPrimary_Throws()
    {
        var sut = BuildSut(new Dictionary<string, string>
        {
            ["GEMINI_API_KEY"] = "g-key",
            ["INPUT_LLM_PROVIDER"] = "gemini",
            ["INPUT_LLM_FALLBACK_PROVIDER"] = "gemini",
        });

        var act = sut.Create;

        act.Should().Throw<LlmTranslationException>().WithMessage("*must be different from the primary provider*");
    }

    [Fact]
    public void Create_FallbackProviderFake_DoesNotRequireASecondRealKey()
    {
        var sut = BuildSut(new Dictionary<string, string>
        {
            ["GEMINI_API_KEY"] = "g-key",
            ["INPUT_LLM_PROVIDER"] = "gemini",
            ["INPUT_LLM_FALLBACK_PROVIDER"] = "fake",
        });

        var act = sut.Create;

        act.Should().NotThrow();
    }

    [Fact]
    public void Create_NoFallbackProviderConfigured_ReturnsPlainService()
    {
        var sut = BuildSut(new Dictionary<string, string> { ["GEMINI_API_KEY"] = "g-key" });

        var service = sut.Create();

        service.Should().NotBeOfType<FallbackLlmTranslationService>();
    }

    [Fact]
    public void Create_OpenAiBaseUrlSet_IsPassedToChatClientFactory()
    {
        var sut = BuildSut(new Dictionary<string, string>
        {
            ["OPENAI_API_KEY"] = "ollama",
            ["INPUT_OPENAI_MODEL"] = "llama3",
            ["INPUT_OPENAI_BASE_URL"] = "http://localhost:11434/v1",
        });

        sut.Create();

        _chatClientFactory.Verify(f => f.CreateOpenAi("ollama", "llama3", "http://localhost:11434/v1"), Times.Once);
    }

    [Fact]
    public void Create_NoOpenAiBaseUrl_PassesNullToChatClientFactory()
    {
        var sut = BuildSut(new Dictionary<string, string> { ["OPENAI_API_KEY"] = "o-key" });

        sut.Create();

        _chatClientFactory.Verify(f => f.CreateOpenAi("o-key", It.IsAny<string>(), null), Times.Once);
    }

    [Fact]
    public void Create_NoKeysSet_Throws()
    {
        var sut = BuildSut(new Dictionary<string, string>());

        var act = sut.Create;

        act.Should().Throw<LlmTranslationException>().WithMessage("*No LLM provider*");
    }

    [Fact]
    public void Create_TwoKeysSetWithoutExplicitProvider_Throws()
    {
        var sut = BuildSut(new Dictionary<string, string>
        {
            ["GEMINI_API_KEY"] = "g-key",
            ["OPENAI_API_KEY"] = "o-key",
        });

        var act = sut.Create;

        act.Should().Throw<LlmTranslationException>().WithMessage("*Multiple LLM provider*");
    }

    [Fact]
    public void Create_AllThreeKeysSetWithoutExplicitProvider_Throws()
    {
        var sut = BuildSut(new Dictionary<string, string>
        {
            ["GEMINI_API_KEY"] = "g-key",
            ["OPENAI_API_KEY"] = "o-key",
            ["ANTHROPIC_API_KEY"] = "a-key",
        });

        var act = sut.Create;

        act.Should().Throw<LlmTranslationException>().WithMessage("*Multiple LLM provider*");
    }

    [Fact]
    public void Create_TwoKeysSetWithExplicitProvider_ResolvesToThatProvider()
    {
        var sut = BuildSut(new Dictionary<string, string>
        {
            ["GEMINI_API_KEY"] = "g-key",
            ["OPENAI_API_KEY"] = "o-key",
            ["INPUT_LLM_PROVIDER"] = "openai",
        });

        var service = sut.Create();

        service.ProviderName.Should().Be("openai");
    }

    [Fact]
    public void Create_ExplicitFakeProvider_ReturnsFakeTranslationServiceEvenWithoutAnyKeys()
    {
        var sut = BuildSut(new Dictionary<string, string> { ["INPUT_LLM_PROVIDER"] = "fake" });

        var service = sut.Create();

        service.Should().BeOfType<FakeTranslationService>();
    }

    [Fact]
    public void Create_ExplicitProviderButItsKeyMissing_Throws()
    {
        var sut = BuildSut(new Dictionary<string, string> { ["INPUT_LLM_PROVIDER"] = "claude" });

        var act = sut.Create;

        act.Should().Throw<LlmTranslationException>().WithMessage("*ANTHROPIC_API_KEY*");
    }

    [Fact]
    public void Create_ExplicitAutoWithSingleKey_ResolvesNormally()
    {
        var sut = BuildSut(new Dictionary<string, string>
        {
            ["GEMINI_API_KEY"] = "g-key",
            ["INPUT_LLM_PROVIDER"] = "auto",
        });

        var service = sut.Create();

        service.ProviderName.Should().Be("gemini");
    }

    [Fact]
    public void Create_CustomModelEnvVar_IsPassedToChatClientFactory()
    {
        var sut = BuildSut(new Dictionary<string, string>
        {
            ["GEMINI_API_KEY"] = "g-key",
            ["INPUT_GEMINI_MODEL"] = "gemini-custom-model",
        });

        sut.Create();

        _chatClientFactory.Verify(f => f.CreateGemini("g-key", "gemini-custom-model"), Times.Once);
    }

    [Fact]
    public void Create_NoMaxParallelRequestsEnvVar_ServiceStillConstructsSuccessfully()
    {
        // MaxParallelRequestsOrDefault falls back to 4 for anything missing/invalid - this just
        // proves that path doesn't throw; the actual concurrency bound is covered by
        // ChatClientLlmTranslationServiceTests (constructed directly with an explicit value there).
        var sut = BuildSut(new Dictionary<string, string> { ["GEMINI_API_KEY"] = "g-key" });

        var act = sut.Create;

        act.Should().NotThrow();
    }

    [Fact]
    public void Create_InvalidMaxParallelRequestsEnvVar_FallsBackWithoutThrowing()
    {
        var sut = BuildSut(new Dictionary<string, string>
        {
            ["GEMINI_API_KEY"] = "g-key",
            ["INPUT_MAX_PARALLEL_REQUESTS"] = "not-a-number",
        });

        var act = sut.Create;

        act.Should().NotThrow();
    }
}
