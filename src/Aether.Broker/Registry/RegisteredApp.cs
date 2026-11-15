using Aether.Contracts;
using Aether.Contracts.Capabilities;
using Aether.Contracts.Models;

namespace Aether.Broker.Registry;

/// <summary>Validated, normalized allowlist entry used at runtime.</summary>
public sealed class RegisteredApp
{
    public RegisteredApp(AppManifestEntry entry, int order)
    {
        Entry = entry;
        Order = order;
        Capabilities = new HashSet<string>(entry.Capabilities, StringComparer.Ordinal);
        AllowedChannels = new HashSet<string>(entry.AllowedChannels, StringComparer.Ordinal);
        AllowedContextTypes = new HashSet<string>(entry.AllowedContextTypes, StringComparer.Ordinal);
        AllowedClassifications = new HashSet<DataClassification>(entry.AllowedClassifications.Select(DataClassificationExtensions.ParseOrUnknown));
        Intents = new HashSet<string>(entry.Intents, StringComparer.Ordinal);
    }

    public AppManifestEntry Entry { get; }

    public int Order { get; }

    public string AppId => Entry.AppId;

    public string DisplayName => Entry.DisplayName;

    public bool Launchable => Entry.Launchable;

    public IReadOnlySet<string> Capabilities { get; }

    public IReadOnlySet<string> AllowedChannels { get; }

    public IReadOnlySet<string> AllowedContextTypes { get; }

    public IReadOnlySet<DataClassification> AllowedClassifications { get; }

    public IReadOnlySet<string> Intents { get; }

    public DataClassification MaxClassification =>
        AllowedClassifications.Count == 0 ? DataClassification.Unknown : AllowedClassifications.Max();

    public string Executable => Entry.Executable;

    public IReadOnlyList<string> Arguments => Entry.Arguments;

    public string? WorkingDirectory => Entry.WorkingDirectory;

    public int MaxInstances => Entry.MaxInstances;

    public TimeSpan LaunchTimeout => TimeSpan.FromSeconds(Entry.LaunchTimeoutSeconds);
}
