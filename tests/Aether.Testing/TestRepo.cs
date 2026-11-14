namespace Aether.Testing;

/// <summary>Locates the repository root and build outputs from a test process.</summary>
public static class TestRepo
{
    public static string Root { get; } = FindRoot();

    public static string Configuration { get; } =
        AppContext.BaseDirectory.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}Debug{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
            ? "Debug"
            : "Release";

    public static string PathTo(params string[] parts) => Path.Combine([Root, .. parts]);

    public static string SampleFixturesDirectory => PathTo("samples", "fixtures");

    public static string SchemasDirectory => PathTo("docs", "contracts");

    public static string FindProjectOutput(string relativeProjectDirectory, string assemblyFileName)
    {
        var frameworks = new[] { "net10.0", "net10.0-windows" };
        foreach (var framework in frameworks)
        {
            var candidate = PathTo(relativeProjectDirectory.Replace('/', Path.DirectorySeparatorChar), "bin", Configuration, framework, assemblyFileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException(
            $"Built output for '{assemblyFileName}' under '{relativeProjectDirectory}' was not found. Build the solution first.");
    }

    public static string FindFirstExistingUnder(string relativeProjectDirectory, string searchPattern)
    {
        var root = PathTo(relativeProjectDirectory.Replace('/', Path.DirectorySeparatorChar));
        if (!Directory.Exists(root))
        {
            throw new DirectoryNotFoundException($"Directory '{root}' does not exist.");
        }

        return Directory.EnumerateFiles(root, searchPattern, SearchOption.AllDirectories).First();
    }

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "AetherWorkspace.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate the AetherWorkspace repository root from the test output directory.");
    }
}
