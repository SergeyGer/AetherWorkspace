using Aether.Broker.Workspaces;
using Aether.Contracts.Errors;
using Aether.Contracts.Models.Workspaces;
using Aether.Testing;
using Xunit;

namespace Aether.Broker.Tests;

public class WorkspaceLayoutStoreTests
{
    private static WorkspaceLayout Layout(string layoutId = "store-test") => new(
        WorkspaceLayoutValidation.CurrentSchemaVersion,
        layoutId,
        new DateTimeOffset(2026, 11, 16, 10, 0, 0, TimeSpan.Zero),
        [new WorkspaceMonitor("\\\\.\\DISPLAY1", true, new WorkspaceRect(0, 0, 1920, 1200), new WorkspaceRect(0, 0, 1920, 1160), 1.0)],
        [new WorkspaceBinding("sample.watchlist", "\\\\.\\DISPLAY1", new WorkspaceRect(0, 0, 960, 1160), true)]);

    [Fact]
    public void SaveAndLoadRoundTripsStrictLayout()
    {
        using var directory = new TempDirectory("layout-store");
        var store = new WorkspaceLayoutStore(directory.FullPath);
        var layout = Layout();

        store.Save(layout);
        var loaded = store.Load(layout.LayoutId);

        Assert.True(loaded.Found);
        Assert.NotNull(loaded.Layout);
        Assert.Equal(layout.LayoutId, loaded.Layout!.LayoutId);
        Assert.Equal(layout.Monitors.Single().DeviceName, loaded.Layout.Monitors.Single().DeviceName);
    }

    [Fact]
    public void SaveOverwritesExistingLayoutAtomically()
    {
        using var directory = new TempDirectory("layout-store");
        var store = new WorkspaceLayoutStore(directory.FullPath);
        store.Save(Layout());
        var changed = Layout();
        changed = changed with
        {
            Description = "updated",
            Bindings = [new WorkspaceBinding("sample.risk", "\\\\.\\DISPLAY1", new WorkspaceRect(100, 100, 700, 500), true)],
        };

        store.Save(changed);
        var loaded = store.Load(changed.LayoutId);

        Assert.Equal("updated", loaded.Layout!.Description);
        Assert.Equal("sample.risk", loaded.Layout.Bindings.Single().AppId);
        Assert.Empty(Directory.GetFiles(directory.FullPath, "*.tmp"));
    }

    [Fact]
    public void MissingLayoutReturnsNotFound()
    {
        using var directory = new TempDirectory("layout-store");
        var store = new WorkspaceLayoutStore(directory.FullPath);

        var loaded = store.Load("missing-layout");

        Assert.False(loaded.Found);
        Assert.Equal(AetherErrorCodes.WorkspaceLayoutNotFound, loaded.Code);
    }

    [Fact]
    public void CorruptLayoutFileReturnsInvalid()
    {
        using var directory = new TempDirectory("layout-store");
        var store = new WorkspaceLayoutStore(directory.FullPath);
        File.WriteAllText(store.GetLayoutPath("corrupt"), "not-json");

        var loaded = store.Load("corrupt");

        Assert.False(loaded.Found);
        Assert.Equal(AetherErrorCodes.WorkspaceLayoutInvalid, loaded.Code);
    }

    [Theory]
    [InlineData("")]
    [InlineData("../escape")]
    [InlineData("bad id")]
    [InlineData("trailing.")]
    [InlineData("a/b")]
    public void InvalidLayoutIdsAreRejected(string layoutId)
    {
        using var directory = new TempDirectory("layout-store");
        var store = new WorkspaceLayoutStore(directory.FullPath);

        Assert.Throws<Aether.Contracts.Errors.AetherContractException>(() => store.GetLayoutPath(layoutId));
    }

    [Fact]
    public void OversizedLayoutIsRejected()
    {
        using var directory = new TempDirectory("layout-store");
        var store = new WorkspaceLayoutStore(directory.FullPath, maxBytes: 4096);
        var layout = Layout() with
        {
            Description = new string('x', 8000),
        };

        Assert.Throws<Aether.Contracts.Errors.AetherContractException>(() => store.Save(layout));
    }
}
