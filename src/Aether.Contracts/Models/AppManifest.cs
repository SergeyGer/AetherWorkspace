namespace Aether.Contracts.Models;

/// <summary>
/// Allowlisted application registry. The broker may only launch applications
/// described here; the model/plan can reference an app by id but can never
/// supply an executable path or arguments.
/// </summary>
public sealed class AppManifestDocument
{
    public string ManifestVersion { get; init; } = "";

    public IReadOnlyList<AppManifestEntry> Apps { get; init; } = [];
}

public sealed class AppManifestEntry
{
    public string AppId { get; init; } = "";

    public string DisplayName { get; init; } = "";

    /// <summary>False for non-launchable pseudo apps such as the shell operator identity.</summary>
    public bool Launchable { get; init; } = true;

    /// <summary>
    /// Executable path template. Only tokens supplied by the broker at launch time
    /// are expanded; model-generated content can never influence this value.
    /// </summary>
    public string Executable { get; init; } = "";

    public IReadOnlyList<string> Arguments { get; init; } = [];

    public string? WorkingDirectory { get; init; }

    public IReadOnlyList<string> Capabilities { get; init; } = [];

    public IReadOnlyList<string> AllowedChannels { get; init; } = [];

    public IReadOnlyList<string> AllowedContextTypes { get; init; } = [];

    /// <summary>Wire names, for example PUBLIC / INTERNAL / RESTRICTED / UNKNOWN.</summary>
    public IReadOnlyList<string> AllowedClassifications { get; init; } = [];

    public int MaxInstances { get; init; } = 1;

    public int LaunchTimeoutSeconds { get; init; } = 15;
}
