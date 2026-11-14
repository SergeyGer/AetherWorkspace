namespace Aether.Contracts.Errors;

/// <summary>Raised when a versioned contract document cannot be accepted.</summary>
public sealed class AetherContractException : Exception
{
    public AetherContractException(string code, string message)
        : base(message) => Code = code;

    public string Code { get; }
}
