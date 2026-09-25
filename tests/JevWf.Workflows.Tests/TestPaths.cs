namespace JevWf.Workflows.Tests;

internal static class TestPaths
{
    public static string Root { get; } = FindRoot();

    public static string Catalog => Path.Combine(Root, "catalog");

    public static string Scenarios => Path.Combine(Root, "scenarios");

    private static string FindRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "JevWf.sln")))
                return dir.FullName;
        }
        throw new DirectoryNotFoundException("Repository root (JevWf.sln) not found.");
    }
}
