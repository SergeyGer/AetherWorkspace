using Aether.Broker.Catalog;
using Aether.Broker.Registry;
using Aether.Contracts;
using Aether.Contracts.Json;
using Aether.Contracts.Models;
using Aether.Execution.Catalog;
using Aether.Contracts.V1;
using ContextCatalogEntry = Aether.Execution.Catalog.ContextCatalogEntry;

namespace Aether.Testing;

/// <summary>Shared trusted test fixtures mirroring the repository demo configuration.</summary>
public static class TestData
{
    public static readonly string[] SampleAppIds = ["sample.watchlist", "sample.risk", "sample.chart"];

    public static string AetherHome => TestRepo.Root;

    public static string FixturesDirectory => TestRepo.PathTo("samples", "fixtures");

    public static string SamplesDllPath =>
        TestRepo.FindProjectOutput("src/Aether.SampleApps.Headless", "Aether.SampleApps.Headless.dll");

    public static string FindDotnetExecutable()
    {
        var dotnetRoot = Environment.GetEnvironmentVariable("DOTNET_ROOT");
        if (!string.IsNullOrWhiteSpace(dotnetRoot))
        {
            var candidate = Path.Combine(dotnetRoot, OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        var pathVariable = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var directory in pathVariable.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(directory, OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException(".NET muxer not found. Set DOTNET_ROOT or add dotnet to PATH.");
    }

    public static IReadOnlyDictionary<string, string> LaunchTokens() => new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["DOTNET"] = FindDotnetExecutable(),
        ["SAMPLES_DLL"] = SamplesDllPath,
        ["AETHER_HOME"] = AetherHome,
        ["FIXTURES_DIR"] = FixturesDirectory,
    };

    public static IReadOnlyList<string> AllowedExecutableRoots() =>
    [
        Path.GetDirectoryName(FindDotnetExecutable())!,
        AetherHome,
    ];

    public static AppManifestDocument LoadManifestDocument() =>
        AetherJson.DeserializeStrict<AppManifestDocument>(
            File.ReadAllText(TestRepo.PathTo("samples", "registry", "apps.json")));

    public static ContextCatalogDocument LoadContextCatalogDocument() =>
        AetherJson.DeserializeStrict<ContextCatalogDocument>(
            File.ReadAllText(TestRepo.PathTo("samples", "contexts", "contexts.json")));

    public static AppRegistry CreateRegistry() => AppRegistry.Load(LoadManifestDocument());

    public static ContextCatalog CreateContextCatalog() => ContextCatalog.Load(LoadContextCatalogDocument(), 16 * 1024);

    public static ActionPlan ReferencePlan() => new()
    {
        SchemaVersion = AetherVersions.ContractSchemaVersion,
        PlanId = "plan-test-reference",
        Steps =
        [
            new ActionPlanStep { Action = ActionKinds.LaunchApp, AppId = "sample.watchlist" },
            new ActionPlanStep { Action = ActionKinds.LaunchApp, AppId = "sample.risk" },
            new ActionPlanStep { Action = ActionKinds.LaunchApp, AppId = "sample.chart" },
            new ActionPlanStep { Action = ActionKinds.PublishContext, ContextRef = "synthetic.instrument.northstar" },
            new ActionPlanStep { Action = ActionKinds.ArrangeWindows, Layout = Layouts.SideBySide },
            new ActionPlanStep { Action = ActionKinds.SummarizeContext, ContextRef = "synthetic.exposure.northstar", Mode = SummarizeModes.Private },
        ],
    };

    /// <summary>Execution catalog derived from the same trusted demo registry/contexts.</summary>
    public static PlanCatalog CreatePlanCatalog()
    {
        var registry = CreateRegistry();
        var contexts = CreateContextCatalog();
        var apps = registry.Ordered.ToDictionary(
            app => app.AppId,
            app => new AppCatalogEntry(
                app.AppId,
                app.DisplayName,
                app.Launchable,
                app.Capabilities,
                app.AllowedChannels,
                app.AllowedContextTypes,
                app.AllowedClassifications),
            StringComparer.Ordinal);
        var contextEntries = contexts.ContextRefs.ToDictionary(
            contextRef =>
            {
                contexts.TryGet(contextRef, out var entry);
                return contextRef;
            },
            contextRef =>
            {
                contexts.TryGet(contextRef, out var entry);
                return new ContextCatalogEntry(entry.ContextRef, entry.ContextType, entry.Classification);
            },
            StringComparer.Ordinal);
        return new PlanCatalog(apps, contextEntries);
    }

    public static CapabilityGrant OperatorGrant() => new(
        "sess-test-operator",
        "aether.shell",
        new HashSet<string>(StringComparer.Ordinal)
        {
            "plan.execute", "broker.launch", "broker.arrange", "context.publish", "context.read", "window.report",
        },
        new HashSet<string>(StringComparer.Ordinal) { ChannelNames.Default },
        new HashSet<string>(StringComparer.Ordinal) { "fdc3.instrument", "aether.exposure" },
        new HashSet<DataClassification> { DataClassification.Public, DataClassification.Internal, DataClassification.Restricted });

    public static Aether.Security.Policy.PolicyDocument DefaultPolicy() =>
        Aether.Security.Policy.PolicyDocument.CreateDefault().EnsureValid();

    public static Aether.Contracts.V1.RegisterReply CreateOperatorRegistrationReply(
        string sessionId = "sess-test-operator",
        string secret = "secret-test") => new()
    {
        SessionId = sessionId,
        SessionSecret = secret,
        ExpiresAtUnixMs = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeMilliseconds(),
        Capabilities =
        {
            "plan.execute", "broker.launch", "broker.arrange", "context.publish", "context.read", "window.report",
        },
        AllowedChannels = { ChannelNames.Default },
        AllowedContextTypes = { "fdc3.instrument", "aether.exposure" },
        AllowedClassifications = { Classification.Public, Classification.Internal, Classification.Restricted },
        MaxClassification = Classification.Restricted,
        HeartbeatIntervalSeconds = 5,
        BrokerVersion = AetherVersions.BrokerVersion,
    };

    public static Aether.Contracts.V1.SnapshotReply CreateSnapshotReply()
    {
        var registry = CreateRegistry();
        var contexts = CreateContextCatalog();
        var reply = new Aether.Contracts.V1.SnapshotReply
        {
            BrokerVersion = AetherVersions.BrokerVersion,
            ServerTimeUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            MaxActionsPerPlan = 6,
            PolicyVersion = AetherVersions.PolicyVersion,
        };
        reply.SupportedLayouts.Add(Layouts.SideBySide);
        foreach (var app in registry.Ordered)
        {
            var summary = new Aether.Contracts.V1.AppSummary
            {
                AppId = app.AppId,
                DisplayName = app.DisplayName,
                Launchable = app.Launchable,
            };
            summary.Capabilities.AddRange(app.Capabilities);
            summary.AllowedChannels.AddRange(app.AllowedChannels);
            summary.AllowedContextTypes.AddRange(app.AllowedContextTypes);
            summary.AllowedClassifications.AddRange(app.AllowedClassifications.Select(classification => classification.ToProto()));
            reply.Apps.Add(summary);
        }

        foreach (var contextRef in contexts.ContextRefs.OrderBy(value => value, StringComparer.Ordinal))
        {
            contexts.TryGet(contextRef, out var entry);
            reply.Contexts.Add(new Aether.Contracts.V1.ContextSummary
            {
                ContextRef = entry.ContextRef,
                ContextType = entry.ContextType,
                Classification = entry.Classification.ToProto(),
            });
        }

        return reply;
    }
}
