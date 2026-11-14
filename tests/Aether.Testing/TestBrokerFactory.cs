using Aether.Broker;
using Aether.Broker.Launching;
using Aether.Contracts.Broker;

namespace Aether.Testing;

/// <summary>Builds broker options for in-process integration tests.</summary>
public static class TestBrokerFactory
{
    public static BrokerOptions CreateOptions(
        string socketPath,
        string? auditFile = null,
        string? childLogDirectory = null,
        IProcessIdentityResolver? identityResolver = null,
        TimeProvider? timeProvider = null,
        int heartbeatIntervalSeconds = 5,
        int missedHeartbeatsBeforeUnresponsive = 3) => new()
    {
        Registry = TestData.CreateRegistry(),
        Contexts = TestData.CreateContextCatalog(),
        Transport = BrokerTransports.UnixDomainSocket,
        Endpoint = socketPath,
        LaunchTokens = TestData.LaunchTokens(),
        AllowedExecutableRoots = TestData.AllowedExecutableRoots(),
        OperatorAppId = "aether.shell",
        MaxActionsPerPlan = 6,
        AuditFile = auditFile,
        ChildLogDirectory = childLogDirectory,
        TimeProvider = timeProvider ?? TimeProvider.System,
        ProcessIdentityResolver = identityResolver,
        HeartbeatIntervalSeconds = heartbeatIntervalSeconds,
        MissedHeartbeatsBeforeUnresponsive = missedHeartbeatsBeforeUnresponsive,
    };
}
