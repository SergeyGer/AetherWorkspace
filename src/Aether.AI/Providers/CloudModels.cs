using Aether.Contracts;
using Aether.Security.Policy;

namespace Aether.AI.Providers;

/// <summary>
/// Provider-agnostic outbound request. Business logic never references a model
/// brand or version; provider adapters translate this request into whatever the
/// configured endpoint expects.
/// </summary>
public sealed record CloudModelRequest(
    string TraceId,
    AiTaskKind TaskKind,
    DataClassification Classification,
    string PayloadKind,
    int PayloadBytes,
    string SystemPrompt,
    string UserContent);

/// <summary>Raw provider response. Validation happens outside the adapter.</summary>
public sealed record CloudModelResponse(bool Success, string? Content, string? ErrorCode)
{
    public static CloudModelResponse Ok(string content) => new(true, content, null);

    public static CloudModelResponse Fail(string errorCode) => new(false, null, errorCode);
}

/// <summary>
/// Outbound transport for one provider. Implementations must not be called
/// directly by business logic: every external attempt goes through the egress
/// gateway so that policy denial can be enforced and observed.
/// </summary>
public interface ICloudModelTransport
{
    string ProviderId { get; }

    Task<CloudModelResponse> SendAsync(CloudModelRequest request, CancellationToken cancellationToken);
}

/// <summary>Loopback local model transport (for example an Ollama-style local endpoint).</summary>
public interface ILocalModelTransport
{
    string ProviderId { get; }

    Task<CloudModelResponse> SendAsync(CloudModelRequest request, CancellationToken cancellationToken);
}

public sealed record PlanProviderOptions(bool CloudEnabled, bool CloudConsentGranted)
{
    public static PlanProviderOptions Disabled { get; } = new(false, false);
}
