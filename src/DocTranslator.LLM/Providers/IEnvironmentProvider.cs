namespace DocTranslator.LLM.Providers;

/// <summary>Testable indirection over environment variable reads, so provider selection can be unit tested without mutating real process env vars.</summary>
public interface IEnvironmentProvider
{
    string? GetEnvironmentVariable(string name);
}

public sealed class EnvironmentProvider : IEnvironmentProvider
{
    public string? GetEnvironmentVariable(string name) => Environment.GetEnvironmentVariable(name);
}
