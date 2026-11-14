namespace Aether.Contracts.Models;

/// <summary>
/// Versioned, untrusted plan proposal. A plan is only a declaration of intent:
/// it is schema-validated, authorized and executed by the deterministic executor
/// and never interpreted directly. Steps are intentionally flat (no recursion).
/// </summary>
public sealed class ActionPlan
{
    public string SchemaVersion { get; init; } = "";

    public string PlanId { get; init; } = "";

    public string? Description { get; init; }

    public IReadOnlyList<ActionPlanStep> Steps { get; init; } = [];
}

/// <summary>
/// One bounded plan step. Fields are typed; the executor rejects fields that do
/// not belong to the declared action, so no executable paths, shell commands or
/// arbitrary URLs can be smuggled through the plan.
/// </summary>
public sealed class ActionPlanStep
{
    public string Action { get; init; } = "";

    public string? AppId { get; init; }

    public string? ContextRef { get; init; }

    public string? Layout { get; init; }

    public string? Mode { get; init; }

    public string? StatusText { get; init; }
}

/// <summary>The five v0.1 operations the executor understands.</summary>
public enum ActionKind
{
    LaunchApp = 0,
    PublishContext = 1,
    ArrangeWindows = 2,
    SummarizeContext = 3,
    ShowStatus = 4,
}

public static class ActionKinds
{
    public const string LaunchApp = nameof(LaunchApp);
    public const string PublishContext = nameof(PublishContext);
    public const string ArrangeWindows = nameof(ArrangeWindows);
    public const string SummarizeContext = nameof(SummarizeContext);
    public const string ShowStatus = nameof(ShowStatus);

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        LaunchApp,
        PublishContext,
        ArrangeWindows,
        SummarizeContext,
        ShowStatus,
    };

    public static bool TryParse(string? value, out ActionKind kind)
    {
        switch (value)
        {
            case LaunchApp:
                kind = ActionKind.LaunchApp;
                return true;
            case PublishContext:
                kind = ActionKind.PublishContext;
                return true;
            case ArrangeWindows:
                kind = ActionKind.ArrangeWindows;
                return true;
            case SummarizeContext:
                kind = ActionKind.SummarizeContext;
                return true;
            case ShowStatus:
                kind = ActionKind.ShowStatus;
                return true;
            default:
                kind = default;
                return false;
        }
    }

    public static string ToWireName(this ActionKind kind) => kind switch
    {
        ActionKind.LaunchApp => LaunchApp,
        ActionKind.PublishContext => PublishContext,
        ActionKind.ArrangeWindows => ArrangeWindows,
        ActionKind.SummarizeContext => SummarizeContext,
        ActionKind.ShowStatus => ShowStatus,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unsupported action kind."),
    };
}
