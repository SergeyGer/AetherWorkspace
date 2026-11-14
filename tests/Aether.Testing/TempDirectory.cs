namespace Aether.Testing;

/// <summary>Creates a unique, disposable directory for a test run.</summary>
public sealed class TempDirectory : IDisposable
{
    public TempDirectory(string? prefix = null)
    {
        var name = (prefix ?? "aether") + "-" + Guid.NewGuid().ToString("n");
        FullPath = Path.Combine(Path.GetTempPath(), "aether-tests", name);
        Directory.CreateDirectory(FullPath);
    }

    public string FullPath { get; }

    public string PathTo(params string[] parts) => Path.Combine([FullPath, .. parts]);

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(FullPath))
            {
                Directory.Delete(FullPath, recursive: true);
            }
        }
        catch (IOException)
        {
            // Best-effort cleanup; test temp directories are outside the repository.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
