using Aether.Contracts;
using Aether.Contracts.Models;

namespace Aether.Execution.Catalog;

/// <summary>Allowlisted application facts available for plan validation and authorization.</summary>
public sealed record AppCatalogEntry(
    string AppId,
    string DisplayName,
    bool Launchable,
    IReadOnlySet<string> Capabilities,
    IReadOnlySet<string> AllowedChannels,
    IReadOnlySet<string> AllowedContextTypes,
    IReadOnlySet<DataClassification> AllowedClassifications);

/// <summary>Trusted context metadata; payload content is resolved by the broker.</summary>
public sealed record ContextCatalogEntry(string ContextRef, string ContextType, DataClassification Classification);

/// <summary>Snapshot of allowlisted apps and contexts used by the deterministic executor.</summary>
public sealed record PlanCatalog(
    IReadOnlyDictionary<string, AppCatalogEntry> Apps,
    IReadOnlyDictionary<string, ContextCatalogEntry> Contexts)
{
    public static PlanCatalog Empty { get; } = new(
        new Dictionary<string, AppCatalogEntry>(StringComparer.Ordinal),
        new Dictionary<string, ContextCatalogEntry>(StringComparer.Ordinal));
}

/// <summary>Capabilities and resource grants bound to one authenticated session.</summary>
public sealed record CapabilityGrant(
    string SessionId,
    string AppId,
    IReadOnlySet<string> Capabilities,
    IReadOnlySet<string> AllowedChannels,
    IReadOnlySet<string> AllowedContextTypes,
    IReadOnlySet<DataClassification> AllowedClassifications)
{
    public bool HasCapability(string capability) => Capabilities.Contains(capability);

    public static CapabilityGrant Empty { get; } = new(
        "",
        "",
        new HashSet<string>(StringComparer.Ordinal),
        new HashSet<string>(StringComparer.Ordinal),
        new HashSet<string>(StringComparer.Ordinal),
        new HashSet<DataClassification>());
}
