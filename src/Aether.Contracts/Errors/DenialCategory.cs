namespace Aether.Contracts.Errors;

/// <summary>
/// Stable, user-facing denial categories. The shell maps these to human-readable
/// explanations without disclosing hidden policy internals or raw payload data.
/// </summary>
public enum DenialCategory
{
    Unknown = 0,
    InvalidPlan = 1,
    UnsupportedAction = 2,
    UnauthorizedApp = 3,
    CapabilityMissing = 4,
    UnsafeClassification = 5,
    ProviderUnavailable = 6,
    PolicyDenied = 7,
    InputRejected = 8,
    Cancelled = 9,
    InternalError = 10,
}
