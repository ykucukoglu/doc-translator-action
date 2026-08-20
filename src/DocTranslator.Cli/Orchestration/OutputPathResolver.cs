namespace DocTranslator.Cli.Orchestration;

public interface IOutputPathResolver
{
    string Resolve(string template, string targetLanguage, string sourceRelativePath);
}

/// <summary>Resolves the <c>docs/{lang}/{relativePath}</c>-style output path template.</summary>
public sealed class OutputPathResolver : IOutputPathResolver
{
    public string Resolve(string template, string targetLanguage, string sourceRelativePath) =>
        template
            .Replace("{lang}", targetLanguage)
            .Replace("{relativePath}", sourceRelativePath.Replace('\\', '/'));
}
