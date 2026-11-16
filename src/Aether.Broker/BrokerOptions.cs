using Aether.Contracts;
using Aether.Contracts.Models;
using Aether.Broker.Catalog;
using Aether.Broker.Registry;
using Aether.Broker.Launching;
using Aether.Broker.Workspaces;
using Aether.Security.Audit;

namespace Aether.Broker;

/// <summary>
/// Trusted broker configuration. Everything here is local configuration; none of
/// it can be influenced by model output or untrusted app payloads.
/// </summary>
public sealed class BrokerOptions
{
    public required AppRegistry Registry { get; init; }

    public required ContextCatalog Contexts { get; init; }

    /// <summary>Optional explicit transport ("unix-domain-socket" or "named-pipe"); platform default when null.</summary>
    public string? Transport { get; set; }

    /// <summary>Optional explicit endpoint (socket path or pipe name); generated when null.</summary>
    public string? Endpoint { get; set; }

    /// <summary>Safe token substitutions available to executable/argument templates.</summary>
    public IReadOnlyDictionary<string, string> LaunchTokens { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>Executables may only be launched from these canonical roots.</summary>
    public IReadOnlyList<string> AllowedExecutableRoots { get; init; } = [];

    public string? OperatorAppId { get; init; } = "aether.shell";

    /// <summary>Operator token; generated when null. Bound to the shell handshake file.</summary>
    public string? OperatorToken { get; init; }

    public int OperatorTokenMaxUses { get; init; } = 64;

    public int MaxActionsPerPlan { get; init; } = PlanLimits.DefaultMaxActions;

    public IReadOnlyList<string> SupportedLayouts { get; init; } = [Layouts.SideBySide];

    public int SessionTtlMinutes { get; init; } = 120;

    public int PendingRegistrationTtlSeconds { get; init; } = 30;

    public int HeartbeatIntervalSeconds { get; init; } = 5;

    public int MissedHeartbeatsBeforeUnresponsive { get; init; } = 3;

    public int MaxPayloadBytes { get; init; } = 16 * 1024;

    public int ChannelQueueCapacity { get; init; } = 64;

    public int MaxWindows { get; init; } = 16;

    public int MaxTitleLength { get; init; } = 128;

    public int WorkAreaWidth { get; init; } = 1920;

    public int WorkAreaHeight { get; init; } = 1080;

    public string MonitorId { get; init; } = "virtual-monitor-1";

    public int WindowGapPixels { get; init; } = 8;

    public int MaxRestartAttempts { get; init; } = 3;

    /// <summary>
    /// When true, LaunchApp waits until the launched app has an active channel
    /// subscription or a reported window, so the reference workflow never
    /// publishes context before subscribers are ready.
    /// </summary>
    public bool RequireAppReadiness { get; init; } = true;

    public int AppReadinessTimeoutSeconds { get; init; } = 20;

    /// <summary>Optional directory where child app stdout lines are captured for diagnostics/tests.</summary>
    public string? ChildLogDirectory { get; init; }

    public bool EnableWin32WindowArrangement { get; init; } = OperatingSystem.IsWindows();

    public string? AuditFile { get; init; }

    /// <summary>Optional directory for durable workspace layout files (v0.5). Null disables save/restore.</summary>
    public string? LayoutStoreDirectory { get; init; }

    /// <summary>Maximum serialized workspace layout size in bytes.</summary>
    public int LayoutMaxBytes { get; init; } = 256 * 1024;

    /// <summary>Test seam for display topology; Windows/headless defaults are used when null.</summary>
    public IDisplayTopologyProvider? DisplayTopology { get; init; }

    /// <summary>When greater than zero, the audit JSONL rotates at this size (min 4096).</summary>
    public long AuditMaxFileBytes { get; init; }

    public int AuditMaxFiles { get; init; } = 4;

    public IAuditSink? AuditSink { get; init; }

    public TimeProvider TimeProvider { get; init; } = TimeProvider.System;

    /// <summary>Test seam for process identity verification; production uses the OS resolver.</summary>
    public IProcessIdentityResolver? ProcessIdentityResolver { get; init; }

    public string BrokerVersion { get; init; } = AetherVersions.BrokerVersion;

    public string PolicyVersion { get; init; } = AetherVersions.PolicyVersion;

    public BrokerOptions Validate()
    {
        if (MaxActionsPerPlan is < 1 or > PlanLimits.AbsoluteMaxActions)
        {
            throw BrokerConfigurationException.Invalid(
                $"MaxActionsPerPlan must be between 1 and {PlanLimits.AbsoluteMaxActions}.");
        }

        if (SupportedLayouts.Count == 0 || SupportedLayouts.Any(layout => !Layouts.Supported.Contains(layout)))
        {
            throw BrokerConfigurationException.Invalid("SupportedLayouts must be a non-empty subset of the supported layouts.");
        }

        if (SessionTtlMinutes is < 1 or > 24 * 60)
        {
            throw BrokerConfigurationException.Invalid("SessionTtlMinutes is out of the supported range.");
        }

        if (PendingRegistrationTtlSeconds is < 5 or > 300)
        {
            throw BrokerConfigurationException.Invalid("PendingRegistrationTtlSeconds is out of the supported range.");
        }

        if (HeartbeatIntervalSeconds is < 1 or > 120)
        {
            throw BrokerConfigurationException.Invalid("HeartbeatIntervalSeconds is out of the supported range.");
        }

        if (ChannelQueueCapacity is < 1 or > 4096)
        {
            throw BrokerConfigurationException.Invalid("ChannelQueueCapacity is out of the supported range.");
        }

        if (AuditMaxFileBytes > 0 && AuditMaxFileBytes < 4096)
        {
            throw BrokerConfigurationException.Invalid("AuditMaxFileBytes must be 0 (disabled) or at least 4096.");
        }

        if (AuditMaxFiles is < 1 or > 100)
        {
            throw BrokerConfigurationException.Invalid("AuditMaxFiles must be between 1 and 100.");
        }

        if (MaxPayloadBytes is < 1024 or > 1024 * 1024)
        {
            throw BrokerConfigurationException.Invalid("MaxPayloadBytes is out of the supported range.");
        }

        if (WorkAreaWidth is < 640 or > 16384 || WorkAreaHeight is < 480 or > 16384)
        {
            throw BrokerConfigurationException.Invalid("Work area dimensions are out of the supported range.");
        }

        if (OperatorAppId is not null)
        {
            if (!Registry.TryGet(OperatorAppId, out var operatorApp))
            {
                throw BrokerConfigurationException.Invalid(
                    $"Operator app '{OperatorAppId}' must be declared in the app registry.");
            }

            if (operatorApp.Launchable)
            {
                throw BrokerConfigurationException.Invalid(
                    $"Operator app '{OperatorAppId}' must be declared as non-launchable.");
            }
        }

        return this;
    }
}
