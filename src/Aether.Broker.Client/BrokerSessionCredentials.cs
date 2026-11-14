namespace Aether.Broker.Client;

/// <summary>Session credentials returned exactly once at registration.</summary>
public sealed record BrokerSessionCredentials(string SessionId, string SessionSecret);
