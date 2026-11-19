using System.Diagnostics;
using System.Runtime.InteropServices;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.UIA3;
using Xunit;
using Xunit.Sdk;

using Application = FlaUI.Core.Application;
using ComboBox = FlaUI.Core.AutomationElements.ComboBox;

namespace Aether.Desktop.E2E.Tests;

/// <summary>
/// UIA3 driver for the real WPF shell window (<c>AetherWorkspace</c>). Launches
/// <c>Aether.Shell.exe</c> with <c>AETHER_ENDPOINT_FILE</c> pointing at the named-pipe
/// broker handshake file and exposes bounded waits plus the automation ids documented in
/// <c>src/Aether.Shell/MainWindow.xaml</c>.
/// </summary>
/// <remarks>
/// Modeled on <c>tests/Aether.Shell.UiTests/ShellUiAutomationTests.cs</c> but dedicated to
/// the named-pipe reference-desktop scenario. All polling is capped (500 ms interval) and
/// bounded; a locked or non-interactive Windows session fails with an actionable message
/// instead of hanging. On non-Windows hosts the tests return before this type is created.
/// </remarks>
internal sealed class ShellWindowSession : IDisposable
{
    private const string WindowTitle = "AetherWorkspace";
    private const string BackendModeElement = "BackendModeText";
    private const string StatusElement = "StatusBox";
    private const string SummaryElement = "SummaryBox";
    private const string TraceElement = "TraceBox";
    private const string RunButtonElement = "RunButton";
    private const string CancelButtonElement = "CancelButton";
    private const string ClassificationElement = "ClassificationCombo";
    private const string CloudConsentElement = "CloudConsent";
    private const string CommandElement = "CommandBox";
    private const string StepsElement = "StepsList";

    private static readonly string[] CoreControls =
    [
        BackendModeElement,
        StatusElement,
        SummaryElement,
        CommandElement,
        RunButtonElement,
        CancelButtonElement,
        ClassificationElement,
        CloudConsentElement,
        StepsElement,
    ];

    internal static readonly TimeSpan WindowTimeout = TimeSpan.FromSeconds(60);
    internal static readonly TimeSpan ElementTimeout = TimeSpan.FromSeconds(15);
    internal static readonly TimeSpan BackendTimeout = TimeSpan.FromSeconds(45);
    internal static readonly TimeSpan RegistrationTimeout = TimeSpan.FromSeconds(45);
    internal static readonly TimeSpan WorkflowTimeout = TimeSpan.FromSeconds(180);
    internal static readonly TimeSpan SummaryTimeout = TimeSpan.FromSeconds(30);
    internal static readonly TimeSpan TraceTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(500);

    private readonly FlaUI.Core.Application _application;
    private readonly UIA3Automation _automation;

    private ShellWindowSession(FlaUI.Core.Application application, UIA3Automation automation, Window window)
    {
        _application = application;
        _automation = automation;
        Window = window;
    }

    /// <summary>The attached UIA main window.</summary>
    public Window Window { get; }

    /// <summary>Launches the compiled WPF shell and attaches to its main window.</summary>
    public static ShellWindowSession Launch(string endpointFile, bool emptyInstruction = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(endpointFile);
        var executable = DesktopExecutableLocator.ResolveShell();
        var startInfo = new System.Diagnostics.ProcessStartInfo
        {
            FileName = executable,
            WorkingDirectory = Path.GetDirectoryName(executable) ?? AppContext.BaseDirectory,
            UseShellExecute = false,
        };
        startInfo.Environment["AETHER_ENDPOINT_FILE"] = Path.GetFullPath(endpointFile);
        if (emptyInstruction)
        {
            startInfo.Environment["AETHER_DEMO_EMPTY_INSTRUCTION"] = "1";
        }

        var application = Application.Launch(startInfo);
        var automation = new UIA3Automation();
        try
        {
            var window = application.GetMainWindow(automation, WindowTimeout)
                ?? throw new XunitException(
                    $"The WPF shell did not expose a main window within {WindowTimeout.TotalSeconds:0}s. "
                    + (application.HasExited
                        ? $"Aether.Shell.exe exited during startup with code {application.ExitCode}."
                        : "The process is running but no window was exposed; verify StartupUri=MainWindow.xaml.")
                    + " The reference-desktop tests require an interactive, unlocked Windows session;"
                    + " a locked or Session 0 host cannot be driven through UI Automation.");
            return new ShellWindowSession(application, automation, WaitForStableTitle(application, window));
        }
        catch
        {
            automation.Dispose();
            try
            {
                if (!application.HasExited)
                {
                    application.Kill();
                }
            }
            catch (Exception)
            {
                // Cleanup must never mask the startup failure.
            }

            application.Dispose();
            throw;
        }
    }

