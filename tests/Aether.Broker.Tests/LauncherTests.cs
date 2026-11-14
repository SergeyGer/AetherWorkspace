using Aether.Broker;
using Aether.Broker.Launching;
using Aether.Contracts;
using Aether.Contracts.Broker;
using Aether.Contracts.Errors;
using Aether.Contracts.Models;
using Aether.Testing;
using Xunit;

namespace Aether.Broker.Tests;

public class LauncherTests
{
    [Fact]
    public async Task AuthorizedSampleAppLaunchesRegistersAndBecomesReady()
    {
        await using var context = BrokerTestContext.Create(testName: "launch");
        await context.StartAsync();
        await using var operatorClient = await context.RegisterOperatorAsync();

        var reply = await operatorClient.LaunchAppAsync("sample.watchlist", TraceIdGenerator.NewTraceId());

        Assert.True(reply.Accepted, reply.DenialCode + ": " + reply.DenialMessage);
        Assert.True(reply.Registered);
        Assert.True(reply.ProcessId > 0);
        Assert.Single(context.Host.Launcher.Snapshot());
        Assert.True(context.Host.Channels.SubscriberCountForInstance(reply.InstanceId) > 0);
    }

    [Fact]
    public async Task LaunchingTheSameAppTwiceReusesTheRunningInstance()
    {
        await using var context = BrokerTestContext.Create(testName: "launch-reuse");
        await context.StartAsync();
        await using var operatorClient = await context.RegisterOperatorAsync();

        var first = await operatorClient.LaunchAppAsync("sample.watchlist", TraceIdGenerator.NewTraceId());
        var second = await operatorClient.LaunchAppAsync("sample.watchlist", TraceIdGenerator.NewTraceId());

        Assert.True(first.Accepted);
        Assert.True(second.Accepted);
        Assert.Equal(first.InstanceId, second.InstanceId);
        Assert.Single(context.Host.Launcher.Snapshot());
    }

    [Fact]
    public async Task UnknownAppLaunchIsDenied()
    {
        await using var context = BrokerTestContext.Create(testName: "launch-unknown");
        await context.StartAsync();
        await using var operatorClient = await context.RegisterOperatorAsync();

        var reply = await operatorClient.LaunchAppAsync("sample.evil", TraceIdGenerator.NewTraceId());

        Assert.False(reply.Accepted);
        Assert.Equal(AetherErrorCodes.BrokerAppUnknown, reply.DenialCode);
    }

    [Fact]
    public async Task UnallowlistedExecutableIsDeniedBeforeProcessStart()
    {
        var manifest = new AppManifestDocument
        {
            ManifestVersion = "1.0",
            Apps =
            [
                new AppManifestEntry
                {
                    AppId = "test.evil",
                    DisplayName = "Unapproved executable",
                    Executable = "/bin/sh",
                    Arguments = ["-c", "echo pwned"],
                    Capabilities = ["context.subscribe"],
                    AllowedChannels = ["default"],
                    AllowedContextTypes = ["fdc3.instrument"],
                    AllowedClassifications = ["PUBLIC"],
                },
                new AppManifestEntry
                {
                    AppId = "aether.shell",
                    DisplayName = "Shell",
                    Launchable = false,
                    Capabilities = ["plan.execute", "broker.launch", "broker.arrange", "context.publish", "context.read"],
                    AllowedChannels = ["default"],
                    AllowedContextTypes = ["fdc3.instrument"],
                    AllowedClassifications = ["PUBLIC", "INTERNAL", "RESTRICTED"],
                },
            ],
        };
        var registry = Aether.Broker.Registry.AppRegistry.Load(manifest);
        var options = new BrokerOptions
        {
            Registry = registry,
            Contexts = TestData.CreateContextCatalog(),
            Transport = BrokerTransports.UnixDomainSocket,
            AllowedExecutableRoots = [TestRepo.Root],
            OperatorAppId = "aether.shell",
        };

        await using var context = BrokerTestContext.CreateWith(options);
        await context.StartAsync();
        await using var operatorClient = await context.RegisterOperatorAsync();

        var reply = await operatorClient.LaunchAppAsync("test.evil", TraceIdGenerator.NewTraceId());

        Assert.False(reply.Accepted);
        Assert.Equal(AetherErrorCodes.BrokerExecutableDenied, reply.DenialCode);
        Assert.Empty(context.Host.Launcher.Snapshot());
    }

    [Fact]
    public async Task PathTraversalExecutableTemplateIsDenied()
    {
        var manifest = TestData.LoadManifestDocument();
        var watchlist = manifest.Apps.Single(app => app.AppId == "sample.watchlist");
        var traversal = new AppManifestEntry
        {
            AppId = "sample.watchlist",
            DisplayName = watchlist.DisplayName,
            Executable = "../bin/sh",
            Arguments = [],
            Capabilities = watchlist.Capabilities,
            AllowedChannels = watchlist.AllowedChannels,
            AllowedContextTypes = watchlist.AllowedContextTypes,
            AllowedClassifications = watchlist.AllowedClassifications,
        };
        var apps = manifest.Apps.Where(app => app.AppId != "sample.watchlist").Append(traversal).ToArray();
        var registry = Aether.Broker.Registry.AppRegistry.Load(new AppManifestDocument { ManifestVersion = manifest.ManifestVersion, Apps = apps });
        var options = new BrokerOptions
        {
            Registry = registry,
            Contexts = TestData.CreateContextCatalog(),
            Transport = BrokerTransports.UnixDomainSocket,
            LaunchTokens = TestData.LaunchTokens(),
            AllowedExecutableRoots = TestData.AllowedExecutableRoots(),
            OperatorAppId = "aether.shell",
        };

        await using var context = BrokerTestContext.CreateWith(options);
        await context.StartAsync();
        await using var operatorClient = await context.RegisterOperatorAsync();

        var reply = await operatorClient.LaunchAppAsync("sample.watchlist", TraceIdGenerator.NewTraceId());

        Assert.False(reply.Accepted);
        Assert.Equal(AetherErrorCodes.BrokerExecutableDenied, reply.DenialCode);
    }

    [Fact]
    public async Task StoppingALaunchedAppRevokesItsSession()
    {
        await using var context = BrokerTestContext.Create(testName: "stop");
        await context.StartAsync();
        await using var operatorClient = await context.RegisterOperatorAsync();

        var launched = await operatorClient.LaunchAppAsync("sample.chart", TraceIdGenerator.NewTraceId());
        Assert.True(launched.Accepted);

        var stopped = await operatorClient.StopAppAsync("sample.chart", launched.InstanceId, TraceIdGenerator.NewTraceId());

        Assert.True(stopped.Stopped);
        Assert.Empty(context.Host.Launcher.Snapshot());
        Assert.Null(context.Host.Sessions.GetActiveInstance("sample.chart"));
    }
}
