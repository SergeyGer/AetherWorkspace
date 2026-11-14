namespace Aether.Contracts;

/// <summary>Pinned product and protocol versions used across components.</summary>
public static class AetherVersions
{
    public const string BrokerVersion = "0.1.0";
    public const string ProtocolVersion = "1";
    public const string ContractSchemaVersion = "1.0";
    public const string PolicyVersion = "3.0";
    public const int ProtocolVersionMajor = 1;
}

public static class SchemaVersions
{
    public const int SupportedMajor = 1;

    public static bool IsSupported(string? schemaVersion) =>
        !string.IsNullOrWhiteSpace(schemaVersion) &&
        Version.TryParse(schemaVersion, out var parsed) &&
        parsed.Major == SupportedMajor;
}