    /// <summary>Waits for the backend label and returns it once it contains the given text.</summary>
    public string WaitForBackendModeContaining(string expectedText, TimeSpan timeout)
    {
        var backendMode = WaitForElement(BackendModeElement, ElementTimeout);
        return WaitForText(
            backendMode,
            text => text.Contains(expectedText, StringComparison.OrdinalIgnoreCase),
            timeout,
            $"backend label containing '{expectedText}'");
    }

    /// <summary>Asserts that every core shell control is reachable through UI Automation.</summary>
    public void AssertCoreControlsReachable()
    {
        foreach (var automationId in CoreControls)
        {
            _ = WaitForElement(automationId, ElementTimeout);
        }
    }

    /// <summary>Polls the status box until the predicate matches; returns the last text.</summary>
    public string WaitForStatus(Func<string, bool> isComplete, TimeSpan timeout, string description)
    {
        ArgumentNullException.ThrowIfNull(isComplete);
        var element = WaitForElement(StatusElement, ElementTimeout);
        return WaitForText(element, isComplete, timeout, description);
    }

    /// <summary>Polls the summary box until the predicate matches; returns the last text.</summary>
    public string WaitForSummary(Func<string, bool> isComplete, TimeSpan timeout, string description)
    {
        ArgumentNullException.ThrowIfNull(isComplete);
        var element = WaitForElement(SummaryElement, ElementTimeout);
        return WaitForText(element, isComplete, timeout, description);
    }

    /// <summary>Selects the trace/audit tab and waits for trace text matching the predicate.</summary>
    public string WaitForTrace(Func<string, bool> isComplete, TimeSpan timeout, string description)
    {
        ArgumentNullException.ThrowIfNull(isComplete);
        SelectTab("Trace / audit");
        var element = WaitForElement(TraceElement, ElementTimeout);
        return WaitForText(element, isComplete, timeout, description);
    }

    /// <summary>Reads the current status box text.</summary>
    public string ReadStatus() => ReadText(WaitForElement(StatusElement, ElementTimeout));

    /// <summary>Reads the current command box text.</summary>
    public string ReadCommand() => ReadText(WaitForElement(CommandElement, ElementTimeout));

    /// <summary>Reads the current summary box text.</summary>
    public string ReadSummary() => ReadText(WaitForElement(SummaryElement, ElementTimeout));

    /// <summary>Reads the current backend label text.</summary>
    public string ReadBackendMode() => ReadText(WaitForElement(BackendModeElement, ElementTimeout));

    /// <summary>Reads the selected classification display name (or an empty string).</summary>
    public string ReadSelectedClassification() =>
        WaitForElement(ClassificationElement, ElementTimeout).AsComboBox().SelectedItem?.Name ?? string.Empty;

    /// <summary>True when the cloud opt-in consent checkbox is checked through UI Automation.</summary>
    public bool IsCloudConsentChecked() =>
        WaitForElement(CloudConsentElement, ElementTimeout).AsCheckBox().IsChecked == true;

