using System.Security.Cryptography;

namespace Aether.Contracts;

/// <summary>Minimal W3C-style 128-bit trace / 64-bit span identifiers.</summary>
public sealed record TraceContext(string TraceId, string SpanId)
{
    public static TraceContext NewRoot() =>
        new(TraceIdGenerator.NewTraceId(), TraceIdGenerator.NewSpanId());

    public TraceContext NewSpan() => this with { SpanId = TraceIdGenerator.NewSpanId() };

    public static bool IsValidTraceId(string? traceId) =>
        !string.IsNullOrEmpty(traceId) && traceId.Length == 32 && traceId.All(Uri.IsHexDigit);

    public static bool IsValidSpanId(string? spanId) =>
        !string.IsNullOrEmpty(spanId) && spanId.Length == 16 && spanId.All(Uri.IsHexDigit);
}

public static class TraceIdGenerator
{
    public static string NewTraceId() => Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();

    public static string NewSpanId() => Convert.ToHexString(RandomNumberGenerator.GetBytes(8)).ToLowerInvariant();
}
