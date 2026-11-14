namespace Aether.Broker.Sessions;

/// <summary>Outcome of a registration attempt; failures are structured denials.</summary>
public sealed record SessionRegistrationResult(
    bool Success,
    BrokerSession? Session,
    string? SessionSecret,
    string? DenialCode,
    string? DenialMessage)
{
    public static SessionRegistrationResult Ok(BrokerSession session, string secret) => new(true, session, secret, null, null);

    public static SessionRegistrationResult Deny(string code, string message) => new(false, null, null, code, message);
}
