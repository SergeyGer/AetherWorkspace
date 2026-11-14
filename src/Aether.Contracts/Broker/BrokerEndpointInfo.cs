using System.Text.Json;
using Aether.Contracts.Json;
using Aether.Contracts.Models;

namespace Aether.Contracts.Broker;

/// <summary>Transport kinds supported by the local broker.</summary>
public static class BrokerTransports
{
    public const string UnixDomainSocket = "unix-domain-socket";
    public const string NamedPipe = "named-pipe";

    public static bool TryParse(string? value, out string transport) =>
        (transport = value switch
        {
            UnixDomainSocket => UnixDomainSocket,
            NamedPipe => NamedPipe,
            _ => "",
        }).Length > 0;
}

/// <summary>
/// Local handshake file written by the broker process. Contains the endpoint,
/// operator identity and the short-lived operator token that the shell uses to
/// register. The file is created with current-user-only permissions; it must
/// never be committed or transmitted.
/// </summary>
public sealed class BrokerEndpointInfo
{
    public string Transport { get; init; } = BrokerTransports.UnixDomainSocket;

    public string Endpoint { get; init; } = "";

    public string OperatorAppId { get; init; } = "aether.shell";

    public string OperatorToken { get; init; } = "";

    public string BrokerVersion { get; init; } = AetherVersions.BrokerVersion;

    public string ProtocolVersion { get; init; } = AetherVersions.ProtocolVersion;

    public string PolicyVersion { get; init; } = AetherVersions.PolicyVersion;

    public string? AuditFile { get; init; }

    public int MaxActionsPerPlan { get; init; } = PlanLimits.DefaultMaxActions;

    public DateTimeOffset WrittenAtUtc { get; init; } = DateTimeOffset.UtcNow;

    public static BrokerEndpointInfo Read(string path)
    {
        var json = File.ReadAllText(path);
        return AetherJson.DeserializeStrict<BrokerEndpointInfo>(json);
    }

    public void Write(string path)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = AetherJson.SerializeIndented(this);
        var temporaryPath = path + ".tmp";
        File.WriteAllText(temporaryPath, json);

        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(temporaryPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }

        File.Move(temporaryPath, path, overwrite: true);
    }
}
