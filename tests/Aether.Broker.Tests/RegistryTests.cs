using Aether.Broker;
using Aether.Broker.Catalog;
using Aether.Broker.Registry;
using Aether.Contracts;
using Aether.Contracts.Json;
using Aether.Contracts.Models;
using Aether.Testing;
using Xunit;

namespace Aether.Broker.Tests;

public class RegistryTests
{
    [Fact]
    public void RepositoryRegistryLoadsAndContainsTheReferenceApps()
    {
        var registry = TestData.CreateRegistry();

        Assert.Equal(4, registry.Ordered.Count);
        foreach (var appId in TestData.SampleAppIds)
        {
            Assert.True(registry.TryGet(appId, out var app));
            Assert.True(app.Launchable);
            Assert.Contains("context.subscribe", app.Capabilities);
            Assert.Contains("window.report", app.Capabilities);
        }

        Assert.True(registry.TryGet("aether.shell", out var shell));
        Assert.False(shell.Launchable);
        Assert.Contains("plan.execute", shell.Capabilities);
    }

    [Fact]
    public void UnsupportedManifestVersionRejectsTheRegistry()
    {
        var document = TestData.LoadManifestDocument();
        var invalid = new AppManifestDocument { ManifestVersion = "2.0", Apps = document.Apps };
        var exception = Assert.Throws<BrokerConfigurationException>(() => AppRegistry.Load(invalid));
        Assert.Equal(Contracts.Errors.AetherErrorCodes.BrokerRegistryInvalid, exception.Code);
    }

    [Fact]
    public void DuplicateAppIdsRejectTheRegistry()
    {
        var document = TestData.LoadManifestDocument();
        var duplicated = new AppManifestDocument
        {
            ManifestVersion = document.ManifestVersion,
            Apps = [document.Apps[0], document.Apps[0]],
        };

        Assert.Throws<BrokerConfigurationException>(() => AppRegistry.Load(duplicated));
    }

    [Fact]
    public void UnknownCapabilitiesRejectTheRegistry()
    {
        var document = TestData.LoadManifestDocument();
        var entry = Copy(document.Apps[0]);
        entry = new AppManifestEntry
        {
            AppId = entry.AppId,
            DisplayName = entry.DisplayName,
            Executable = entry.Executable,
            Arguments = entry.Arguments,
            Capabilities = ["context.subscribe", "root.shell"],
            AllowedChannels = entry.AllowedChannels,
            AllowedContextTypes = entry.AllowedContextTypes,
            AllowedClassifications = entry.AllowedClassifications,
        };

        Assert.Throws<BrokerConfigurationException>(() => AppRegistry.Load(new AppManifestDocument
        {
            ManifestVersion = document.ManifestVersion,
            Apps = [entry],
        }));
    }

    [Fact]
    public void UnknownClassificationRejectsTheRegistry()
    {
        var document = TestData.LoadManifestDocument();
        var entry = Copy(document.Apps[0]);
        entry = new AppManifestEntry
        {
            AppId = entry.AppId,
            DisplayName = entry.DisplayName,
            Executable = entry.Executable,
            Arguments = entry.Arguments,
            Capabilities = entry.Capabilities,
            AllowedChannels = entry.AllowedChannels,
            AllowedContextTypes = entry.AllowedContextTypes,
            AllowedClassifications = ["TOP_SECRET"],
        };

        Assert.Throws<BrokerConfigurationException>(() => AppRegistry.Load(new AppManifestDocument
        {
            ManifestVersion = document.ManifestVersion,
            Apps = [entry],
        }));
    }

    [Fact]
    public void LaunchableAppWithoutExecutableRejectsTheRegistry()
    {
        var document = TestData.LoadManifestDocument();
        var entry = Copy(document.Apps[0]);
        entry = new AppManifestEntry
        {
            AppId = entry.AppId,
            DisplayName = entry.DisplayName,
            Launchable = true,
            Executable = "",
            Capabilities = entry.Capabilities,
            AllowedChannels = entry.AllowedChannels,
            AllowedContextTypes = entry.AllowedContextTypes,
            AllowedClassifications = entry.AllowedClassifications,
        };

        Assert.Throws<BrokerConfigurationException>(() => AppRegistry.Load(new AppManifestDocument
        {
            ManifestVersion = document.ManifestVersion,
            Apps = [entry],
        }));
    }

