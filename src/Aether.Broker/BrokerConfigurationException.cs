using Aether.Contracts.Errors;

namespace Aether.Broker;

/// <summary>Raised when the trusted broker configuration is invalid (fail closed).</summary>
public sealed class BrokerConfigurationException : Exception
{
    public BrokerConfigurationException(string code, string message)
        : base(message) => Code = code;

    public string Code { get; }

    public static BrokerConfigurationException Invalid(string message) =>
        new(AetherErrorCodes.BrokerRegistryInvalid, message);
}
