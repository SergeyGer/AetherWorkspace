namespace Aether.Security.Consent;

/// <summary>
/// Explicit opt-in cloud state. Cloud is disabled unless the user enabled it in
/// this session and granted consent; a local JSON flag cannot establish remote
/// provider guarantees.
/// </summary>
public sealed record ConsentState(bool CloudEnabled, bool CloudConsentGranted, string? ConsentVersion, DateTimeOffset? GrantedAtUtc)
{
    public static readonly ConsentState Disabled = new(false, false, null, null);

    public bool IsCloudUsable => CloudEnabled && CloudConsentGranted;

    public static ConsentState Grant(string consentVersion, DateTimeOffset grantedAtUtc) =>
        new(true, true, string.IsNullOrWhiteSpace(consentVersion) ? "v1" : consentVersion, grantedAtUtc);
}
