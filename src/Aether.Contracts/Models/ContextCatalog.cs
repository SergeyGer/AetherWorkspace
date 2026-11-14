using System.Text.Json;

namespace Aether.Contracts.Models;

/// <summary>
/// Trusted local catalog of typed synthetic contexts. Plans reference contexts by
/// id; the broker resolves the payload from this catalog and validates the
/// declared classification. Untrusted producers never supply payload content.
/// </summary>
public sealed class ContextCatalogDocument
{
    public string SchemaVersion { get; init; } = "";

    public IReadOnlyList<ContextCatalogEntry> Contexts { get; init; } = [];
}

public sealed class ContextCatalogEntry
{
    public string ContextRef { get; init; } = "";

    public string ContextType { get; init; } = "";

    /// <summary>Wire classification name; unknown values are treated as UNKNOWN.</summary>
    public string Classification { get; init; } = "";

    public JsonElement Payload { get; init; }
}
