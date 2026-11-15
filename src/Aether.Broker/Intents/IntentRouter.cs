using Aether.Broker.Catalog;
using Aether.Broker.Registry;
using Aether.Contracts.Errors;
using Aether.Contracts.Fdc3;

namespace Aether.Broker.Intents;

/// <summary>Structured result of resolving a bounded FDC3 intent to an allowlisted target.</summary>
public sealed record IntentResolution(
    bool Resolved,
    string Intent,
    string ContextRef,
    string ContextType,
    string? AppId,
    string? DisplayName,
    string? DenialCode,
    string? DenialMessage)
{
    public static IntentResolution Deny(string intent, string contextRef, string code, string message) =>
        new(false, intent, contextRef, "", null, null, code, message);
}

/// <summary>
/// Bounded FDC3 intent resolution: supported intents map to registered apps that explicitly
/// declare the intent and accept its required context type. Unknown intents, context
/// mismatches and unapproved explicit targets are rejected; a preferred target must itself
/// declare the intent. Deliberately not a general FDC3 desktop agent.
/// </summary>
public sealed class IntentRouter
{
    private readonly AppRegistry _registry;
    private readonly ContextCatalog _contexts;

    public IntentRouter(AppRegistry registry, ContextCatalog contexts)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _contexts = contexts ?? throw new ArgumentNullException(nameof(contexts));
    }

    public IntentResolution Resolve(string? intent, string? contextRef, string? preferredAppId)
    {
        var normalizedIntent = intent?.Trim() ?? "";
        var normalizedContextRef = contextRef?.Trim() ?? "";

        if (!Fdc3Intents.Supported.Contains(normalizedIntent) ||
            !Fdc3Intents.TryGetRequiredContextType(normalizedIntent, out var requiredContextType))
        {
            return IntentResolution.Deny(normalizedIntent, normalizedContextRef,
                AetherErrorCodes.Fdc3IntentUnsupported,
                "The requested intent is not in the bounded supported FDC3 intent subset.");
        }

        if (!_contexts.TryGet(normalizedContextRef, out var context))
        {
            return IntentResolution.Deny(normalizedIntent, normalizedContextRef,
                AetherErrorCodes.BrokerContextNotFound,
                "The referenced context is not present in the trusted catalog.");
        }

        if (!string.Equals(context.ContextType, requiredContextType, StringComparison.Ordinal))
        {
            return IntentResolution.Deny(normalizedIntent, normalizedContextRef,
                AetherErrorCodes.Fdc3IntentContextMismatch,
                $"Intent '{normalizedIntent}' requires a {requiredContextType} context.");
        }

        var candidates = _registry.Ordered
            .Where(app => app.Launchable &&
                          app.Intents.Contains(normalizedIntent) &&
                          app.AllowedContextTypes.Contains(context.ContextType))
            .ToArray();

        if (!string.IsNullOrWhiteSpace(preferredAppId))
        {
            var preferred = candidates.FirstOrDefault(app => string.Equals(app.AppId, preferredAppId, StringComparison.Ordinal));
            if (preferred is null)
            {
                return IntentResolution.Deny(normalizedIntent, normalizedContextRef,
                    AetherErrorCodes.Fdc3IntentTargetNotApproved,
                    "The explicitly requested target app is not approved for this intent and context type.");
            }

            return new IntentResolution(true, normalizedIntent, normalizedContextRef, context.ContextType,
                preferred.AppId, preferred.DisplayName, null, null);
        }

        var target = candidates.FirstOrDefault();
        if (target is null)
        {
            return IntentResolution.Deny(normalizedIntent, normalizedContextRef,
                AetherErrorCodes.Fdc3IntentTargetNotApproved,
                "No registered application declares support for this intent and context type.");
        }

        return new IntentResolution(true, normalizedIntent, normalizedContextRef, context.ContextType,
            target.AppId, target.DisplayName, null, null);
    }
}
