using System.Diagnostics;
using System.Text.Json;
using System.Text;
using Aether.Broker.Client;
using Aether.Contracts;
using Aether.Contracts.Broker;
using Aether.Security.Audit;
using Aether.Testing;
using FlaUI.Core.AutomationElements;
using Xunit;
using Xunit.Sdk;

namespace Aether.Desktop.E2E.Tests;

/// <summary>
/// Windows reference-desktop end-to-end tests (v3.0 Addendum A01, WT-04-01/02).
/// </summary>
/// <remarks>
/// <para>
/// Each test starts the real <c>Aether.BrokerApp</c> over a Windows named pipe, launches
/// the real WPF shell (<c>Aether.Shell.exe</c>) with <c>AETHER_ENDPOINT_FILE</c> pointing at
/// the broker handshake file, drives the window through FlaUI/UIA3, runs the default
/// RESTRICTED reference workflow and then correlates the UI result with the broker's own
/// audit chain, child-process logs and an authenticated operator client.
/// </para>
/// <para>
/// This project is not part of <c>AetherWorkspace.slnx</c> and is never executed by the
/// Linux CI job. On a non-Windows host both facts return immediately: a cross-build proves
/// compilation only and is not UI evidence. A Windows host must be an interactive,
/// unlocked desktop session; locked-session runs are recorded as <c>SKIPPED_LOCKED</c> by
/// <c>tools/windows-test/Run-AetherTests.ps1</c> instead of executing these tests.
/// </para>
/// </remarks>
[Trait("Category", "ReferenceDesktop")]
[Collection("Desktop GUI")]
public sealed class DesktopGuiE2ETests
{
    private const string SuccessMarker = "Workflow succeeded";
    private const string RegistrationMarker = "Registered with broker";
    private const string PrivateSummaryMarker = "Private summary";
    private const string LocalSummaryProvider = "local-deterministic";
    private const string LocalSummaryProviderLine = "summary provider: " + LocalSummaryProvider;
    private const string ReferenceCommandPrefix = "Open Northstar bond analytics";
    private const string LayoutName = "side-by-side";

    private static readonly TimeSpan OperatorTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan ChildTraceTimeout = TimeSpan.FromSeconds(20);

    [Fact(Timeout = 300_000)]
    [Trait("TestId", "WT-04-01")]
    public async Task GuiWorkflowRunsOverWindowsNamedPipes()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        await using var broker = await NamedPipeBrokerHarness.StartAsync("gui-workflow");
        Assert.Equal(BrokerTransports.NamedPipe, broker.Endpoint.Transport);
        Assert.NotEmpty(broker.Endpoint.OperatorToken);

        using var shell = ShellWindowSession.Launch(broker.EndpointFile);
        var run = await RunRestrictedReferenceWorkflowAsync(shell, broker);

        // Operator-side verification: the handshake file's operator token authenticates a
        // separate BrokerClient that inspects the broker state after the GUI workflow.
        await using var operatorClient = await broker.CreateOperatorClientAsync();
        var health = await broker.WaitForAppHealthAsync(operatorClient, TestData.SampleAppIds, OperatorTimeout);
        Assert.Equal(BrokerTransports.NamedPipe, health.Transport);
        foreach (var appId in TestData.SampleAppIds)
        {
            Assert.Contains(
                health.AppHealth,
                entry => string.Equals(entry, appId + "=HEALTHY", StringComparison.Ordinal));
        }

        var snapshot = await operatorClient.GetSnapshotAsync(run.TraceId);
        Assert.Equal(broker.Endpoint.BrokerVersion, snapshot.BrokerVersion);
        Assert.Equal(Ordered(TestData.SampleAppIds), Ordered(snapshot.Apps.Where(app => app.Launchable).Select(app => app.AppId)));

