namespace Aether.Contracts.Models.Contexts;

/// <summary>
/// Bounded FDC3-aligned organization context (subset: type/name/id.LEI). Used for
/// synthetic issuer context propagation in the demonstration; not a full FDC3 catalogue.
/// </summary>
public sealed class OrganizationContext
{
    public const string ContextType = Fdc3.Fdc3ContextTypes.Organization;
    public const string SchemaVersion = "1.0";

    public string Type { get; init; } = ContextType;

    public string Name { get; init; } = "";

    public OrganizationId Id { get; init; } = new();
}

public sealed class OrganizationId
{
    /// <summary>Legal Entity Identifier (synthetic value in fixtures).</summary>
    public string? Lei { get; init; }
}
