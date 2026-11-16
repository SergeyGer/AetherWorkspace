using Aether.Broker;
using Aether.Broker.Client;
using Aether.Contracts;
using Aether.Contracts.Errors;
using Aether.Contracts.Json;
using Aether.Contracts.Models.Workspaces;
using Aether.Testing;
using Xunit;

namespace Aether.Broker.Tests;

public class WorkspaceLayoutRpcTests
{
    private static async Task<(BrokerTestContext Context, BrokerClient Client, string LayoutDirectory)> StartAsync(string name)
    {
        var directory = Path.Combine(Path.GetTempPath(), "aether-layout-" + name + "-" + Guid.NewGuid().ToString("n")[..6]);
        Directory.CreateDirectory(directory);
        var layoutDirectory = Path.Combine(directory, "layouts");
        var options = TestBrokerFactory.CreateOptions(
            Path.Combine(directory, "b.sock"),
            auditFile: Path.Combine(directory, "audit.jsonl"),
            childLogDirectory: Path.Combine(directory, "logs"),
            layoutStoreDirectory: layoutDirectory);
        var context = BrokerTestContext.CreateWith(options);
        await context.StartAsync();
        var client = await context.RegisterOperatorAsync();
        return (context, client, layoutDirectory);
    }

    private static async Task LaunchSampleAppsAsync(BrokerClient client)
    {
        foreach (var appId in TestData.SampleAppIds)
        {
            var reply = await client.LaunchAppAsync(appId, TraceIdGenerator.NewTraceId());
            Assert.True(reply.Accepted, reply.DenialCode + ": " + reply.DenialMessage);
        }

        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (DateTime.UtcNow < deadline)
        {
            var health = await client.HealthProbeAsync();
            if (health.RegisteredWindows >= TestData.SampleAppIds.Length)
            {
                return;
            }

            await Task.Delay(50);
        }

        Assert.Fail("The sample apps did not report their top-level windows in time.");
    }

    [Fact]
    public async Task SaveAndRestoreRoundTripPersistsLayoutAndAppliesPlacements()
    {
        var (context, client, layoutDirectory) = await StartAsync("roundtrip");
        await using var _ = context;
        await using var __ = client;
        await LaunchSampleAppsAsync(client);

        var saved = await client.SaveWorkspaceLayoutAsync("desk-a", "synthetic roundtrip");
        Assert.True(saved.Accepted, saved.DenialCode + ": " + saved.DenialMessage);
        Assert.Equal("desk-a", saved.LayoutId);
        Assert.Contains("\"schemaVersion\":\"1.0\"", saved.LayoutJson, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(layoutDirectory, "desk-a.json")));

        var restored = await client.RestoreWorkspaceLayoutAsync("desk-a");
        Assert.True(restored.Applied, restored.DenialCode + ": " + restored.DenialMessage);
        Assert.Equal(TestData.SampleAppIds.Length, restored.Placements.Count);
        Assert.All(restored.Placements, placement => Assert.Equal("virtual-monitor-1", placement.MonitorDeviceName));
        Assert.All(restored.Placements, placement =>
        {
            Assert.True(placement.X >= 0);
            Assert.True(placement.Y >= 0);
            Assert.True(placement.X + placement.Width <= 1920);
            Assert.True(placement.Y + placement.Height <= 1080);
        });
    }

    [Fact]
    public async Task RestoreFallsBackToPrimaryWhenSavedMonitorDisappeared()
    {
        var (context, client, layoutDirectory) = await StartAsync("missing-monitor");
        await using var _ = context;
        await using var __ = client;
        await LaunchSampleAppsAsync(client);

        var crafted = new WorkspaceLayout(
            WorkspaceLayoutValidation.CurrentSchemaVersion,
            "missing-monitor",
            new DateTimeOffset(2026, 11, 16, 10, 0, 0, TimeSpan.Zero),
            [new WorkspaceMonitor("\\\\.\\DISPLAY9", true, new WorkspaceRect(0, 0, 1440, 900), new WorkspaceRect(0, 0, 1440, 860), 1.0)],
            [new WorkspaceBinding("sample.watchlist", "\\\\.\\DISPLAY9", new WorkspaceRect(10, 10, 800, 600), true)]);
        File.WriteAllText(Path.Combine(layoutDirectory, "missing-monitor.json"), AetherJson.SerializeIndented(crafted));

        var restored = await client.RestoreWorkspaceLayoutAsync("missing-monitor");

        Assert.True(restored.Applied, restored.DenialCode + ": " + restored.DenialMessage);
        Assert.True(restored.MissingMonitorCount >= 1);
        Assert.True(restored.PrimaryFallbackCount >= 1);
        var placement = restored.Placements.Single();
        Assert.Equal("sample.watchlist", placement.AppId);
        Assert.Equal("virtual-monitor-1", placement.MonitorDeviceName);
        Assert.Contains(restored.Notes, note => note.Contains("unavailable", StringComparison.Ordinal));
        Assert.True(placement.X >= 0 && placement.X + placement.Width <= 1920);
    }

    [Fact]
    public async Task CorruptPersistedLayoutIsDenied()
    {
        var (context, client, layoutDirectory) = await StartAsync("corrupt");
        await using var _ = context;
        await using var __ = client;
        await LaunchSampleAppsAsync(client);
        File.WriteAllText(Path.Combine(layoutDirectory, "corrupt.json"), "not-json");

        var restored = await client.RestoreWorkspaceLayoutAsync("corrupt");

        Assert.False(restored.Applied);
        Assert.Equal(AetherErrorCodes.WorkspaceLayoutInvalid, restored.DenialCode);
    }

    [Fact]
    public async Task UnknownLayoutIsDenied()
    {
        var (context, client, _) = await StartAsync("not-found");
        await using var __ = context;
        await using var ___ = client;

        var restored = await client.RestoreWorkspaceLayoutAsync("does-not-exist");

        Assert.False(restored.Applied);
        Assert.Equal(AetherErrorCodes.WorkspaceLayoutNotFound, restored.DenialCode);
    }

    [Fact]
    public async Task SaveWithoutLayoutStoreIsDeniedWhenDisabled()
    {
        await using var context = BrokerTestContext.Create(testName: "no-store");
        await context.StartAsync();
        await using var client = await context.RegisterOperatorAsync();

        var saved = await client.SaveWorkspaceLayoutAsync("desk-a");

        Assert.False(saved.Accepted);
        Assert.Equal(AetherErrorCodes.WorkspaceLayoutUnavailable, saved.DenialCode);
    }
}
