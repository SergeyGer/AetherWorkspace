using Aether.Contracts;
using Aether.Contracts.Errors;
using Aether.Contracts.Json;
using Aether.Contracts.Models;
using Aether.Contracts.Models.Contexts;
using Aether.Contracts.V1;

namespace Aether.Broker.Catalog;

/// <summary>Validated typed context from the trusted local catalog.</summary>
public sealed class ResolvedContext
{
    public ResolvedContext(string contextRef, string contextType, DataClassification classification, string payloadJson, int payloadBytes)
    {
        ContextRef = contextRef;
        ContextType = contextType;
        Classification = classification;
        PayloadJson = payloadJson;
        PayloadBytes = payloadBytes;
    }

    public string ContextRef { get; }

    public string ContextType { get; }

    public DataClassification Classification { get; }

    public string PayloadJson { get; }

    public int PayloadBytes { get; }

    public ContextSummary ToSummary() => new()
    {
        ContextRef = ContextRef,
        ContextType = ContextType,
        Classification = Classification.ToProto(),
    };
}

/// <summary>Trusted context catalog; plans may only reference ids listed here.</summary>
public sealed class ContextCatalog
{
    private readonly Dictionary<string, ResolvedContext> _contexts;

    private ContextCatalog(Dictionary<string, ResolvedContext> contexts) => _contexts = contexts;

    public IReadOnlyCollection<string> ContextRefs => _contexts.Keys;

    public bool TryGet(string contextRef, out ResolvedContext context) => _contexts.TryGetValue(contextRef, out context!);

    public static ContextCatalog Load(ContextCatalogDocument document, int maxPayloadBytes)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (!SchemaVersions.IsSupported(document.SchemaVersion))
        {
            throw BrokerConfigurationException.Invalid($"Unsupported context catalog version '{document.SchemaVersion}'.");
        }

        var contexts = new Dictionary<string, ResolvedContext>(StringComparer.Ordinal);
        foreach (var entry in document.Contexts)
        {
            if (entry is null || string.IsNullOrWhiteSpace(entry.ContextRef) || !ContextRefPattern().IsMatch(entry.ContextRef))
            {
                throw BrokerConfigurationException.Invalid("The context catalog contains an invalid context reference.");
            }

            if (!DataClassificationExtensions.TryParseWire(entry.Classification, out var classification))
            {
                throw BrokerConfigurationException.Invalid(
                    $"Context '{entry.ContextRef}' declares unsupported classification '{entry.Classification}'.");
            }

            if (classification == DataClassification.Unknown)
            {
                throw BrokerConfigurationException.Invalid(
                    $"Context '{entry.ContextRef}' declares UNKNOWN classification and cannot be published.");
            }

            var payloadJson = entry.Payload.GetRawText();
            var payloadBytes = System.Text.Encoding.UTF8.GetByteCount(payloadJson);
            if (payloadBytes > maxPayloadBytes)
            {
                throw BrokerConfigurationException.Invalid($"Context '{entry.ContextRef}' exceeds the maximum payload size.");
            }

            ValidatePayload(entry.ContextRef, entry.ContextType, payloadJson);

            if (!contexts.TryAdd(entry.ContextRef, new ResolvedContext(
                    entry.ContextRef,
                    entry.ContextType,
                    classification,
                    payloadJson,
                    payloadBytes)))
            {
                throw BrokerConfigurationException.Invalid($"Duplicate context reference '{entry.ContextRef}'.");
            }
        }

        if (contexts.Count == 0)
        {
            throw BrokerConfigurationException.Invalid("The context catalog contains no contexts.");
        }

        return new ContextCatalog(contexts);
    }

    private static void ValidatePayload(string contextRef, string contextType, string payloadJson)
    {
        try
        {
            switch (contextType)
            {
                case InstrumentContext.ContextType:
                {
                    var instrument = AetherJson.DeserializeStrict<InstrumentContext>(payloadJson);
                    if (instrument.Type != InstrumentContext.ContextType)
                    {
                        throw BrokerConfigurationException.Invalid(
                            $"Context '{contextRef}' declares type '{contextType}' but the payload type does not match.");
                    }

                    break;
                }

                case ExposureContext.ContextType:
                {
                    var exposure = AetherJson.DeserializeStrict<ExposureContext>(payloadJson);
                    if (exposure.Type != ExposureContext.ContextType)
                    {
                        throw BrokerConfigurationException.Invalid(
                            $"Context '{contextRef}' declares type '{contextType}' but the payload type does not match.");
                    }

                    break;
                }

                default:
                    throw BrokerConfigurationException.Invalid(
                        $"Context '{contextRef}' declares unsupported context type '{contextType}'.");
            }
        }
        catch (AetherContractException exception)
        {
            throw BrokerConfigurationException.Invalid(
                $"Context '{contextRef}' payload is invalid for type '{contextType}': {exception.Message}");
        }
    }

    private static System.Text.RegularExpressions.Regex ContextRefPattern() =>
        new("^[a-z0-9][a-z0-9._-]{0,127}$", System.Text.RegularExpressions.RegexOptions.CultureInvariant);
}
