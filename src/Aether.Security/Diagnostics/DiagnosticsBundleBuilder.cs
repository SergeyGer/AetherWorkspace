using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Aether.Contracts;
using Aether.Contracts.Errors;
using Aether.Contracts.Json;
using Aether.Contracts.Models.Diagnostics;

namespace Aether.Security.Diagnostics;

/// <summary>
/// Builds and writes sanitized diagnostics bundles. Free-text values are redacted for common
/// synthetic PII/secret shapes; payload content, commands and summaries are never accepted by
/// this API in the first place.
/// </summary>
public static partial class DiagnosticsBundleBuilder
{
    public const string CurrentSchemaVersion = "1.0";
    public const int MaximumBundleBytes = 256 * 1024;

    public static DiagnosticsBundle Build(
        DiagnosticsRun run,
        IReadOnlyList<DiagnosticsTimelineEntry> timeline,
        IReadOnlyList<DiagnosticsApp> apps,
        DiagnosticsAudit audit,
        DiagnosticsProviders providers,
        bool offline,
        DateTimeOffset generatedAtUtc,
        string? appVersion = null)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(timeline);
        ArgumentNullException.ThrowIfNull(apps);
        ArgumentNullException.ThrowIfNull(audit);
        ArgumentNullException.ThrowIfNull(providers);

        return new DiagnosticsBundle(
            CurrentSchemaVersion,
            generatedAtUtc,
            RedactRequired(appVersion),
            RedactRequired(AetherVersions.BrokerVersion),
            RedactRequired(AetherVersions.PolicyVersion),
            new DiagnosticsRun(
                RedactRequired(run.Status),
                RedactRequired(run.TraceId),
                Redact(run.PlanId),
                RedactRequired(run.Classification),
                Redact(run.DenialCode),
                run.DurationMs,
                RedactRequired(run.Planner),
                run.UsedModel),
            timeline.Select(entry => new DiagnosticsTimelineEntry(
                entry.Sequence,
                RedactRequired(entry.Action),
                RedactRequired(entry.State),
                Redact(entry.Code),
                entry.DurationMs)).ToArray(),
            apps.Select(app => new DiagnosticsApp(RedactRequired(app.AppId), RedactRequired(app.Health), app.Windows)).ToArray(),
            new DiagnosticsEnvironment(
                RedactRequired(RuntimeInformation.OSDescription),
                RedactRequired(RuntimeInformation.OSArchitecture.ToString()),
                RedactRequired(RuntimeInformation.FrameworkDescription),
                RedactRequired(RuntimeInformation.ProcessArchitecture.ToString())),
            new DiagnosticsAudit(
                audit.EntryCount,
                audit.ChainValid,
                audit.RotatedFiles,
                audit.FileChainValid,
                audit.EventTypeCounts.ToDictionary(pair => RedactRequired(pair.Key), pair => pair.Value, StringComparer.Ordinal)),
            new DiagnosticsProviders(
                RedactRequired(providers.Mode),
                RedactRequired(providers.RouteKind),
                Redact(providers.RouteReason),
                Redact(providers.Provider),
                Redact(providers.Model),
                providers.CloudEnabled,
                providers.CloudConsentGranted,
                providers.CloudAdapterConfigured,
                providers.LocalModelAdapterConfigured),
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["offline"] = offline ? "true" : "false",
                ["syntheticDataOnly"] = "true",
                ["rawPayloadsIncluded"] = "false",
                ["telemetryUploads"] = "false",
            });
    }

    /// <summary>Writes the bundle to an explicit local path atomically and returns the full path.</summary>
    public static string WriteFile(DiagnosticsBundle bundle, string path)
    {
        ArgumentNullException.ThrowIfNull(bundle);
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new AetherContractException(AetherErrorCodes.DiagnosticsPathInvalid, "A diagnostics export path is required.");
        }

        var fullPath = Path.GetFullPath(path);
        if (!string.Equals(Path.GetExtension(fullPath), ".json", StringComparison.OrdinalIgnoreCase))
        {
            throw new AetherContractException(AetherErrorCodes.DiagnosticsPathInvalid, "The diagnostics export path must end with '.json'.");
        }

        var json = AetherJson.SerializeIndented(bundle);
        var bytes = System.Text.Encoding.UTF8.GetByteCount(json);
        if (bytes > MaximumBundleBytes)
        {
            throw new AetherContractException(AetherErrorCodes.DiagnosticsPathInvalid, $"The diagnostics bundle exceeds {MaximumBundleBytes} bytes.");
        }

        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var temporary = fullPath + "." + Guid.NewGuid().ToString("n") + ".tmp";
        try
        {
            File.WriteAllText(temporary, json);
            File.Move(temporary, fullPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }

        return fullPath;
    }

    private static string RedactRequired(string? value) => Redact(value) ?? "";

    /// <summary>Redacts common synthetic account identifier and secret shapes from free text.</summary>
    public static string? Redact(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        var redacted = AccountPattern().Replace(value, "[REDACTED-ACCOUNT]");
        redacted = SecretPattern().Replace(redacted, "[REDACTED-SECRET]");
        redacted = JwtPattern().Replace(redacted, "[REDACTED-TOKEN]");
        return redacted;
    }

    [GeneratedRegex("ACC-\\d{4,}", RegexOptions.CultureInvariant)]
    private static partial Regex AccountPattern();

    [GeneratedRegex("\\b(sk|rk)-[A-Za-z0-9_\\-]{8,}", RegexOptions.CultureInvariant)]
    private static partial Regex SecretPattern();

    [GeneratedRegex("\\beyJ[A-Za-z0-9_\\-]{10,}\\.[A-Za-z0-9_\\-]{5,}\\.[A-Za-z0-9_\\-]{5,}", RegexOptions.CultureInvariant)]
    private static partial Regex JwtPattern();
}
