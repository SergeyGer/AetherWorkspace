namespace Aether.Security.Policy;

/// <summary>Kind of generative task subject to policy evaluation.</summary>
public enum AiTaskKind
{
    PlanGeneration = 0,
    Summarization = 1,
}

/// <summary>Provider category. Routing is capability/policy driven, never brand driven.</summary>
public enum AiProviderKind
{
    Local = 0,
    Cloud = 1,
}

public static class PolicyWireNames
{
    public const string Allow = "ALLOW";
    public const string OptIn = "OPT_IN";
    public const string DenyByDefault = "DENY_BY_DEFAULT";
    public const string Deny = "DENY";
    public const string FailClosed = "FAIL_CLOSED";
}
