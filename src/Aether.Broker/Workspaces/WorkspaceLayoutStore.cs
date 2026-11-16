using System.Text.RegularExpressions;
using Aether.Contracts.Errors;
using Aether.Contracts.Json;
using Aether.Contracts.Models.Workspaces;

namespace Aether.Broker.Workspaces;

/// <summary>Structured result of loading a persisted workspace layout.</summary>
public sealed record WorkspaceLayoutLoadResult(
    bool Found,
    WorkspaceLayout? Layout,
    string? Code,
    string? Message);

/// <summary>
/// Local, versioned workspace-layout persistence. One strict JSON file per layout id,
/// written atomically (temp file + replace) inside a trusted configuration directory.
/// </summary>
public sealed partial class WorkspaceLayoutStore
{
    private readonly string _directory;
    private readonly int _maxBytes;

    public WorkspaceLayoutStore(string directory, int maxBytes = 256 * 1024)
    {
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new ArgumentException("A layout store directory is required.", nameof(directory));
        }

        if (maxBytes is < 4096 or > 4 * 1024 * 1024)
        {
            throw new ArgumentException("The layout size limit must be between 4 KiB and 4 MiB.", nameof(maxBytes));
        }

        _directory = Path.GetFullPath(directory);
        _maxBytes = maxBytes;
        Directory.CreateDirectory(_directory);
    }

    public string DirectoryPath => _directory;

    public string GetLayoutPath(string layoutId) => Path.Combine(_directory, ValidateLayoutId(layoutId) + ".json");

    public void Save(WorkspaceLayout layout)
    {
        ArgumentNullException.ThrowIfNull(layout);
        var issues = WorkspaceLayoutValidation.Validate(layout);
        if (issues.Count > 0)
        {
            throw new AetherContractException(AetherErrorCodes.WorkspaceLayoutInvalid,
                string.Join("; ", issues.Select(issue => issue.Message)));
        }

        var path = GetLayoutPath(layout.LayoutId);
        var json = AetherJson.SerializeIndented(layout);
        var size = System.Text.Encoding.UTF8.GetByteCount(json);
        if (size > _maxBytes)
        {
            throw new AetherContractException(AetherErrorCodes.WorkspaceLayoutInvalid,
                $"The workspace layout exceeds the {_maxBytes} byte limit.");
        }

        var temporaryPath = path + "." + Guid.NewGuid().ToString("n") + ".tmp";
        try
        {
            File.WriteAllText(temporaryPath, json);
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    public WorkspaceLayoutLoadResult Load(string layoutId)
    {
        string path;
        try
        {
            path = GetLayoutPath(layoutId);
        }
        catch (AetherContractException exception)
        {
            return new(false, null, exception.Code, exception.Message);
        }

        if (!File.Exists(path))
        {
            return new(false, null, AetherErrorCodes.WorkspaceLayoutNotFound, "No persisted workspace layout exists for this id.");
        }

        try
        {
            var info = new FileInfo(path);
            if (info.Length > _maxBytes)
            {
                return new(false, null, AetherErrorCodes.WorkspaceLayoutInvalid, "The persisted workspace layout exceeds the size limit.");
            }

            var parsed = WorkspaceLayoutParser.Parse(File.ReadAllText(path));
            return parsed.Success
                ? new(true, parsed.Layout, null, null)
                : new(false, null, AetherErrorCodes.WorkspaceLayoutInvalid,
                    string.Join("; ", parsed.Issues.Select(issue => issue.Message)));
        }
        catch (IOException exception)
        {
            return new(false, null, AetherErrorCodes.WorkspaceLayoutInvalid, exception.Message);
        }
    }

    private static string ValidateLayoutId(string layoutId)
    {
        if (string.IsNullOrWhiteSpace(layoutId) ||
            layoutId.EndsWith('.') ||
            !LayoutIdPattern().IsMatch(layoutId))
        {
            throw new AetherContractException(AetherErrorCodes.WorkspaceLayoutInvalid,
                "Layout ids must be 1..64 characters of letters, digits, '.', '_' or '-'.");
        }

        return layoutId;
    }

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._-]{0,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex LayoutIdPattern();
}