    [Fact]
    public void ContextCatalogLoadsTrustedSyntheticContexts()
    {
        var catalog = TestData.CreateContextCatalog();
        Assert.True(catalog.TryGet("synthetic.instrument.northstar", out var instrument));
        Assert.Equal(DataClassification.Restricted, instrument.Classification);
        Assert.Equal("fdc3.instrument", instrument.ContextType);
        Assert.True(catalog.TryGet("synthetic.exposure.northstar", out var exposure));
        Assert.Equal("aether.exposure", exposure.ContextType);
    }

    [Fact]
    public void ContextCatalogRejectsUnknownClassification()
    {
        var document = TestData.LoadContextCatalogDocument();
        var invalid = new ContextCatalogDocument
        {
            SchemaVersion = document.SchemaVersion,
            Contexts =
            [
                new ContextCatalogEntry
                {
                    ContextRef = "synthetic.instrument.bad",
                    ContextType = "fdc3.instrument",
                    Classification = "TOP_SECRET",
                    Payload = document.Contexts[0].Payload,
                },
            ],
        };

        Assert.Throws<BrokerConfigurationException>(() => ContextCatalog.Load(invalid, 16 * 1024));
    }

    [Fact]
    public void ContextCatalogRejectsUnknownClassificationEvenWhenValid()
    {
        var document = TestData.LoadContextCatalogDocument();
        var invalid = new ContextCatalogDocument
        {
            SchemaVersion = document.SchemaVersion,
            Contexts =
            [
                new ContextCatalogEntry
                {
                    ContextRef = "synthetic.instrument.unknown",
                    ContextType = "fdc3.instrument",
                    Classification = "UNKNOWN",
                    Payload = document.Contexts[0].Payload,
                },
            ],
        };

        Assert.Throws<BrokerConfigurationException>(() => ContextCatalog.Load(invalid, 16 * 1024));
    }

    [Fact]
    public void ContextCatalogRejectsPayloadTypeMismatch()
    {
        var document = TestData.LoadContextCatalogDocument();
        var exposurePayload = document.Contexts.Single(context => context.ContextRef == "synthetic.exposure.northstar").Payload;
        var invalid = new ContextCatalogDocument
        {
            SchemaVersion = document.SchemaVersion,
            Contexts =
            [
                new ContextCatalogEntry
                {
                    ContextRef = "synthetic.instrument.mismatch",
                    ContextType = "fdc3.instrument",
                    Classification = "PUBLIC",
                    Payload = exposurePayload,
                },
            ],
        };

        Assert.Throws<BrokerConfigurationException>(() => ContextCatalog.Load(invalid, 16 * 1024));
    }

    [Fact]
    public void ContextCatalogRejectsOversizedPayloads()
    {
        var document = TestData.LoadContextCatalogDocument();
        Assert.Throws<BrokerConfigurationException>(() => ContextCatalog.Load(document, 32));
    }

    [Fact]
    public void BrokerOptionsRequireOperatorAppToBeNonLaunchable()
    {
        var document = TestData.LoadManifestDocument();
        var apps = document.Apps.Where(app => app.AppId != "aether.shell").ToArray();
        var registry = AppRegistry.Load(new AppManifestDocument { ManifestVersion = document.ManifestVersion, Apps = apps });
        var options = new BrokerOptions
        {
            Registry = registry,
            Contexts = TestData.CreateContextCatalog(),
            OperatorAppId = "aether.shell",
        };

        Assert.Throws<BrokerConfigurationException>(() => options.Validate());
    }

    [Fact]
    public void ManifestRoundTripsThroughStrictJson()
    {
        var json = AetherJson.Serialize(TestData.LoadManifestDocument());
        var parsed = AetherJson.DeserializeStrict<AppManifestDocument>(json);
        Assert.Equal(TestData.LoadManifestDocument().Apps.Count, parsed.Apps.Count);
    }

    private static AppManifestEntry Copy(AppManifestEntry entry) => new()
    {
        AppId = entry.AppId,
        DisplayName = entry.DisplayName,
        Launchable = entry.Launchable,
        Executable = entry.Executable,
        Arguments = entry.Arguments,
        WorkingDirectory = entry.WorkingDirectory,
        Capabilities = entry.Capabilities,
        AllowedChannels = entry.AllowedChannels,
        AllowedContextTypes = entry.AllowedContextTypes,
        AllowedClassifications = entry.AllowedClassifications,
        MaxInstances = entry.MaxInstances,
        LaunchTimeoutSeconds = entry.LaunchTimeoutSeconds,
    };
}
