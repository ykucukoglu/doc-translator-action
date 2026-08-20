namespace DocTranslator.Core.Tests;

internal static class Fixtures
{
    public static string Load(string fileName) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", fileName));
}