        // The operator can still arrange the three reported top-level windows under the
        // workflow trace: proves the launched processes reported windows and the trace id is
        // accepted end-to-end by the broker.
        var arrangement = await WaitForAsync(
            () => operatorClient.ArrangeWindowsAsync(LayoutName, run.TraceId),
            reply => reply.Applied && reply.Layout.Placements.Count == TestData.SampleAppIds.Length,
            OperatorTimeout);
        Assert.True(
            arrangement.Applied,
            "Operator-side window arrangement was not applied by the broker." + Environment.NewLine + broker.ReadDiagnostics());
        Assert.Equal(
            Ordered(TestData.SampleAppIds),
            Ordered(arrangement.Layout.Placements.Select(placement => placement.AppId)));

        // Broker audit correlation: three successful launches (the launch audit outcome for an
        // allowlisted app is "allowed"), a context publish and a window arrangement; the
        // operator-side arrangement/publish prove the workflow trace id is accepted end-to-end.
        var audit = broker.ReadBrokerAudit();
        Assert.NotEmpty(audit);
        foreach (var appId in TestData.SampleAppIds)
        {
            Assert.Contains(
                audit,
                entry => entry.EventType == AuditEventTypes.BrokerLaunch
                    && entry.Outcome == AuditOutcomes.Allowed
                    && entry.BrokeredAppId == appId);
        }

        Assert.Contains(
            audit,
            entry => entry.EventType == AuditEventTypes.WindowArrange && entry.TraceId == run.TraceId);

