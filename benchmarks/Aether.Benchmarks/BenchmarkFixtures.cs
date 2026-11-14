using System.Text.Json;
using Aether.Contracts;
using Aether.Contracts.Models;
using Aether.Contracts.Models.Contexts;
using Aether.Execution.Catalog;
using ContextCatalogEntry = Aether.Execution.Catalog.ContextCatalogEntry;
using Aether.Execution.Validation;
using Aether.Security.Audit;
using Aether.Security.Classification;
using Aether.Security.Policy;

namespace Aether.Benchmarks;

/// <summary>Shared in-memory fixtures for microbenchmarks (synthetic data only).</summary>
internal static class BenchmarkFixtures
{
    public static readonly ActionPlan SamplePlan = new()
    {
        SchemaVersion = AetherVersions.ContractSchemaVersion,
        PlanId = "plan-benchmark-001",
        Steps =
        [
            new ActionPlanStep { Action = ActionKinds.LaunchApp, AppId = "sample.watchlist" },
            new ActionPlanStep { Action = ActionKinds.LaunchApp, AppId = "sample.risk" },
            new ActionPlanStep { Action = ActionKinds.PublishContext, ContextRef = "synthetic.instrument.northstar" },
            new ActionPlanStep { Action = ActionKinds.ArrangeWindows, Layout = Layouts.SideBySide },
            new ActionPlanStep { Action = ActionKinds.SummarizeContext, ContextRef = "synthetic.exposure.northstar", Mode = SummarizeModes.Private },
        ],
    };

    public static readonly PlanCatalog Catalog = new(
        new Dictionary<string, AppCatalogEntry>(StringComparer.Ordinal)
        {
            ["sample.watchlist"] = new("sample.watchlist", "Watchlist", true,
                new HashSet<string> { "context.subscribe", "window.report" }, new HashSet<string> { "default" },
                new HashSet<string> { "fdc3.instrument" }, new HashSet<DataClassification> { DataClassification.Public, DataClassification.Internal, DataClassification.Restricted }),
            ["sample.risk"] = new("sample.risk", "Risk", true,
                new HashSet<string> { "context.subscribe", "window.report" }, new HashSet<string> { "default" },
                new HashSet<string> { "fdc3.instrument" }, new HashSet<DataClassification> { DataClassification.Public, DataClassification.Internal, DataClassification.Restricted }),
        },
        new Dictionary<string, ContextCatalogEntry>(StringComparer.Ordinal)
        {
            ["synthetic.instrument.northstar"] = new("synthetic.instrument.northstar", "fdc3.instrument", DataClassification.Restricted),
            ["synthetic.exposure.northstar"] = new("synthetic.exposure.northstar", "aether.exposure", DataClassification.Restricted),
        });

    public static readonly CapabilityGrant Grant = new(
        "sess-benchmark",
        "aether.shell",
        new HashSet<string> { "plan.execute", "broker.launch", "broker.arrange", "context.publish", "context.read" },
        new HashSet<string> { "default" },
        new HashSet<string> { "fdc3.instrument", "aether.exposure" },
        new HashSet<DataClassification> { DataClassification.Public, DataClassification.Internal, DataClassification.Restricted });

    public static readonly InstrumentContext Instrument = new()
    {
        Type = InstrumentContext.ContextType,
        Name = "Northstar 2032 Bond",
        Id = new InstrumentId { Ticker = "NRST", Isin = "SYN00000NRST2" },
    };

    public static readonly ActionPlanValidator Validator = new();

    public static readonly PolicyEngine PolicyEngine = new(PolicyDocument.CreateDefault().EnsureValid());

    public static readonly HashChainAuditLog AuditLog = new();

    public static string EnvelopeJson => JsonSerializer.Serialize(new
    {
        messageId = Guid.NewGuid().ToString("n"),
        traceId = TraceIdGenerator.NewTraceId(),
        spanId = TraceIdGenerator.NewSpanId(),
        schemaVersion = AetherVersions.ContractSchemaVersion,
        contextType = InstrumentContext.ContextType,
        classification = DataClassification.Restricted.ToWireName(),
        createdAtUtc = DateTimeOffset.UtcNow,
        payload = Instrument,
    });
}