    /// <summary>
    /// Makes RESTRICTED explicit. The XAML selects RESTRICTED by default; if a future change
    /// picks another default, the combo is repaired through UI Automation so the privacy
    /// preconditions of the reference workflow remain explicit.
    /// </summary>
    public void EnsureRestrictedClassification()
    {
        var comboBox = WaitForElement(ClassificationElement, ElementTimeout).AsComboBox();
        if (IsRestricted(comboBox))
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
                // The drop-down may already be closed; the assertion below decides.
            }
        }

        Assert.True(
            IsRestricted(WaitForElement(ClassificationElement, ElementTimeout).AsComboBox()),
            "The classification combo could not be set to RESTRICTED; the reference workflow must run restricted.");
    }

    /// <summary>Sets the trusted source classification through UIA.</summary>
    public void SetClassification(string wireName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(wireName);
        var comboBox = WaitForElement(ClassificationElement, ElementTimeout).AsComboBox();
        comboBox.Expand();
        try
        {
            comboBox.Select(wireName);
        }
        finally
        {
            try { comboBox.Collapse(); } catch (Exception) { }
        }

        Assert.Equal(
            wireName,
            WaitForElement(ClassificationElement, ElementTimeout).AsComboBox().SelectedItem?.Name ?? string.Empty);
    }

    /// <summary>Selects deterministic (index 0) or LLM-assisted (index 1) planning mode.</summary>
    public void SetPlanningMode(bool llmAssisted)
    {
        var comboBox = WaitForElement("PlannerCombo", ElementTimeout).AsComboBox();
        comboBox.Expand();
        try
        {
            comboBox.Select(llmAssisted ? "LLM-assisted (PUBLIC, consent)" : "Deterministic (offline)");
        }
        finally
        {
            try { comboBox.Collapse(); } catch (Exception) { }
        }

        Assert.Equal(
            llmAssisted ? "LLM-assisted (PUBLIC, consent)" : "Deterministic (offline)",
            WaitForElement("PlannerCombo", ElementTimeout).AsComboBox().SelectedItem?.Name ?? string.Empty);
    }

    /// <summary>Sets the cloud consent checkbox to the requested state through UIA.</summary>
    public void SetCloudConsent(bool consented)
    {
        var deadline = DateTime.UtcNow + ElementTimeout;
        Exception? lastError = null;
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                var checkBox = WaitForElement(CloudConsentElement, ElementTimeout).AsCheckBox();
                if ((checkBox.IsChecked == true) == consented)
                {
                    return;
                }

                // TogglePattern is more reliable than Click() when the shell window is not the
                // foreground window (the local capture gate runs minimized to keep the terminal
                // out of the recording).
                var toggle = checkBox.Patterns.Toggle.PatternOrDefault;
                if (toggle is not null)
                {
                    toggle.Toggle();
                }
                else
                {
                    checkBox.Click();
                }

                Thread.Sleep(150);
                if ((WaitForElement(CloudConsentElement, ElementTimeout).AsCheckBox().IsChecked == true) == consented)
                {
                    return;
                }
            }
            catch (Exception exception)
            {
                lastError = exception;
            }
        }

        Assert.Fail($"The cloud consent checkbox could not be set to {consented} within {ElementTimeout.TotalSeconds:0}s."
            + (lastError is null ? string.Empty : " Last error: " + lastError.Message));
    }

    /// <summary>Replaces the command text through UIA.</summary>
    public void SetCommand(string command)
    {
        ArgumentNullException.ThrowIfNull(command);
        WaitForElement(CommandElement, ElementTimeout).AsTextBox().Text = command;
    }

    /// <summary>Reads the trace/audit panel text (selects the tab first).</summary>
    public string ReadTrace()
    {
        SelectTraceTab();
        return ReadText(WaitForElement(TraceElement, ElementTimeout));
    }

    /// <summary>Selects the Trace / audit tab through UIA.</summary>
    public void SelectTraceTab()
    {
        var tabs = Window.FindAllDescendants(cf => cf.ByControlType(ControlType.TabItem));
        var traceTab = tabs.FirstOrDefault(tab => string.Equals(tab.Name, "Trace / audit", StringComparison.Ordinal));
        if (traceTab is null)
        {
            return;
        }

        var selection = traceTab.Patterns.SelectionItem.PatternOrDefault;
        selection?.Select();
    }

    /// <summary>Waits until the Run button is enabled, then clicks/invokes it.</summary>
    public void ClickRun()
    {
        var runButton = WaitForElement(RunButtonElement, ElementTimeout).AsButton();
        var deadline = DateTime.UtcNow + ElementTimeout;
        while (!runButton.IsEnabled && DateTime.UtcNow < deadline)
        {
            Thread.Sleep(200);
            runButton = WaitForElement(RunButtonElement, ElementTimeout).AsButton();
        }

        Assert.True(runButton.IsEnabled, "The Run workflow button stayed disabled; the shell is not ready to run the reference workflow.");
        runButton.Invoke();
    }

    /// <summary>Invokes the compact demo-layout action (broker demo-grid arrangement).</summary>
    public void ClickDemoLayout()
    {
        var button = WaitForElement("DemoLayoutButton", ElementTimeout).AsButton();
        button.Invoke();
    }

    /// <summary>
    /// Replaces the instruction text progressively so a local demo capture shows a human-like typing
    /// cadence instead of an instant paste. Automation remains the same UIA TextBox as the user sees.
    /// </summary>
    public void TypeCommandGradually(string command, int charactersPerStep = 2, int delayMilliseconds = 35)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(command);
        var box = WaitForElement(CommandElement, ElementTimeout).AsTextBox();
        box.Text = string.Empty;
        for (var length = Math.Min(charactersPerStep, command.Length); length < command.Length; length += charactersPerStep)
        {
            box.Text = command[..length];
            Thread.Sleep(delayMilliseconds);
        }

        box.Text = command;
    }

    /// <summary>Minimizes the main Shell window so demo app windows are fully visible in the capture.</summary>
    public void MinimizeMainWindow()
    {
        var handle = WaitForMainWindowHandle();
        if (!ShowWindow(handle, SwMinimize))
        {
            throw new Xunit.Sdk.XunitException("The Shell main window could not be minimized for the demo capture.");
        }
    }

    private IntPtr WaitForMainWindowHandle()
    {
        var deadline = DateTime.UtcNow + ElementTimeout;
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                var process = Process.GetProcessById(_application.ProcessId);
                if (process.MainWindowHandle != IntPtr.Zero)
                {
                    return process.MainWindowHandle;
                }
            }
            catch (ArgumentException)
            {
            }

            Thread.Sleep(100);
        }

        throw new Xunit.Sdk.XunitException("The Shell main window handle was not available for the demo capture.");
    }

    private const int SwMinimize = 6;

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    /// <summary>Loads the v1.5.1 WPF+WebView2 demo preset through the real Shell UI.</summary>
    public void ClickWebView2DemoPreset()
    {
        var button = WaitForElement("DemoPresetButton", ElementTimeout).AsButton();
        button.Invoke();
    }

    /// <summary>Reads the trusted launch-selection mode shown by the Shell.</summary>
    public string ReadSelectionMode() =>
        WaitForElement("SelectionModeCombo", ElementTimeout).AsComboBox().SelectedItem?.Name ?? string.Empty;

    /// <summary>Finds a descendant by automation id, returning null when absent.</summary>
    public AutomationElement? TryFindElement(string automationId) =>
        Window.FindFirstDescendant(cf => cf.ByAutomationId(automationId));

    /// <summary>Finds a descendant by automation id, polling until the timeout expires.</summary>
    public AutomationElement WaitForElement(string automationId, TimeSpan timeout)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(automationId);
        var deadline = DateTime.UtcNow + timeout;
        do
        {
            var element = TryFindElement(automationId);
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

    public void Dispose()
    {
        try
        {
            if (!_application.HasExited)
            {
                _application.Close(killIfCloseFails: true);
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
                if (!_application.HasExited)
                {
                    _application.Kill();
                }
            }
            catch (Exception)
            {
                // The process may have exited between the check and the kill.
            }

            try
            {
                _automation.Dispose();
            }
            catch (Exception)
            {
                // Cleanup must never mask the actual test failure.
            }

            try
            {
                _application.Dispose();
            }
            catch (Exception)
            {
                // Cleanup must never mask the actual test failure.
            }
        }
    }

    private static Window WaitForStableTitle(FlaUI.Core.Application application, Window window)
    {
        var deadline = DateTime.UtcNow + ElementTimeout;
        while (!string.Equals(window.Title, WindowTitle, StringComparison.Ordinal)
            && DateTime.UtcNow < deadline
            && !application.HasExited)
        {
            Thread.Sleep(200);
        }

        if (!string.Equals(window.Title, WindowTitle, StringComparison.Ordinal))
        {
            throw new XunitException(
                $"The attached main window title is '{window.Title}', expected '{WindowTitle}'."
                + " Verify that the compiled Aether.Shell version matches the source tree.");
        }

        return window;
    }

    private void SelectTab(string header)
    {
        var tabItem = Window.FindFirstDescendant(cf => cf.ByName(header).And(cf.ByControlType(ControlType.TabItem)))
            ?? throw new XunitException($"The '{header}' tab was not found in the WPF shell.");
        var selectionItemPattern = tabItem.Patterns.SelectionItem.PatternOrDefault
            ?? throw new XunitException($"The '{header}' tab does not expose the selection item pattern.");
        selectionItemPattern.Select();
    }

    private static string WaitForText(
        AutomationElement element,
        Func<string, bool> isComplete,
        TimeSpan timeout,
        string description)
    {
        var lastText = string.Empty;
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            lastText = ReadText(element);
            if (isComplete(lastText))
            {
                return lastText;
            }

            Thread.Sleep(PollInterval);
        }

        throw new XunitException(
            $"The WPF shell did not reach {description} within {timeout.TotalSeconds:0}s."
            + Environment.NewLine
            + "Last text: " + lastText);
    }

    private static string ReadText(AutomationElement element)
    {
        // Editable WPF text boxes expose the value/text pattern; TextBlocks expose their
        // rendered content as the UIA name. Fall back to the text pattern for both.
        if (element.ControlType == ControlType.Edit)
        {
            return element.AsTextBox().Text ?? string.Empty;
        }

        if (!string.IsNullOrEmpty(element.Name))
        {
            return element.Name;
        }

        var textPattern = element.Patterns.Text.PatternOrDefault;
        return textPattern?.DocumentRange.GetText(-1) ?? string.Empty;
    }

    private static bool IsRestricted(FlaUI.Core.AutomationElements.ComboBox comboBox) =>
        string.Equals(comboBox.SelectedItem?.Name, "RESTRICTED", StringComparison.Ordinal);
}
