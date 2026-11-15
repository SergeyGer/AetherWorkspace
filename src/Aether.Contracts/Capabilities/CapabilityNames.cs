namespace Aether.Contracts.Capabilities;

/// <summary>
/// Stable capability identifiers. Capabilities are granted to a broker session
/// from the app manifest and checked on every privileged IPC operation.
/// </summary>
public static class CapabilityNames
{
    /// <summary>Publish typed context to an allowed channel.</summary>
    public const string ContextPublish = "context.publish";

    /// <summary>Subscribe to an allowed channel.</summary>
    public const string ContextSubscribe = "context.subscribe";

    /// <summary>Read a resolved context payload from the broker.</summary>
    public const string ContextRead = "context.read";

    /// <summary>Report/refresh a top-level window for arrangement.</summary>
    public const string WindowReport = "window.report";

    /// <summary>Ask the broker to launch a registered sample app.</summary>
    public const string BrokerLaunch = "broker.launch";

    /// <summary>Ask the broker to arrange reported top-level windows.</summary>
    public const string BrokerArrange = "broker.arrange";

    /// <summary>Submit a validated plan for deterministic execution.</summary>
    public const string PlanExecute = "plan.execute";

    /// <summary>Resolve a bounded FDC3 intent to a registered target app.</summary>
    public const string IntentResolve = "intent.resolve";

    public static readonly IReadOnlyList<string> All =
    [
        ContextPublish,
        ContextSubscribe,
        ContextRead,
        WindowReport,
        BrokerLaunch,
        BrokerArrange,
        PlanExecute,
        IntentResolve,
    ];
}
