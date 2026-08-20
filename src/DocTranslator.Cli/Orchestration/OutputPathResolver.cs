namespace DocTranslator.Cli.Orchestration;

public interface IOutputPathResolver
{
    string Resolve(string outputPathTemplate, string targetLanguage, string sourceRelativePath);
}

/// <summary>Resolves the <c>docs/{lang}/{relativePath}</c>-style output path template.</summary>
public sealed class OutputPathResolver : IOutputPathResolver
{
    public string Resolve(string outputPathTemplate, string targetLanguage, string sourceRelativePath) =>
        outputPathTemplate
            .Replace("{lang}", targetLanguage)
            .Replace("{relativePath}", sourceRelativePath.Replace('\\', '/'));
}
