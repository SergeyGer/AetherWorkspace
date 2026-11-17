using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Aether.Contracts;
using Aether.Testing;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.UIA3;
using Xunit;
using Xunit.Sdk;

namespace Aether.Shell.UiTests;

/// <summary>
/// Windows-only end-to-end UI automation for the WPF shell. The tests launch a real
/// broker through <see cref="EndToEndHarness"/> (Unix-domain-socket transport), start
/// the compiled Aether.Shell executable and drive it through UI Automation (FlaUI/UIA3).
/// </summary>
/// <remarks>
/// These tests are deliberately not part of AetherWorkspace.slnx and therefore never
/// run on Linux CI. They require an interactive Windows desktop session. On any other
/// operating system each test returns before touching FlaUI so a cross-compiled test
/// assembly does not claim to have verified the UI.
/// </remarks>
[Trait("Category", "UI")]
[Collection("Shell UI")]
public sealed partial class ShellUiAutomationTests
{
    private const string WindowTitle = "AetherWorkspace";
    private const string ExpectedTransport = "unix-domain-socket";
    private const string RegistrationMarker = "Registered with broker";
    private const string SuccessMarker = "Workflow succeeded";
    private const string PrivateSummaryMarker = "Private summary";
    private const string ReferenceCommandPrefix = "Open Northstar bond analytics";

    private static readonly string[] SmokeControls =
    [
        "CommandBox",
        "RunButton",
        "ClassificationCombo",
        "CloudConsent",
        "CancelButton",
        "StatusBox",
        "StepsList",
    ];

    private static readonly TimeSpan ElementTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan WindowTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan ConnectionTimeout = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan RegistrationTimeout = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan WorkflowTimeout = TimeSpan.FromSeconds(150);
    private static readonly TimeSpan SummaryTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(500);

