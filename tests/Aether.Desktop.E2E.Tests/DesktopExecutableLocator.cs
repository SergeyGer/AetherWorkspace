using Aether.Testing;

namespace Aether.Desktop.E2E.Tests;

/// <summary>
/// Resolves the build outputs that the reference-desktop tests launch as separate
/// processes: the compiled WPF shell (<c>Aether.Shell.exe</c>) and the real broker
/// (<c>Aether.BrokerApp.exe</c>, or the muxer plus <c>Aether.BrokerApp.dll</c>).
/// </summary>
/// <remarks>
/// The configuration is inferred from this test assembly's output directory
/// (<c>bin/Debug</c> or <c>bin/Release</c>) so a plain <c>dotnet test</c> after a matching
/// build never needs extra configuration. <c>AETHER_SHELL_EXE</c> and
/// <c>AETHER_BROKER_EXE</c> override the resolved paths for non-standard layouts.
/// </remarks>
internal static class DesktopExecutableLocator
{
    private const string ShellExecutableOverrideVariable = "AETHER_SHELL_EXE";
    private const string BrokerExecutableOverrideVariable = "AETHER_BROKER_EXE";
    private const string WpfSamplesOverrideVariable = "AETHER_WPF_SAMPLES_EXE";
    private const string WebView2ChartOverrideVariable = "AETHER_WEBVIEW2_CHART_EXE";

    /// <summary>Resolves the WPF shell host executable.</summary>
    public static string ResolveShell()
    {
        var overridePath = ResolveOverride(ShellExecutableOverrideVariable);
        if (overridePath is not null)
        {
            return overridePath;
        }

        var path = TestRepo.PathTo(
            "src",
            "Aether.Shell",
            "bin",
            TestRepo.Configuration,
            "net10.0-windows",
            "Aether.Shell.exe");
        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                $"The compiled WPF shell was not found at '{path}'. Build src/Aether.Shell for the "
                + $"'{TestRepo.Configuration}' configuration first (or set {ShellExecutableOverrideVariable}).",
                path);
        }

        return path;
    }

    /// <summary>Resolves the WPF sample-applications host executable (Watchlist/Risk/Chart).</summary>
    public static string ResolveWpfSampleApps()
    {
        var overridePath = ResolveOverride(WpfSamplesOverrideVariable);
        if (overridePath is not null)
        {
            return overridePath;
        }

        var path = TestRepo.PathTo(
            "src",
            "Aether.SampleApps",
            "bin",
            TestRepo.Configuration,
            "net10.0-windows",
            "Aether.SampleApps.exe");
        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                $"The compiled WPF sample apps were not found at '{path}'. Build src/Aether.SampleApps for the "
                + $"'{TestRepo.Configuration}' configuration first (or set {WpfSamplesOverrideVariable}).",
                path);
        }

        return path;
    }

    /// <summary>Resolves the independently launched WebView2 chart host executable.</summary>
    public static string ResolveWebView2Chart()
    {
        var overridePath = ResolveOverride(WebView2ChartOverrideVariable);
        if (overridePath is not null)
        {
            return overridePath;
        }

        var path = TestRepo.PathTo(
            "src",
            "Aether.SampleApps.WebView2",
            "bin",
            TestRepo.Configuration,
            "net10.0-windows",
            "Aether.SampleApps.WebView2.exe");
        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                $"The compiled WebView2 chart host was not found at '{path}'. Build src/Aether.SampleApps.WebView2 for the "
                + $"'{TestRepo.Configuration}' configuration first (or set {WebView2ChartOverrideVariable}).",
                path);
        }

        return path;
    }

    /// <summary>
    /// Resolves how the real broker is started. On Windows the compiled apphost is preferred;
    /// the framework-dependent fallback (<c>dotnet Aether.BrokerApp.dll</c>) keeps the helper
    /// usable from a Linux cross-build without an apphost.
    /// </summary>
    public static (string FileName, string? PrefixAssembly) ResolveBrokerHost()
    {
        var overridePath = ResolveOverride(BrokerExecutableOverrideVariable);
        if (overridePath is not null)
        {
            return (overridePath, null);
        }

        var executable = TestRepo.PathTo(
            "src",
            "Aether.BrokerApp",
            "bin",
            TestRepo.Configuration,
            "net10.0",
            "Aether.BrokerApp.exe");
        if (File.Exists(executable))
        {
            return (executable, null);
        }

        return (TestData.FindDotnetExecutable(), TestRepo.FindProjectOutput("src/Aether.BrokerApp", "Aether.BrokerApp.dll"));
    }

    private static string? ResolveOverride(string variable)
    {
        var configured = Environment.GetEnvironmentVariable(variable);
        if (string.IsNullOrWhiteSpace(configured))
        {
            return null;
        }

        var candidate = Path.IsPathRooted(configured) ? configured : Path.Combine(TestRepo.Root, configured);
        if (!File.Exists(candidate))
        {
            throw new FileNotFoundException($"{variable} points to '{candidate}', which does not exist.", candidate);
        }

        return candidate;
    }
}
