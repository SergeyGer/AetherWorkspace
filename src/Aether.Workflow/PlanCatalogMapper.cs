using Aether.Contracts;
using Aether.Contracts.V1;
using Aether.Execution.Catalog;

namespace Aether.Workflow;

/// <summary>Maps broker snapshot/registration data into execution-side catalog and grants.</summary>
public static class PlanCatalogMapper
{
    public static PlanCatalog FromSnapshot(SnapshotReply snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var apps = new Dictionary<string, AppCatalogEntry>(StringComparer.Ordinal);
        foreach (var app in snapshot.Apps)
        {
            apps[app.AppId] = new AppCatalogEntry(
                app.AppId,
                app.DisplayName,
                app.Launchable,
                new HashSet<string>(app.Capabilities, StringComparer.Ordinal),
                new HashSet<string>(app.AllowedChannels, StringComparer.Ordinal),
                new HashSet<string>(app.AllowedContextTypes, StringComparer.Ordinal),
                new HashSet<DataClassification>(app.AllowedClassifications.Select(classification => classification.FromProto())));
        }

        var contexts = new Dictionary<string, ContextCatalogEntry>(StringComparer.Ordinal);
        foreach (var context in snapshot.Contexts)
        {
            contexts[context.ContextRef] = new ContextCatalogEntry(
                context.ContextRef,
                context.ContextType,
                context.Classification.FromProto());
        }

        return new PlanCatalog(apps, contexts);
    }

    public static CapabilityGrant FromRegistration(RegisterReply registration)
    {
        ArgumentNullException.ThrowIfNull(registration);

        return new CapabilityGrant(
            registration.SessionId,
            registration.SessionId,
            new HashSet<string>(registration.Capabilities, StringComparer.Ordinal),
            new HashSet<string>(registration.AllowedChannels, StringComparer.Ordinal),
            new HashSet<string>(registration.AllowedContextTypes, StringComparer.Ordinal),
            new HashSet<DataClassification>(registration.AllowedClassifications.Select(classification => classification.FromProto())));
    }
}