    [Fact(Timeout = 180_000)]
    public async Task ShellStartsConnectsToBrokerAndExposesCoreControls()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        await RunSmokeScenarioAsync();
    }

    [Fact(Timeout = 300_000)]
    public async Task RestrictedReferenceWorkflowProducesPrivateSummaryAndTrace()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        await RunWorkflowScenarioAsync();
    }

    /// <summary>
    /// AW-066 measurement run: executes the reference workflow repeatedly through the real WPF
    /// shell, reads the per-run render-frame/command telemetry rendered by the shell, and writes a
    /// sanitized JSONL for the local measurement runner. Set AETHER_UI_CYCLES=100 on the physical
    /// Windows 11 reference laptop; CI uses the default three-cycle smoke.
    /// </summary>
    [Fact(Timeout = 1_800_000)]
    public async Task RepeatedWorkflowReportsUiTelemetry()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var cycles = int.TryParse(Environment.GetEnvironmentVariable("AETHER_UI_CYCLES"), out var configured)
            ? Math.Clamp(configured, 1, 100)
            : 3;
        var outputPath = Environment.GetEnvironmentVariable("AETHER_UI_TELEMETRY_OUT");
        if (string.IsNullOrWhiteSpace(outputPath))
        {
            outputPath = Path.Combine(Path.GetTempPath(), $"aether-ui-telemetry-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}.jsonl");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        if (File.Exists(outputPath))
        {
            File.Delete(outputPath);
        }

        await using var harness = await EndToEndHarness.StartAsync("ui-telemetry");
        var shellExecutable = ShellExecutableLocator.Resolve();
        Application? application = null;
        try
        {
            application = LaunchShell(shellExecutable, harness.EndpointFile);
            using var automation = new UIA3Automation();
            var window = AttachToMainWindow(application, automation);

            var backendMode = WaitForElement(window, "BackendModeText", ElementTimeout);
            await WaitForBackendModeAsync(window, backendMode, ConnectionTimeout);
            var statusBox = WaitForElement(window, "StatusBox", ElementTimeout);
            await WaitForTextAsync(
                () => ReadTextBox(statusBox),
                text => text.Contains(RegistrationMarker, StringComparison.Ordinal),
                RegistrationTimeout);

            var classificationCombo = WaitForElement(window, "ClassificationCombo", ElementTimeout).AsComboBox();
            EnsureRestrictedClassification(classificationCombo);
            var telemetryElement = WaitForElement(window, "UiTelemetryText", ElementTimeout);
            var records = new List<Dictionary<string, object?>>();
            string? previousTraceId = null;

            for (var cycle = 1; cycle <= cycles; cycle++)
            {
                var runButton = WaitForElement(window, "RunButton", ElementTimeout).AsButton();
                await WaitForElementEnabledAsync(runButton, "RunButton", ElementTimeout);
                runButton.Invoke();
                // The shell clears StatusBox at the start of each run, so "workflow succeeded" is
                // per-run; distinguish cycles by a new trace id instead of accumulated text.
                var status = await WaitForTextAsync(
                    () =>
                    {
                        var text = ReadTextBox(statusBox);
                        var trace = TraceIdPattern().Match(text);
                        return trace.Success && trace.Groups["trace"].Value != previousTraceId ? text : text;
                    },
                    text =>
                    {
                        var trace = TraceIdPattern().Match(text);
                        return (text.Contains(SuccessMarker, StringComparison.Ordinal)
                                && trace.Success
                                && trace.Groups["trace"].Value != previousTraceId)
                            || IsTerminalFailure(text);
                    },
                    WorkflowTimeout);
                var cycleTrace = TraceIdPattern().Match(status);
                Assert.True(
                    status.Contains(SuccessMarker, StringComparison.Ordinal) && cycleTrace.Success && cycleTrace.Groups["trace"].Value != previousTraceId,
                    $"Cycle {cycle}/{cycles} did not succeed." + Environment.NewLine + status);
                previousTraceId = cycleTrace.Groups["trace"].Value;

                var telemetry = await WaitForTextAsync(
                    () => ReadTextBlock(telemetryElement),
                    text => text.Contains("frames=", StringComparison.Ordinal) && text.Contains("command=", StringComparison.Ordinal) && text.Contains("gc=", StringComparison.Ordinal),
                    ElementTimeout);
                var match = UiTelemetryPattern().Match(telemetry);
                Assert.True(match.Success, "Could not parse UI telemetry: " + telemetry);
                var record = new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["cycle"] = cycle,
                    ["timestampUtc"] = DateTimeOffset.UtcNow,
                    ["traceId"] = TraceIdPattern().Match(status).Groups["trace"].Value,
                    ["frames"] = int.Parse(match.Groups["frames"].Value, System.Globalization.CultureInfo.InvariantCulture),
                    ["p50FrameMs"] = double.Parse(match.Groups["p50"].Value, System.Globalization.CultureInfo.InvariantCulture),
                    ["p95FrameMs"] = double.Parse(match.Groups["p95"].Value, System.Globalization.CultureInfo.InvariantCulture),
                    ["p99FrameMs"] = double.Parse(match.Groups["p99"].Value, System.Globalization.CultureInfo.InvariantCulture),
                    ["maxFrameMs"] = double.Parse(match.Groups["max"].Value, System.Globalization.CultureInfo.InvariantCulture),
                    ["longFrames"] = int.Parse(match.Groups["long"].Value, System.Globalization.CultureInfo.InvariantCulture),
                    ["commandDurationMs"] = long.Parse(match.Groups["command"].Value, System.Globalization.CultureInfo.InvariantCulture),
                    ["workingSetMb"] = double.Parse(match.Groups["ws"].Value, System.Globalization.CultureInfo.InvariantCulture),
                    ["managedMemoryMb"] = double.Parse(match.Groups["gc"].Value, System.Globalization.CultureInfo.InvariantCulture),
                };
                records.Add(record);
                Assert.True((int)record["frames"]! > 0, $"Cycle {cycle} captured no UI frames.");
                File.AppendAllText(outputPath, JsonSerializer.Serialize(record) + Environment.NewLine);
            }

            Assert.Equal(cycles, records.Count);
            var summary = new
            {
                schemaVersion = "1.0",
                kind = "wpf-ui-telemetry",
                cycles,
                outputPath,
                records,
            };
            File.WriteAllText(outputPath + ".summary.json", JsonSerializer.Serialize(summary, new JsonSerializerOptions { WriteIndented = true }));
        }
        finally
        {
            CloseShell(application);
        }
    }

    [GeneratedRegex(@"frames=(?<frames>\d+)\s+p50=(?<p50>[\d.]+)ms\s+p95=(?<p95>[\d.]+)ms\s+p99=(?<p99>[\d.]+)ms\s+max=(?<max>[\d.]+)ms\s+long=(?<long>\d+)\s+command=(?<command>\d+)ms\s+ws=(?<ws>[\d.]+)MB\s+gc=(?<gc>[\d.]+)MB", RegexOptions.CultureInvariant)]
    private static partial Regex UiTelemetryPattern();

    private static async Task WaitForElementEnabledAsync(AutomationElement element, string description, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (element.IsEnabled)
            {
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(100), CancellationToken.None);
        }

        Assert.Fail($"The '{description}' control did not become enabled within {timeout.TotalSeconds:0}s.");
    }

    private static int CountOccurrences(string text, string value)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
    }

    private static async Task RunSmokeScenarioAsync()
    {
        await using var harness = await EndToEndHarness.StartAsync("ui-smoke");
        var shellExecutable = ShellExecutableLocator.Resolve();
        Application? application = null;
        try
        {
            application = LaunchShell(shellExecutable, harness.EndpointFile);
            using var automation = new UIA3Automation();
            var window = AttachToMainWindow(application, automation);

            var backendMode = WaitForElement(window, "BackendModeText", ElementTimeout);
            await WaitForBackendModeAsync(window, backendMode, ConnectionTimeout);

            foreach (var automationId in SmokeControls)
            {
                _ = WaitForElement(window, automationId, ElementTimeout);
            }

            // The command bar keeps its default reference command and the classification
            // combo defaults to the most restrictive classification.
            var commandText = ReadTextBox(window, "CommandBox");
            Assert.Contains(ReferenceCommandPrefix, commandText, StringComparison.Ordinal);

            var classificationCombo = WaitForElement(window, "ClassificationCombo", ElementTimeout).AsComboBox();
            Assert.Equal("RESTRICTED", classificationCombo.SelectedItem?.Name);
        }
        finally
        {
            CloseShell(application);
        }
    }

    private static async Task RunWorkflowScenarioAsync()
    {
        await using var harness = await EndToEndHarness.StartAsync("ui-workflow");
        var shellExecutable = ShellExecutableLocator.Resolve();
        Application? application = null;
        try
        {
            application = LaunchShell(shellExecutable, harness.EndpointFile);
            using var automation = new UIA3Automation();
            var window = AttachToMainWindow(application, automation);

            var backendMode = WaitForElement(window, "BackendModeText", ElementTimeout);
            await WaitForBackendModeAsync(window, backendMode, ConnectionTimeout);

            var statusBox = WaitForElement(window, "StatusBox", ElementTimeout);
            var registrationStatus = await WaitForTextAsync(
                () => ReadTextBox(statusBox),
                text => text.Contains(RegistrationMarker, StringComparison.Ordinal)
                    || text.Contains("could not connect to the local broker", StringComparison.Ordinal),
                RegistrationTimeout);
            Assert.True(
                registrationStatus.Contains(RegistrationMarker, StringComparison.Ordinal),
                "The WPF shell did not register with the end-to-end broker." + Environment.NewLine
                + "Status:" + Environment.NewLine + registrationStatus);

            // RESTRICTED is selected by default; make the precondition explicit and repair
            // it through UIA if a future XAML change picks a different default.
            var classificationCombo = WaitForElement(window, "ClassificationCombo", ElementTimeout).AsComboBox();
            EnsureRestrictedClassification(classificationCombo);

            // Keep the default reference command: the workflow only makes sense for it.
            var commandText = ReadTextBox(window, "CommandBox");
            Assert.Contains(ReferenceCommandPrefix, commandText, StringComparison.Ordinal);

            var runButton = WaitForElement(window, "RunButton", ElementTimeout).AsButton();
            runButton.Invoke();

            var workflowStatus = await WaitForTextAsync(
                () => ReadTextBox(statusBox),
                text => text.Contains(SuccessMarker, StringComparison.Ordinal) || IsTerminalFailure(text),
                WorkflowTimeout);
            Assert.True(
                workflowStatus.Contains(SuccessMarker, StringComparison.Ordinal),
                "The reference workflow did not succeed." + Environment.NewLine
                + "Status:" + Environment.NewLine + workflowStatus + Environment.NewLine
                + DescribeHarness(harness));

            var summaryBox = WaitForElement(window, "SummaryBox", ElementTimeout);
            var summary = await WaitForTextAsync(
                () => ReadTextBox(summaryBox),
                text => text.Contains(PrivateSummaryMarker, StringComparison.Ordinal),
                SummaryTimeout);
            Assert.Contains(PrivateSummaryMarker, summary, StringComparison.Ordinal);
            Assert.DoesNotContain("ACC-", summary, StringComparison.Ordinal);

            var traceMatch = TraceIdPattern().Match(workflowStatus);
            Assert.True(
                traceMatch.Success,
                "The success status did not contain a trace id." + Environment.NewLine + workflowStatus);
            var traceId = traceMatch.Groups["trace"].Value;
            Assert.Matches("^[0-9a-fA-F]{32}$", traceId);
            Assert.True(TraceContext.IsValidTraceId(traceId), $"'{traceId}' is not a valid 128-bit trace id.");

            // The rendered trace/audit tab must carry the same trace id. Selecting the tab
            // through UIA also proves the TraceBox automation id is reachable.
            SelectTab(window, "Trace / audit");
            var traceBox = WaitForElement(window, "TraceBox", ElementTimeout);
            var traceText = ReadTextBox(traceBox);
            Assert.Contains("trace: " + traceId, traceText, StringComparison.Ordinal);

            // v1.1 demo surface: compact demo-layout action and explicit planning mode reachable by AutomationId.
            WaitForElement(window, "DemoLayoutButton", ElementTimeout);
            WaitForElement(window, "PlannerCombo", ElementTimeout);
        }
        finally
        {
            CloseShell(application);
        }
    }

    private static Application LaunchShell(string executablePath, string endpointFile)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = executablePath,
            WorkingDirectory = Path.GetDirectoryName(executablePath) ?? TestRepo.Root,
            UseShellExecute = false,
        };
        startInfo.Environment["AETHER_ENDPOINT_FILE"] = endpointFile;
        return Application.Launch(startInfo);
    }

    private static Window AttachToMainWindow(Application application, UIA3Automation automation)
    {
        var window = application.GetMainWindow(automation, WindowTimeout)
            ?? throw new XunitException(
                $"Could not attach to the WPF shell main window within {WindowTimeout.TotalSeconds:0}s. "
                + (application.HasExited
                    ? "The shell process exited."
                    : "The shell process is running but did not expose a main window. "
                        + "Verify that src/Aether.Shell/App.xaml opens MainWindow (StartupUri=\"MainWindow.xaml\")."));

        var titleDeadline = DateTime.UtcNow + ElementTimeout;
        while (!string.Equals(window.Title, WindowTitle, StringComparison.Ordinal)
            && DateTime.UtcNow < titleDeadline
            && !application.HasExited)
        {
            Thread.Sleep(200);
        }

        Assert.Equal(WindowTitle, window.Title);
        return window;
    }

    private static async Task<string> WaitForBackendModeAsync(
        Window window,
        AutomationElement backendMode,
        TimeSpan timeout)
    {
        var lastText = string.Empty;
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            lastText = ReadTextBlock(backendMode);
            if (lastText.Contains(ExpectedTransport, StringComparison.Ordinal))
            {
                return lastText;
            }

            if (lastText.Contains("unavailable", StringComparison.OrdinalIgnoreCase))
            {
                throw new XunitException(
                    "The shell reported an unavailable backend ('" + lastText + "')." + Environment.NewLine
                    + "Status:" + Environment.NewLine + TryReadStatus(window));
            }

            await Task.Delay(PollInterval, CancellationToken.None);
        }

        throw new XunitException(
            $"Backend mode did not contain '{ExpectedTransport}' within {timeout.TotalSeconds:0}s. "
            + $"Last value: '{lastText}'." + Environment.NewLine
            + "Status:" + Environment.NewLine + TryReadStatus(window));
    }

    private static async Task<string> WaitForTextAsync(
        Func<string> readText,
        Func<string, bool> isComplete,
        TimeSpan timeout)
    {
        var lastText = string.Empty;
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            lastText = readText();
            if (isComplete(lastText))
            {
                return lastText;
            }

            await Task.Delay(PollInterval, CancellationToken.None);
        }

        return lastText;
    }

    private static bool IsTerminalFailure(string statusText) =>
        statusText.Contains("Workflow failed", StringComparison.Ordinal)
        || statusText.Contains("Workflow denied", StringComparison.Ordinal)
        || statusText.Contains("Workflow cancelled", StringComparison.Ordinal)
        || statusText.Contains("Cannot run", StringComparison.Ordinal);

    private static void EnsureRestrictedClassification(ComboBox comboBox)
    {
        if (IsRestrictedSelected(comboBox))
        {
            return;
        }

        comboBox.Expand();
        try
        {
            comboBox.Select("RESTRICTED");
        }
        finally
        {
            try
            {
                comboBox.Collapse();
            }
            catch (Exception)
            {
                // The drop-down may already be closed; the selection assertion below decides.
            }
        }

        Assert.True(
            IsRestrictedSelected(comboBox),
            "The classification combo could not be set to RESTRICTED; the workflow must run restricted.");
    }

    private static bool IsRestrictedSelected(ComboBox comboBox) =>
        string.Equals(comboBox.SelectedItem?.Name, "RESTRICTED", StringComparison.Ordinal);

    private static void SelectTab(Window window, string header)
    {
        var tabItem = window.FindFirstDescendant(cf => cf.ByName(header).And(cf.ByControlType(ControlType.TabItem)))
            ?? throw new XunitException($"The '{header}' tab was not found in the WPF shell.");
        var selectionItemPattern = tabItem.Patterns.SelectionItem.PatternOrDefault
            ?? throw new XunitException($"The '{header}' tab does not expose the selection item pattern.");
        selectionItemPattern.Select();
    }

    private static AutomationElement WaitForElement(Window window, string automationId, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        do
        {
            var element = window.FindFirstDescendant(cf => cf.ByAutomationId(automationId));
            if (element is not null)
            {
                return element;
            }

            Thread.Sleep(250);
        }
        while (DateTime.UtcNow < deadline);

        throw new XunitException(
            $"The WPF shell did not expose an element with AutomationId '{automationId}' within {timeout.TotalSeconds:0}s.");
    }

    private static string ReadTextBox(AutomationElement element) => element.AsTextBox().Text ?? string.Empty;

    private static string ReadTextBox(Window window, string automationId) =>
        ReadTextBox(WaitForElement(window, automationId, ElementTimeout));

    private static string ReadTextBlock(AutomationElement element)
    {
        // A WPF TextBlock without an explicit AutomationProperties.Name exposes its
        // rendered text as the UIA name. Fall back to the text pattern if that changes.
        var name = element.Name;
        if (!string.IsNullOrEmpty(name))
        {
            return name;
        }

        var textPattern = element.Patterns.Text.PatternOrDefault;
        return textPattern?.DocumentRange.GetText(-1) ?? string.Empty;
    }

    private static string TryReadStatus(Window window)
    {
        try
        {
            var statusBox = window.FindFirstDescendant(cf => cf.ByAutomationId("StatusBox"));
            return statusBox is null ? "<StatusBox was not found>" : ReadTextBox(statusBox);
        }
        catch (Exception exception)
        {
            return "<StatusBox could not be read: " + exception.Message + ">";
        }
    }

    private static void CloseShell(Application? application)
    {
        if (application is null)
        {
            return;
        }

        try
        {
            if (!application.HasExited)
            {
                application.Close(killIfCloseFails: true);
            }
        }
        catch (Exception)
        {
            // Cleanup must never mask the actual test failure.
        }
        finally
        {
            try
            {
                if (!application.HasExited)
                {
                    application.Kill();
                }
            }
            catch (Exception)
            {
                // The process may have exited between the check and the kill.
            }

            try
            {
                application.Dispose();
            }
            catch (Exception)
            {
                // Cleanup must never mask the actual test failure.
            }
        }
    }

    private static string DescribeHarness(EndToEndHarness harness)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Broker stderr:");
        builder.AppendLine(ReadTail(harness.BrokerStdErrFile));
        builder.AppendLine("Broker stdout:");
        builder.AppendLine(ReadTail(harness.BrokerStdOutFile));
        return builder.ToString();
    }

    private static string ReadTail(string path)
    {
        if (!File.Exists(path))
        {
            return "<missing: " + path + ">";
        }

        try
        {
            var lines = File.ReadAllLines(path);
            return string.Join(Environment.NewLine, lines.TakeLast(40));
        }
        catch (Exception exception)
        {
            return "<unreadable: " + exception.Message + ">";
        }
    }

    [GeneratedRegex("Trace\\s+(?<trace>[0-9a-fA-F]{32})", RegexOptions.CultureInvariant)]
    private static partial Regex TraceIdPattern();
}