        // Each of the three independent sample processes logged the workflow trace id.
        foreach (var appId in TestData.SampleAppIds)
        {
            Assert.True(
                await WaitForChildTraceAsync(broker, appId, run.TraceId, ChildTraceTimeout),
                $"The child process log for '{appId}' did not contain the workflow trace id "
                + $"'{run.TraceId}' within {ChildTraceTimeout.TotalSeconds:0}s."
                + Environment.NewLine
                + broker.ReadDiagnostics());
        }
    }

    [Fact(Timeout = 240_000)]
    [Trait("TestId", "WT-04-02")]
    [Trait("Category", "Privacy04")]
    public async Task RestrictedClassificationNeverShowsExternalProvider()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        await using var broker = await NamedPipeBrokerHarness.StartAsync("privacy");
        using var shell = ShellWindowSession.Launch(broker.EndpointFile);
        var run = await RunRestrictedReferenceWorkflowAsync(shell, broker);

        // The visible route is the local IPC + deterministic local provider path.
        Assert.Contains("local IPC only", run.BackendMode, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(LocalSummaryProviderLine, run.TraceText, StringComparison.Ordinal);
        Assert.Contains("output classification: RESTRICTED", run.TraceText, StringComparison.Ordinal);
        Assert.Contains("deterministic-grammar", run.TraceText, StringComparison.Ordinal);
        Assert.Contains("model=False", run.TraceText, StringComparison.Ordinal);
        Assert.Contains("chain valid=True", run.TraceText, StringComparison.Ordinal);

        // No external or cloud provider success is visible anywhere in the run surfaces.
        var combined = string.Join(
            Environment.NewLine,
            run.BackendMode,
            run.Status,
            run.Summary,
            run.TraceText);
        Assert.DoesNotContain("cloud", combined, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("externalProvider", combined, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("openai", combined, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("No external provider was called", run.Summary, StringComparison.Ordinal);

        // The private summary is local, synthetic and carries no account identifiers.
        Assert.Contains(PrivateSummaryMarker, run.Summary, StringComparison.Ordinal);
        Assert.DoesNotContain("ACC-", combined, StringComparison.Ordinal);

        // The operator never opted in to cloud processing and RESTRICTED stayed selected.
        Assert.False(shell.IsCloudConsentChecked());
        Assert.Equal("RESTRICTED", shell.ReadSelectedClassification());
    }

    private static async Task<GuiWorkflowRun> RunRestrictedReferenceWorkflowAsync(
        ShellWindowSession shell,
        NamedPipeBrokerHarness broker)
    {
        ArgumentNullException.ThrowIfNull(shell);
        ArgumentNullException.ThrowIfNull(broker);

        var backendMode = shell.WaitForBackendModeContaining(BrokerTransports.NamedPipe, ShellWindowSession.BackendTimeout);
        shell.AssertCoreControlsReachable();

        var registrationStatus = shell.WaitForStatus(
            text => text.Contains(RegistrationMarker, StringComparison.Ordinal)
                || text.Contains("could not connect", StringComparison.OrdinalIgnoreCase),
            ShellWindowSession.RegistrationTimeout,
            "broker registration");
        Assert.True(
            registrationStatus.Contains(RegistrationMarker, StringComparison.Ordinal),
            "The WPF shell did not register with the named-pipe broker."
            + Environment.NewLine
            + "Status:" + Environment.NewLine
            + registrationStatus
            + Environment.NewLine
            + broker.ReadDiagnostics());

        // RESTRICTED is selected by default; make the precondition explicit and verifiable.
        shell.EnsureRestrictedClassification();
        Assert.Equal("RESTRICTED", shell.ReadSelectedClassification());
        Assert.False(shell.IsCloudConsentChecked());

        // Keep the default reference command: the reference workflow only makes sense for it.
        Assert.Contains(ReferenceCommandPrefix, shell.ReadCommand(), StringComparison.Ordinal);

        shell.ClickRun();

        var workflowStatus = shell.WaitForStatus(
            text => text.Contains(SuccessMarker, StringComparison.Ordinal) || IsTerminalFailure(text),
            ShellWindowSession.WorkflowTimeout,
            "reference workflow completion");
        Assert.True(
            workflowStatus.Contains(SuccessMarker, StringComparison.Ordinal),
            "The reference workflow did not succeed over the Windows named-pipe transport."
            + Environment.NewLine
            + "Status:" + Environment.NewLine
            + workflowStatus
            + Environment.NewLine
            + broker.ReadDiagnostics());

        var traceId = ExtractTraceId(workflowStatus);

        var summary = shell.WaitForSummary(
            text => text.Contains(PrivateSummaryMarker, StringComparison.Ordinal),
            ShellWindowSession.SummaryTimeout,
            "private local summary");
        Assert.DoesNotContain("ACC-", summary, StringComparison.Ordinal);

        var traceText = shell.WaitForTrace(
            text => text.Contains("trace: " + traceId, StringComparison.Ordinal),
            ShellWindowSession.TraceTimeout,
            "trace/audit view carrying the workflow trace id");
        Assert.DoesNotContain("ACC-", traceText, StringComparison.Ordinal);

        return new GuiWorkflowRun(backendMode, workflowStatus, summary, traceText, traceId);
    }

    private static string ExtractTraceId(string status)
    {
        const string marker = "Trace ";
        var markerIndex = status.LastIndexOf(marker, StringComparison.Ordinal);
        if (markerIndex < 0)
        {
            throw new XunitException(
                "The success status did not contain a trace id." + Environment.NewLine + status);
        }

        var traceId = status[(markerIndex + marker.Length)..].Trim();
        Assert.Equal(32, traceId.Length);
        Assert.True(
            TraceContext.IsValidTraceId(traceId),
            $"'{traceId}' is not a valid 128-bit trace id.");
        Assert.All(
            traceId,
            character => Assert.True(Uri.IsHexDigit(character), $"Trace id '{traceId}' contains non-hex character '{character}'."));
        return traceId;
    }

    private static bool IsTerminalFailure(string statusText) =>
        statusText.Contains("Workflow failed", StringComparison.Ordinal)
        || statusText.Contains("Workflow denied", StringComparison.Ordinal)
        || statusText.Contains("Workflow cancelled", StringComparison.Ordinal)
        || statusText.Contains("Cannot run", StringComparison.Ordinal);

    private static async Task<bool> WaitForChildTraceAsync(
        NamedPipeBrokerHarness broker,
        string appId,
        string traceId,
        TimeSpan timeout)
    {
        var needle = "\"traceId\":\"" + traceId + "\"";
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (broker.ReadChildLogLines(appId).Any(line => line.Contains(needle, StringComparison.Ordinal)))
            {
                return true;
            }

            await Task.Delay(250, CancellationToken.None);
        }

        return false;
    }

    private static async Task<T> WaitForAsync<T>(
        Func<Task<T>> probe,
        Func<T, bool> isComplete,
        TimeSpan timeout)
    {
        ArgumentNullException.ThrowIfNull(probe);
        ArgumentNullException.ThrowIfNull(isComplete);

        var deadline = DateTime.UtcNow + timeout;
        T last = default!;
        while (DateTime.UtcNow < deadline)
        {
            last = await probe();
            if (isComplete(last))
            {
                return last;
            }

            await Task.Delay(250, CancellationToken.None);
        }

        return last;
    }

    private static string[] Ordered(IEnumerable<string> values) =>
        values.OrderBy(value => value, StringComparer.Ordinal).ToArray();

    private sealed record GuiWorkflowRun(
        string BackendMode,
        string Status,
        string Summary,
        string TraceText,
        string TraceId);
    /// <summary>
    /// WT-04-09 regression for the packaged desktop defect: the real WPF shell runs the
    /// reference workflow against the three *real WPF* sample apps (not the headless doubles),
    /// including window arrangement and typed context delivery. The sample windows must remain
    /// open after the workflow, with broker health still HEALTHY.
    /// </summary>
    [Fact(Timeout = 360_000)]
    [Trait("TestId", "WT-04-09")]
    public async Task GuiWorkflowWithRealWpfSampleAppsKeepsWindowsOpen()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var samplesExecutable = DesktopExecutableLocator.ResolveWpfSampleApps();
        await using var broker = await NamedPipeBrokerHarness.StartAsync(
            "gui-wpf-samples",
            useWindowsRegistry: true,
            wpfSamplesExe: samplesExecutable);

        using var shell = ShellWindowSession.Launch(broker.EndpointFile);
        var run = await RunRestrictedReferenceWorkflowAsync(shell, broker);

        await Task.Delay(TimeSpan.FromSeconds(6));

        await using var operatorClient = await broker.CreateOperatorClientAsync();
        var health = await broker.WaitForAppHealthAsync(operatorClient, TestData.SampleAppIds, OperatorTimeout);
        foreach (var appId in TestData.SampleAppIds)
        {
            Assert.Contains(health.AppHealth, entry => string.Equals(entry, appId + "=HEALTHY", StringComparison.Ordinal));
        }

        var deadline = DateTime.UtcNow.AddSeconds(20);
        var live = Array.Empty<Process>();
        while (DateTime.UtcNow < deadline)
        {
            live = Process.GetProcessesByName("Aether.SampleApps").Where(process => !process.HasExited).ToArray();
            if (live.Length >= TestData.SampleAppIds.Length &&
                live.All(process =>
                {
                    process.Refresh();
                    return process.MainWindowHandle != IntPtr.Zero;
                }))
            {
                break;
            }

            await Task.Delay(250);
        }

        var diagnostics = string.Join(
            Environment.NewLine,
            new[] { $"trace: {run.TraceId}", $"live Aether.SampleApps processes: {live.Length}" }
                .Concat(live.Select(process => $"pid={process.Id} exited={process.HasExited} mainWindow=0x{process.MainWindowHandle.ToInt64():X}"))
                .Concat(Directory.Exists(broker.ChildLogDirectory)
                    ? Directory.GetFiles(broker.ChildLogDirectory, "*.log")
                        .OrderBy(path => path, StringComparer.Ordinal)
                        .SelectMany(path => new[] { "--- " + Path.GetFileName(path) + " ---" }.Concat(File.ReadAllLines(path).TakeLast(40)))
                    : ["(no child logs)"]));

        Assert.True(live.Length >= TestData.SampleAppIds.Length, "A WPF sample process exited after the reference workflow.\n" + diagnostics);
        Assert.All(live, process =>
        {
            process.Refresh();
            Assert.True(process.MainWindowHandle != IntPtr.Zero, $"WPF sample pid {process.Id} lost its main window.\n" + diagnostics);
        });
    }

    /// <summary>
    /// AW-055/AW-056 local capture scenario: runs a real named-pipe desktop workflow (Track A
    /// deterministic or Track B LLM-assisted PUBLIC), drives the real WPF sample windows for the
    /// ticker scene, holds video-friendly beats and writes a sanitized event manifest. This test is
    /// intentionally in Category=DemoCapture and is never run by CI; it is invoked only by the local
    /// Windows demo-capture harness while OBS records.
    /// </summary>
    [Fact(Timeout = 600_000)]
    [Trait("Category", "DemoCapture")]
    public async Task CaptureReferenceDemoScenario()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var scenario = Environment.GetEnvironmentVariable("AETHER_DEMO_SCENARIO") ?? "TrackA";
        var eventsPath = Environment.GetEnvironmentVariable("AETHER_DEMO_EVENTS_OUT");
        if (string.IsNullOrWhiteSpace(eventsPath))
        {
            eventsPath = Path.Combine(Path.GetTempPath(), $"aether-demo-events-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}.json");
        }

        const string northstarCommand = "Open Northstar bond analytics, show the associated risk view, synchronize this instrument across the workspace and summarize the exposure privately.";
        const string vegaCommand = "Open Vega bond analytics, show the associated risk view, synchronize this instrument across the workspace and summarize the exposure privately.";
        var started = DateTimeOffset.UtcNow;
        var holdScale = double.TryParse(Environment.GetEnvironmentVariable("AETHER_DEMO_HOLD_SCALE"), out var configuredScale)
            ? Math.Clamp(configuredScale, 0.2, 3.0)
            : 1.0;
        Task Hold(double seconds) => Task.Delay(TimeSpan.FromSeconds(seconds * holdScale), CancellationToken.None);
        var events = new List<object>();
        void Mark(string name, string? detail = null) =>
            events.Add(new { name, timestampUtc = DateTimeOffset.UtcNow, detail });

        var samplesExecutable = DesktopExecutableLocator.ResolveWpfSampleApps();
        await using var broker = await NamedPipeBrokerHarness.StartAsync(
            "demo-capture-" + scenario.ToLowerInvariant(),
            useWindowsRegistry: true,
            wpfSamplesExe: samplesExecutable);
        using var shell = ShellWindowSession.Launch(broker.EndpointFile);

        _ = shell.WaitForBackendModeContaining("named-pipe", ShellWindowSession.BackendTimeout);
        shell.WaitForStatus(text => text.Contains("Registered", StringComparison.Ordinal), ShellWindowSession.RegistrationTimeout, "shell registration");
        Mark("shell-ready");
        await Hold(4);

        string traceText;
        string traceId;
        bool usedModel;
        if (string.Equals(scenario, "TrackB", StringComparison.OrdinalIgnoreCase))
        {
            shell.SetClassification("PUBLIC");
            shell.SetPlanningMode(llmAssisted: true);
            shell.SetCloudConsent(true);
            shell.SetCommand(vegaCommand);
            Mark("track-b-configured");
        }
        else
        {
            shell.EnsureRestrictedClassification();
            shell.SetPlanningMode(llmAssisted: false);
            shell.SetCloudConsent(false);
            shell.SetCommand(northstarCommand);
            Mark("track-a-configured");
        }

        await Hold(5);
        shell.ClickRun();
        Mark("workflow-started");
        var status = shell.WaitForStatus(
            text => text.Contains("Workflow succeeded", StringComparison.Ordinal) || IsTerminalFailure(text),
            ShellWindowSession.WorkflowTimeout,
            "captured workflow result");
        Assert.True(status.Contains("Workflow succeeded", StringComparison.Ordinal), "The captured scenario failed: " + status);
        traceId = ExtractTraceId(status);
        var summary = shell.WaitForSummary(text => text.Contains("Private summary", StringComparison.Ordinal), ShellWindowSession.SummaryTimeout, "private summary");
        Assert.Contains("Private summary", summary, StringComparison.Ordinal);
        traceText = shell.ReadTrace();
        usedModel = traceText.Contains("deterministic-grammar, model=False", StringComparison.Ordinal) ? false
            : traceText.Contains("cloud-plan, model=True", StringComparison.Ordinal) ? true
            : throw new Xunit.Sdk.XunitException("The captured trace does not expose truthful planner provenance: " + traceText);
        if (string.Equals(scenario, "TrackB", StringComparison.OrdinalIgnoreCase))
        {
            Assert.True(usedModel, "Track B capture did not use the LLM-assisted planner.");
            Assert.Contains("cloud-plan", traceText, StringComparison.Ordinal);
        }
        else
        {
            Assert.False(usedModel, "Track A capture must not claim model output.");
            Assert.Contains("deterministic-grammar", traceText, StringComparison.Ordinal);
        }

        Mark("workflow-succeeded", "planner=" + (usedModel ? "cloud-plan" : "deterministic-grammar"));
        await Hold(8);

        shell.ClickDemoLayout();
        Mark("demo-layout");
        await Hold(6);

        using var automation = new FlaUI.UIA3.UIA3Automation();
        var windows = WaitForSampleWindows(automation, TimeSpan.FromSeconds(30));
        var sampleProcesses = Process.GetProcessesByName("Aether.SampleApps")
            .Where(process => !process.HasExited)
            .ToArray();
        Mark("sample-windows", string.Join(",", windows.Keys));
        await Hold(10);

        foreach (var ticker in new[] { "VEGA", "ORIN" })
        {
            SelectWatchlistTicker(windows["watchlist"], ticker);
            Mark("ticker-" + ticker.ToLowerInvariant());
            await Hold(10);
        }

        shell.SelectTraceTab();
        Mark("inspector");
        await Hold(10);
        Mark("ticker-scene-complete");
        await Hold(6);

        var completed = DateTimeOffset.UtcNow;
        var manifest = new
        {
            schemaVersion = "1.0",
            kind = "windows11-demo-capture",
            scenario,
            startedAtUtc = started,
            completedAtUtc = completed,
            commitSha = Environment.GetEnvironmentVariable("AETHER_DEMO_SHA") ?? string.Empty,
            runId = Environment.GetEnvironmentVariable("AETHER_DEMO_RUN_ID") ?? string.Empty,
            traceId,
            planner = usedModel ? "cloud-plan" : "deterministic-grammar",
            usedModel,
            pids = sampleProcesses.Select(process => process!.Id).ToArray(),
            tickers = new[] { "VEGA", "ORIN" },
            events,
        };
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(eventsPath))!);
        File.WriteAllText(eventsPath, JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));
        Mark("manifest-written");
    }

    private static Dictionary<string, FlaUI.Core.AutomationElements.Window> WaitForSampleWindows(
        FlaUI.UIA3.UIA3Automation automation,
        TimeSpan timeout)
    {
        var titles = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["watchlist"] = "Synthetic Watchlist",
            ["risk"] = "Synthetic Risk View",
            ["chart"] = "Synthetic Bond Chart",
        };
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            var found = new Dictionary<string, FlaUI.Core.AutomationElements.Window>(StringComparer.Ordinal);
            foreach (var process in Process.GetProcessesByName("Aether.SampleApps").Where(process => !process.HasExited))
            {
                var windows = FlaUI.Core.Application.Attach(process.Id).GetAllTopLevelWindows(automation);
                foreach (var (key, title) in titles)
                {
                    var window = windows.FirstOrDefault(candidate => candidate.Title.Contains(title, StringComparison.Ordinal));
                    if (window is not null)
                    {
                        found[key] = window;
                    }
                }
            }

            if (found.Count == titles.Count)
            {
                return found;
            }

            Thread.Sleep(250);
        }

        throw new Xunit.Sdk.XunitException("The real WPF sample windows did not appear for the demo capture scenario.");
    }

    private static void SelectWatchlistTicker(FlaUI.Core.AutomationElements.Window watchlist, string ticker)
    {
        var grid = watchlist.FindFirstDescendant(criteria => criteria.ByAutomationId("WatchlistGrid"))?.AsDataGridView()
            ?? throw new Xunit.Sdk.XunitException("WatchlistGrid was not found in the demo capture scenario.");
        var row = grid.Rows.FirstOrDefault(candidate =>
            candidate.Cells.Any(cell => string.Equals(cell.Value?.ToString(), ticker, StringComparison.Ordinal)))
            ?? throw new Xunit.Sdk.XunitException($"Watchlist row '{ticker}' was not found in the demo capture scenario.");
        var selection = row.Patterns.SelectionItem.PatternOrDefault
            ?? throw new Xunit.Sdk.XunitException($"Watchlist row '{ticker}' does not expose SelectionItem.");
        selection.Select();
    }
}
