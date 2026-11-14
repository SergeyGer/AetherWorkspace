using Aether.Broker;
using Aether.Broker.Sessions;
using Aether.Broker.Windows;
using Aether.Contracts.Models;
using Aether.Security.Audit;
using Aether.Testing;
using Xunit;

namespace Aether.Broker.Tests;

public class WindowRegistryTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 14, 12, 0, 0, TimeSpan.Zero);

    private static BrokerOptions Options(int maxWindows = 16) => new()
    {
        Registry = TestData.CreateRegistry(),
        Contexts = TestData.CreateContextCatalog(),
        MaxWindows = maxWindows,
        MaxTitleLength = 32,
    };

    private static BrokerSession Session(string appId = "sample.watchlist", string instanceId = "inst-1") =>
        new("sess-1", TestData.CreateRegistry().GetRequired(appId), instanceId, 1000, Now, "hash", Now, Now.AddHours(1));

    private static WindowState State(string appId = "sample.watchlist", string instanceId = "inst-1", string windowId = "main") =>
        new(appId, instanceId, windowId, "window", new WindowBounds(0, 0, 800, 600), "virtual-monitor-1", Visible: true, NativeHandle: 0);

    [Fact]
    public void ReportStoresWindowForTheAuthenticatedIdentity()
    {
        var registry = new WindowRegistry(Options());
        var session = Session();

        Assert.True(registry.Report(session, State(), out _, out _));
        Assert.Equal(1, registry.Count);
        Assert.True(registry.HasWindowForInstance("inst-1"));
        Assert.Single(registry.ListVisible());
    }

    [Fact]
    public void WindowFromAnotherIdentityIsRejected()
    {
        var registry = new WindowRegistry(Options());
        var session = Session(instanceId: "inst-1");

        Assert.False(registry.Report(session, State(instanceId: "inst-2"), out var code, out _));
        Assert.Equal(Contracts.Errors.AetherErrorCodes.AuthIdentityMismatch, code);
    }

    [Fact]
    public void InsaneBoundsAndOversizedTitlesAreRejected()
    {
        var registry = new WindowRegistry(Options());
        var session = Session();

        Assert.False(registry.Report(session, State() with { Bounds = new WindowBounds(0, 0, 0, 0) }, out _, out _));
        Assert.False(registry.Report(session, State() with { Bounds = new WindowBounds(0, 0, 99999, 600) }, out _, out _));
        Assert.False(registry.Report(session, State() with { Title = new string('x', 100) }, out _, out _));
        Assert.False(registry.Report(session, State() with { WindowId = "inv alid id" }, out _, out _));
        Assert.Equal(0, registry.Count);
    }

    [Fact]
    public void WindowCapacityIsBounded()
    {
        var registry = new WindowRegistry(Options(maxWindows: 1));
        var session = Session();

        Assert.True(registry.Report(session, State(windowId: "main"), out _, out _));
        Assert.False(registry.Report(session, State(windowId: "other"), out var code, out _));
        Assert.Equal(Contracts.Errors.AetherErrorCodes.BrokerQueueOverflow, code);
    }

    [Fact]
    public void RemovingAnInstanceDropsItsWindows()
    {
        var registry = new WindowRegistry(Options());
        var session = Session();

        registry.Report(session, State(), out _, out _);
        registry.RemoveInstance("inst-1");

        Assert.Equal(0, registry.Count);
    }
}
