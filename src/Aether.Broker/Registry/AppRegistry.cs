using System.Text.RegularExpressions;
using Aether.Contracts;
using Aether.Contracts.Capabilities;
using Aether.Contracts.Fdc3;
using Aether.Contracts.Models;

namespace Aether.Broker.Registry;

/// <summary>
/// Validated allowlisted application registry. Only apps declared here may be
/// launched; unknown capabilities and malformed manifests reject the whole
/// registry (fail-closed, never partially trusted).
/// </summary>
public sealed partial class AppRegistry
{
    private readonly Dictionary<string, RegisteredApp> _apps;

    private AppRegistry(Dictionary<string, RegisteredApp> apps, IReadOnlyList<RegisteredApp> ordered)
    {
        _apps = apps;
        Ordered = ordered;
    }

    public IReadOnlyList<RegisteredApp> Ordered { get; }

    public IReadOnlyCollection<string> AppIds => _apps.Keys;

    public bool TryGet(string appId, out RegisteredApp app) => _apps.TryGetValue(appId, out app!);

    public RegisteredApp GetRequired(string appId) =>
        _apps.TryGetValue(appId, out var app)
            ? app
            : throw BrokerConfigurationException.Invalid($"App '{appId}' is not registered.");

    public static AppRegistry Load(AppManifestDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (!SchemaVersions.IsSupported(document.ManifestVersion))
        {
            throw BrokerConfigurationException.Invalid(
                $"Unsupported app manifest version '{document.ManifestVersion}'. This build accepts schema major version 1.");
        }

        if (document.Apps.Count == 0)
        {
            throw BrokerConfigurationException.Invalid("The app manifest contains no applications.");
        }

        var apps = new Dictionary<string, RegisteredApp>(StringComparer.Ordinal);
        var ordered = new List<RegisteredApp>();

        for (var index = 0; index < document.Apps.Count; index++)
        {
            var entry = document.Apps[index] ?? throw BrokerConfigurationException.Invalid("The app manifest contains a null entry.");
            ValidateEntry(entry);

            if (!apps.TryAdd(entry.AppId, new RegisteredApp(entry, index)))
            {
                throw BrokerConfigurationException.Invalid($"Duplicate app id '{entry.AppId}' in the app manifest.");
            }

            ordered.Add(apps[entry.AppId]);
        }

        return new AppRegistry(apps, ordered);
    }

    private static void ValidateEntry(AppManifestEntry entry)
    {
        if (string.IsNullOrWhiteSpace(entry.AppId) || !AppIdPattern().IsMatch(entry.AppId))
        {
            throw BrokerConfigurationException.Invalid($"Invalid app id '{entry.AppId}'.");
        }

        if (string.IsNullOrWhiteSpace(entry.DisplayName) || entry.DisplayName.Length > 128)
        {
            throw BrokerConfigurationException.Invalid($"App '{entry.AppId}' requires a display name of at most 128 characters.");
        }

        if (entry.MaxInstances is < 1 or > 16)
        {
            throw BrokerConfigurationException.Invalid($"App '{entry.AppId}' declares an unsupported maxInstances value.");
        }

        if (entry.LaunchTimeoutSeconds is < 1 or > 120)
        {
            throw BrokerConfigurationException.Invalid($"App '{entry.AppId}' declares an unsupported launch timeout.");
        }

        foreach (var capability in entry.Capabilities)
        {
            if (!CapabilityNames.All.Contains(capability, StringComparer.Ordinal))
            {
                throw BrokerConfigurationException.Invalid($"App '{entry.AppId}' declares unknown capability '{capability}'.");
            }
        }

        if (entry.Launchable)
        {
            if (string.IsNullOrWhiteSpace(entry.Executable))
            {
                throw BrokerConfigurationException.Invalid($"Launchable app '{entry.AppId}' requires an executable template.");
            }

            if (entry.Arguments.Count > 32 || entry.Arguments.Any(argument => argument is null || argument.Length > 2048))
            {
                throw BrokerConfigurationException.Invalid($"App '{entry.AppId}' declares unsupported launch arguments.");
            }
        }

        if (entry.AllowedClassifications.Count == 0)
        {
            throw BrokerConfigurationException.Invalid($"App '{entry.AppId}' must declare allowedClassifications.");
        }

        foreach (var classification in entry.AllowedClassifications)
        {
            if (!DataClassificationExtensions.TryParseWire(classification, out _))
            {
                throw BrokerConfigurationException.Invalid(
                    $"App '{entry.AppId}' declares unsupported classification '{classification}'.");
            }
        }

        foreach (var intent in entry.Intents)
        {
            if (!Fdc3Intents.Supported.Contains(intent))
            {
                throw BrokerConfigurationException.Invalid($"App '{entry.AppId}' declares unsupported FDC3 intent '{intent}'.");
            }
        }

        foreach (var channel in entry.AllowedChannels)
        {
            if (!ChannelNamePattern().IsMatch(channel))
            {
                throw BrokerConfigurationException.Invalid($"App '{entry.AppId}' declares unsupported channel '{channel}'.");
            }
        }
    }

    [GeneratedRegex("^[a-z0-9][a-z0-9._-]{0,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex AppIdPattern();

    [GeneratedRegex("^[a-z0-9][a-z0-9._-]{0,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex ChannelNamePattern();
}
