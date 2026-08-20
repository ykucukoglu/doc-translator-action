namespace DocTranslator.Cli.Orchestration;

public interface IOutputPathResolver
{
    string Resolve(string outputPathTemplate, string targetLanguage, string sourceRelativePath);
}

/// <summary>
/// Resolves the output path template. Supports <c>{lang}</c>, <c>{relativePath}</c> (the
/// original defaults), plus <c>{dir}</c>, <c>{filename}</c> (without extension), and <c>{ext}</c>
/// (without the dot) - enough to express both the default <c>docs/{lang}/{relativePath}</c>
/// tree-per-language layout and co-located naming styles used by tools like MkDocs' static-i18n
/// plugin or ad-hoc Docusaurus setups, e.g. <c>{dir}/{filename}.{lang}.{ext}</c> turning
/// <c>docs/guide.md</c> into <c>docs/guide.de.md</c>.
/// </summary>
public sealed class OutputPathResolver : IOutputPathResolver
{
    public string Resolve(string outputPathTemplate, string targetLanguage, string sourceRelativePath)
    {
        var normalizedSourcePath = sourceRelativePath.Replace('\\', '/');
        var lastSlash = normalizedSourcePath.LastIndexOf('/');
        var dir = lastSlash >= 0 ? normalizedSourcePath[..lastSlash] : string.Empty;
        var fileNameWithExt = lastSlash >= 0 ? normalizedSourcePath[(lastSlash + 1)..] : normalizedSourcePath;
        var lastDot = fileNameWithExt.LastIndexOf('.');
        var fileName = lastDot > 0 ? fileNameWithExt[..lastDot] : fileNameWithExt;
        var ext = lastDot > 0 ? fileNameWithExt[(lastDot + 1)..] : string.Empty;

        var resolved = outputPathTemplate
            .Replace("{lang}", targetLanguage)
            .Replace("{relativePath}", normalizedSourcePath)
            .Replace("{dir}", dir)
            .Replace("{filename}", fileName)
            .Replace("{ext}", ext);

        // A root-level source file leaves {dir} empty, which would otherwise produce a leading
        // "/" (or a doubled "//" between two adjacent placeholders) - collapse those away rather
        // than making every template author special-case root files themselves.
        return string.Join('/', resolved.Split('/', StringSplitOptions.RemoveEmptyEntries));
    }
}
