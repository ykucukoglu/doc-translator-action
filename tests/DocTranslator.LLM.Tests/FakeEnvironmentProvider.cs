using DocTranslator.LLM.Providers;

namespace DocTranslator.LLM.Tests;

internal sealed class FakeEnvironmentProvider(IReadOnlyDictionary<string, string> values) : IEnvironmentProvider
{
    public string? GetEnvironmentVariable(string name) => values.GetValueOrDefault(name);
}
