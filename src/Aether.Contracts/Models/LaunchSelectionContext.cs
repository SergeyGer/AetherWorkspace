namespace Aether.Contracts.Models;

/// <summary>Launch-scope policy for a workflow, created by the trusted Shell UI and never by the model.</summary>
public enum LaunchSelectionMode
{
    /// <summary>The plan may launch any registered/authorized app; the selection is advisory.</summary>
    Preferred = 0,

    /// <summary>Every LaunchApp step must target an app in the trusted operator selection.</summary>
    Strict = 1,
}

/// <summary>
/// Trusted execution context adjacent to (never embedded in) the untrusted ActionPlan. The model
/// cannot forge the operator's selection because this object is supplied out-of-band by the Shell.
/// </summary>
public sealed record LaunchSelectionContext(
    LaunchSelectionMode Mode,
    IReadOnlySet<string> AllowedLaunchAppIds,
    string SelectionRevision)
{
    public static LaunchSelectionContext Preferred(IEnumerable<string> appIds, string revision) =>
        new(LaunchSelectionMode.Preferred, new HashSet<string>(appIds, StringComparer.Ordinal), revision);

    public static LaunchSelectionContext Strict(IEnumerable<string> appIds, string revision) =>
        new(LaunchSelectionMode.Strict, new HashSet<string>(appIds, StringComparer.Ordinal), revision);

    public bool Allows(string? appId) =>
        Mode != LaunchSelectionMode.Strict
        || (!string.IsNullOrWhiteSpace(appId) && AllowedLaunchAppIds.Contains(appId));
}
