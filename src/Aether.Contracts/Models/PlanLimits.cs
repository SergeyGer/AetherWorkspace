namespace Aether.Contracts.Models;

/// <summary>Bounded plan constraints. Values can be tightened by configuration.</summary>
public static class PlanLimits
{
    /// <summary>Default maximum number of actions in one plan for the MVP.</summary>
    public const int DefaultMaxActions = 5;

    /// <summary>Absolute hard cap; configuration may never exceed it.</summary>
    public const int AbsoluteMaxActions = 12;

    public const int MaxPlanIdLength = 64;
    public const int MaxDescriptionLength = 512;
    public const int MaxAppIdLength = 64;
    public const int MaxContextRefLength = 128;
    public const int MaxStatusTextLength = 256;
    public const int MaxModeLength = 16;
}

/// <summary>Layout names accepted by the deterministic window arranger.</summary>
public static class Layouts
{
    public const string SideBySide = "side-by-side";

    public static readonly IReadOnlySet<string> Supported = new HashSet<string>(StringComparer.Ordinal)
    {
        SideBySide,
    };
}

/// <summary>Allowed <c>SummarizeContext</c> modes for v0.1.</summary>
public static class SummarizeModes
{
    public const string Private = "private";

    public static readonly IReadOnlySet<string> Supported = new HashSet<string>(StringComparer.Ordinal)
    {
        Private,
    };
}