/// <summary>Locates the compiled WPF shell executable for the current configuration.</summary>
internal static class ShellExecutableLocator
{
    private const string ShellExecutableOverrideVariable = "AETHER_SHELL_EXE";

    public static string Resolve()
    {
        var configured = Environment.GetEnvironmentVariable(ShellExecutableOverrideVariable);
        if (!string.IsNullOrWhiteSpace(configured))
        {
            var candidate = Path.IsPathRooted(configured)
                ? configured
                : Path.Combine(TestRepo.Root, configured);
            if (!File.Exists(candidate))
            {
                throw new FileNotFoundException(
                    $"{ShellExecutableOverrideVariable} points to '{candidate}', which does not exist.",
                    candidate);
            }

            return candidate;
        }

        var configuration = ResolveConfiguration();
        var path = Path.Combine(
            TestRepo.Root,
            "src",
            "Aether.Shell",
            "bin",
            configuration,
            "net10.0-windows",
            "Aether.Shell.exe");
        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                $"The built WPF shell was not found at '{path}'. Build src/Aether.Shell (or the solution) "
                + $"for the '{configuration}' configuration first, or set {ShellExecutableOverrideVariable}.",
                path);
        }

        return path;
    }

    private static string ResolveConfiguration()
    {
        var separator = Path.DirectorySeparatorChar;
        var baseDirectory = AppContext.BaseDirectory;
        if (baseDirectory.Contains($"{separator}bin{separator}Debug{separator}", StringComparison.OrdinalIgnoreCase))
        {
            return "Debug";
        }

        if (baseDirectory.Contains($"{separator}bin{separator}Release{separator}", StringComparison.OrdinalIgnoreCase))
        {
            return "Release";
        }

        return TestRepo.Configuration;
    }
}
