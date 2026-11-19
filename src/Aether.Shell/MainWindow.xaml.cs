using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.IO;
using System.Text.Json;
using System.Windows;
using Aether.AI.Planner;
using Aether.AI.Providers;
using Aether.AI.Summarization;
using Aether.Broker.Client;
using Aether.Contracts;
using Aether.Contracts.Broker;
using Aether.Contracts.Instructions;
using Aether.Contracts.Models;
using Aether.Contracts.Models.Diagnostics;
using Aether.Execution.Authorization;
using Aether.Execution.Executor;
using Aether.Execution.Results;
using Aether.Execution.Validation;
using Aether.Security.Audit;
using Aether.Security.Classification;
using Aether.Security.Diagnostics;
using Aether.Security.Egress;
using Aether.Security.Policy;
using Aether.Security.Secrets;
using Aether.Workflow;

namespace Aether.Shell;

/// <summary>
/// Minimal WPF shell: command bar, visible backend/consent/classification state,
/// cancel action, per-step status and a redacted trace/audit view. The shell holds
/// no authoritative policy logic; authorization and execution are delegated to the
/// deterministic executor and broker.
/// </summary>
public partial class MainWindow : Window
{
    private readonly ObservableCollection<StepRow> _steps = [];
    private readonly ObservableCollection<InspectorRow> _inspector = [];
    private readonly ObservableCollection<AppRow> _apps = [];
    private readonly Dictionary<string, int> _appProcessIds = new(StringComparer.Ordinal);
    private readonly InstructionTemplateStore _instructionStore = new();
    private List<InstructionTemplate> _templates = [];
    private string? _activeTemplateId;
    private bool _suppressTemplateSelection;
    private bool _catalogInitialized;
    private readonly HashChainAuditLog _auditSink = new();
    private BrokerClient? _client;
    private BrokerEndpointInfo? _endpoint;
    private AetherWorkflowOrchestrator? _orchestrator;
    private CancellationTokenSource? _runCancellation;
    private bool _running;
    private WorkflowResult? _lastResult;
    private readonly UiFrameTelemetry _uiTelemetry = new();
    private UiTelemetrySnapshot? _lastTelemetry;

    public MainWindow()
    {
        InitializeComponent();
        StepsList.ItemsSource = _steps;
        InspectorList.ItemsSource = _inspector;
        AppsGrid.ItemsSource = _apps;
        Loaded += OnLoaded;
        Closed += OnClosed;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        LoadInstructionTemplates();
        try
        {
            _endpoint = LoadEndpoint();
            BackendModeText.Text = $"{_endpoint.Transport} (local IPC only)";
            await ConnectAsync();
            await RefreshApplicationsAsync();
        }
        catch (Exception exception)
        {
            BackendModeText.Text = "unavailable";
            AppendStatus("Shell could not connect to the local broker: " + exception.Message);
            AppendStatus("Start Aether.BrokerApp first, or set AETHER_ENDPOINT_FILE to the handshake file path.");
        }
    }

    private async Task ConnectAsync()
    {
        if (_endpoint is null)
        {
            throw new InvalidOperationException("Broker endpoint is not configured.");
        }

        _client = new BrokerClient(BrokerChannelFactory.CreateChannel(_endpoint.Transport, _endpoint.Endpoint));
        await _client.RegisterAsync(_endpoint.OperatorAppId, _endpoint.OperatorToken, "shell-wpf-" + Environment.ProcessId);
        AppendStatus($"Registered with broker as { _endpoint.OperatorAppId} (session expiry follows broker policy).");
        _orchestrator = BuildOrchestrator(_client, DeterministicCommandPlanner.DefaultChartAppId);
    }

    private static ICloudModelTransport CreateCloudTransport(ModelProviderSettings settings)
    {
        // Match the headless provider resolution: OPENAI_* by default, GEMINI_* for Gemini, with the
        // generic AETHER_CLOUD_* names still accepted as endpoint/model fallbacks by the adapter.
        var secrets = SecretProviderFactory.Create();
        return settings.ProviderId == "gemini"
            ? new GeminiCloudModelTransport(
                secrets,
                baseUrl: settings.ReadEndpoint(),
                model: settings.Model,
                apiKeyEnvironmentVariable: settings.ApiKeyEnvironmentVariable)
            : new HttpCloudModelTransport(
                secrets,
                endpoint: settings.ReadEndpoint(),
                modelId: settings.Model,
                apiKeyEnvironmentVariable: settings.ApiKeyEnvironmentVariable);
    }

    private AetherWorkflowOrchestrator BuildOrchestrator(
        BrokerClient client,
        string chartAppId,
        Action<WorkflowPhase>? phaseObserver = null)
    {
        var policy = PolicyDocument.CreateDefault().EnsureValid();
        var policyEngine = new PolicyEngine(policy);
        var egressGateway = new EgressGateway(policyEngine, _auditSink);
        var detector = new RuleBasedSensitiveEntityDetector();
        var classifier = new SourceFirstClassifier([detector]);
        var providerCatalog = ModelProviderCatalog.FromEnvironment();
        var summarySettings = providerCatalog.Summarization;
        ISummaryProvider localSummary = summarySettings.ProviderId == "local" && summarySettings.IsValid
            ? new LocalModelSummaryProvider(
                new LoopbackLocalModelTransport(summarySettings.ReadEndpoint(), summarySettings.Model),
                egressGateway)
            : new LocalDeterministicSummaryProvider(detector);
        ISummaryProvider? cloudSummary = summarySettings.IsCloud
            && summarySettings.IsValid
            && CloudConsentCheck.IsChecked == true
                ? new CloudSummaryProvider(
                    CreateCloudTransport(summarySettings),
                    egressGateway,
                    new PlanProviderOptions(CloudEnabled: true, CloudConsentGranted: true))
                : null;
        var summaryService = new SummaryService(
            localSummary,
            cloudSummary,
            cloudSummary is null ? SummaryServiceOptions.LocalOnly : new SummaryServiceOptions(PreferCloudForPublic: true));
        var validator = new ActionPlanValidator(new ActionPlanValidationOptions { MaxActions = 6 });
        var options = new WorkflowOrchestratorOptions
        {
            MaxActionsPerPlan = 6,
            PlanningMode = PlannerCombo.SelectedIndex == 1
                ? PlanGenerationMode.CloudPreferred
                : PlanGenerationMode.DeterministicPreferred,
            Execution = new ExecutionOptions { MaxActionsPerPlan = 6, StepTimeout = TimeSpan.FromSeconds(20) },
            PhaseObserver = phaseObserver,
        };

        IPlanGenerator? modelPlanner = null;
        if (PlannerCombo.SelectedIndex == 1)
        {
            var plannerSettings = providerCatalog.Planner;
            if (plannerSettings.ProviderId == "local" && plannerSettings.IsValid)
            {
                modelPlanner = new LocalModelPlanProvider(
                    new LoopbackLocalModelTransport(),
                    egressGateway);
            }
            else if (plannerSettings.IsCloud
                     && plannerSettings.IsValid
                     && CloudConsentCheck.IsChecked == true)
            {
                modelPlanner = new CloudPlanProvider(
                    CreateCloudTransport(plannerSettings),
                    egressGateway,
                    new PlanProviderOptions(CloudEnabled: true, CloudConsentGranted: true));
            }
        }

        return new AetherWorkflowOrchestrator(
            classifier,
            new DeterministicCommandPlanner(chartAppId),
            modelPlanner,
            new BrokerClientWorkflowBroker(client),
            summaryService,
            validator,
            new CapabilityPlanAuthorizationPolicy(),
            options,
            _auditSink);
    }

    private async void OnRunClick(object sender, RoutedEventArgs e)
    {
        if (_running)
        {
            return;
        }

        if (_client is null)
        {
            try
            {
                await ConnectAsync();
            }
            catch (Exception exception)
            {
                AppendStatus("Cannot run: broker connection failed: " + exception.Message);
                return;
            }
        }

        if (_client is null)
        {
            return;
        }

        var selectedAppIds = _apps
            .Where(app => app.IsSelected && app.Launchable)
            .Select(app => app.AppId)
            .ToArray();
        var chartAppId = selectedAppIds.Contains(DeterministicCommandPlanner.WebView2ChartAppId, StringComparer.Ordinal)
                         && !selectedAppIds.Contains(DeterministicCommandPlanner.DefaultChartAppId, StringComparer.Ordinal)
            ? DeterministicCommandPlanner.WebView2ChartAppId
            : DeterministicCommandPlanner.DefaultChartAppId;
        var selectionMode = SelectionModeCombo.SelectedIndex == 1
            ? LaunchSelectionMode.Strict
            : LaunchSelectionMode.Preferred;
        var revision = DateTimeOffset.UtcNow.UtcTicks.ToString(CultureInfo.InvariantCulture);
        var launchSelection = new LaunchSelectionContext(selectionMode, new HashSet<string>(selectedAppIds, StringComparer.Ordinal), revision);

        // Planner/selections are UI state that can change after the initial connection; rebuild per run.
        _orchestrator = BuildOrchestrator(
            _client,
            chartAppId,
            phase => Dispatcher.BeginInvoke(new Action(() => PhaseText.Text = phase.ToString().ToUpperInvariant())));

        if (!DataClassificationExtensions.TryParseWire(SelectedClassification(), out var classification))
        {
            classification = DataClassification.Unknown;
        }

        var plannerLabel = PlannerCombo.SelectedIndex == 1 ? "LLM-assisted (PUBLIC, consent)" : "Deterministic (offline)";

        SetRunningState(true);
        _steps.Clear();
        SummaryBox.Text = string.Empty;
        PlanPreviewBox.Text = string.Empty;
        PlanPreviewLabel.Text = "Planning…";
        if (_lastTelemetry is not null)
        {
            RenderTelemetry(_lastTelemetry);
        }

        TraceBox.Text = string.Empty;
        StatusBox.Text = string.Empty;
        AppendStatus($"Run context: planner={plannerLabel}; classification={classification.ToWireName()}; " +
                     $"externalConsent={CloudConsentCheck.IsChecked == true}; selection={selectionMode} " +
                     $"allowedApps=[{string.Join(", ", selectedAppIds.Order(StringComparer.Ordinal))}].");
        PhaseText.Text = WorkflowPhase.Classifying.ToString().ToUpperInvariant();
        _runCancellation = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        _uiTelemetry.Start();
        UiTelemetryText.Text = "ui telemetry: capturing…";
        try
        {
            var request = new WorkflowRequest(
                CommandBox.Text,
                classification,
                LaunchSelection: launchSelection);
            var result = await _orchestrator.ExecuteAsync(request, _runCancellation.Token);
            _lastTelemetry = _uiTelemetry.Stop(result.DurationMs);
            CaptureWorkflowProcessIds(result);
            RenderProcessDetails(result);
            PhaseText.Text = result.Success ? "COMPLETED" : "DENIED";
            RenderPlanPreview(result);
            RenderResult(result);
            WriteTelemetryIfConfigured(result, _lastTelemetry);
        }
        catch (OperationCanceledException)
        {
            PhaseText.Text = "CANCELLED";
            AppendStatus("Workflow cancelled by the operator.");
        }
        catch (Exception exception)
        {
            PhaseText.Text = "FAILED";
            AppendStatus("Workflow failed: " + exception.Message);
        }
        finally
        {
            _lastTelemetry ??= _uiTelemetry.Stop(0);
            RenderTelemetry(_lastTelemetry);
            SetRunningState(false);
            _runCancellation?.Dispose();
            _runCancellation = null;
        }
    }

    private async Task RefreshApplicationsAsync()
    {
        if (_client is null)
        {
            return;
        }

        try
        {
            var traceId = TraceIdGenerator.NewTraceId();
            var snapshot = await _client.GetSnapshotAsync(traceId);
            var health = await _client.HealthProbeAsync();
            var selected = _apps.Where(app => app.IsSelected).Select(app => app.AppId)
                .ToHashSet(StringComparer.Ordinal);
            var defaultSelection = new HashSet<string>(StringComparer.Ordinal)
            {
                "sample.watchlist", "sample.risk", DeterministicCommandPlanner.DefaultChartAppId,
            };
            var useDefaultSelection = !_catalogInitialized;
            _catalogInitialized = true;
            _apps.Clear();
            foreach (var app in snapshot.Apps.Where(app => app.Launchable).OrderBy(app => app.DisplayName, StringComparer.Ordinal))
            {
                var healthEntry = health.AppHealth.FirstOrDefault(entry =>
                    entry.StartsWith(app.AppId + "=", StringComparison.Ordinal));
                var status = healthEntry is null ? "STOPPED" : healthEntry[(app.AppId.Length + 1)..].ToUpperInvariant();
                _apps.Add(new AppRow(app.AppId, app.DisplayName, RuntimeTypeFor(app.AppId), status)
                {
                    IsSelected = selected.Count > 0
                        ? selected.Contains(app.AppId)
                        : useDefaultSelection && defaultSelection.Contains(app.AppId),
                    Launchable = app.Launchable,
                });
            }

            RenderProcessDetails(_lastResult);
            AppendStatus($"Application catalog refreshed: {_apps.Count} registered app(s), {health.AppHealth.Count} health entr(y/ies).");
        }
        catch (Exception exception)
        {
            AppendStatus("Could not refresh applications: " + exception.Message);
        }
    }

    private static string RuntimeTypeFor(string appId) => appId.Contains("webview2", StringComparison.OrdinalIgnoreCase)
        ? "WebView2"
        : appId.StartsWith("sample.", StringComparison.Ordinal) ? "WPF" : "desktop";

    private void LoadInstructionTemplates()
    {
        var result = _instructionStore.Load();
        if (!string.IsNullOrWhiteSpace(result.Error))
        {
            AppendStatus("Instruction library: " + result.Error + (result.QuarantinedPath is null ? "" : " Quarantined: " + result.QuarantinedPath));
        }

        _templates = result.Templates.ToList();
        _suppressTemplateSelection = true;
        SavedInstructionsCombo.ItemsSource = _templates;
        SavedInstructionsCombo.SelectedIndex = -1;
        _suppressTemplateSelection = false;
        _activeTemplateId = null;
    }

    private void LoadTemplateIntoEditor(InstructionTemplate template)
    {
        _activeTemplateId = template.Id;
        InstructionTitleBox.Text = template.Title;
        CommandBox.Text = template.Instruction;
        PhaseText.Text = "IDLE";
    }

    private void PopulateTemplateCombo(string? selectId)
    {
        _suppressTemplateSelection = true;
        SavedInstructionsCombo.ItemsSource = null;
        SavedInstructionsCombo.ItemsSource = _templates;
        SavedInstructionsCombo.SelectedItem = selectId is null
            ? null
            : _templates.FirstOrDefault(template => template.Id == selectId);
        _suppressTemplateSelection = false;
    }

    private void OnSavedInstructionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_suppressTemplateSelection || SavedInstructionsCombo.SelectedItem is not InstructionTemplate template)
        {
            return;
        }

        LoadTemplateIntoEditor(template);
    }

    private void OnNewInstructionClick(object sender, RoutedEventArgs e)
    {
        _activeTemplateId = null;
        InstructionTitleBox.Text = string.Empty;
        CommandBox.Text = string.Empty;
        PopulateTemplateCombo(null);
        PhaseText.Text = "IDLE";
        InstructionTitleBox.Focus();
    }

    private void OnSaveInstructionClick(object sender, RoutedEventArgs e)
    {
        var title = InstructionTitleBox.Text;
        var instruction = CommandBox.Text;
        if (!InstructionTemplateStore.TryValidate(title, instruction, out var error))
        {
            AppendStatus("Instruction not saved: " + error);
            return;
        }

        var existing = _activeTemplateId is null
            ? null
            : _templates.FirstOrDefault(template => template.Id == _activeTemplateId);
        var duplicateTitle = _templates.Any(template =>
            template.Id != existing?.Id
            && string.Equals(template.Title, title.Trim(), StringComparison.OrdinalIgnoreCase));
        if (duplicateTitle)
        {
            AppendStatus("Instruction not saved: a template with that title already exists.");
            return;
        }

        InstructionTemplate saved;
        if (existing is null)
        {
            saved = InstructionTemplateStore.NewTemplate(title, instruction);
            _templates.Add(saved);
        }
        else
        {
            saved = InstructionTemplateStore.WithUpdatedText(existing, title, instruction);
            var index = _templates.FindIndex(template => template.Id == existing.Id);
            _templates[index] = saved;
        }

        var saveResult = _instructionStore.Save(_templates);
        if (!saveResult.Success)
        {
            AppendStatus("Instruction not saved: " + saveResult.Error);
            return;
        }

        _activeTemplateId = saved.Id;
        PopulateTemplateCombo(saved.Id);
        AppendStatus($"Saved local instruction template '{saved.Title}' (user-scoped, not Git-tracked).");
    }

    private void OnDuplicateInstructionClick(object sender, RoutedEventArgs e)
    {
        var source = _templates.FirstOrDefault(template => template.Id == _activeTemplateId);
        if (source is null)
        {
            AppendStatus("Select a saved instruction to duplicate.");
            return;
        }

        var duplicate = InstructionTemplateStore.Duplicate(source);
        var uniqueTitle = duplicate.Title;
        var suffix = 2;
        while (_templates.Any(template => string.Equals(template.Title, uniqueTitle, StringComparison.OrdinalIgnoreCase)))
        {
            uniqueTitle = duplicate.Title + " " + suffix++;
        }

        duplicate = duplicate with { Title = uniqueTitle };
        _templates.Add(duplicate);
        var saveResult = _instructionStore.Save(_templates);
        if (!saveResult.Success)
        {
            _templates.Remove(duplicate);
            AppendStatus("Duplicate not saved: " + saveResult.Error);
            return;
        }

        _activeTemplateId = duplicate.Id;
        PopulateTemplateCombo(duplicate.Id);
        LoadTemplateIntoEditor(duplicate);
        AppendStatus($"Duplicated instruction as '{duplicate.Title}'.");
    }

    private void OnDeleteInstructionClick(object sender, RoutedEventArgs e)
    {
        var target = _templates.FirstOrDefault(template => template.Id == _activeTemplateId);
        if (target is null)
        {
            AppendStatus("Select a saved instruction to delete.");
            return;
        }

        if (MessageBox.Show($"Delete local template '{target.Title}'?", "AetherWorkspace",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return;
        }

        _templates.Remove(target);
        var saveResult = _instructionStore.Save(_templates);
        if (!saveResult.Success)
        {
            AppendStatus("Delete failed: " + saveResult.Error);
            return;
        }

        _activeTemplateId = null;
        PopulateTemplateCombo(null);
        InstructionTitleBox.Text = string.Empty;
        CommandBox.Text = string.Empty;
        AppendStatus($"Deleted local template '{target.Title}'.");
    }

    private async void OnRefreshAppsClick(object sender, RoutedEventArgs e) => await RefreshApplicationsAsync();

    private async void OnLaunchSelectedClick(object sender, RoutedEventArgs e)
    {
        if (_client is null)
        {
            AppendStatus("Broker is not connected.");
            return;
        }

        var selected = _apps.Where(app => app.IsSelected && app.Launchable).ToArray();
        if (selected.Length == 0)
        {
            AppendStatus("Select at least one application first.");
            return;
        }

        var launched = 0;
        foreach (var app in selected)
        {
            try
            {
                var reply = await _client.LaunchAppAsync(app.AppId, TraceIdGenerator.NewTraceId());
                if (reply.Accepted)
                {
                    launched++;
                    _appProcessIds[app.AppId] = reply.ProcessId;
                }
                else
                {
                    AppendStatus($"Launch {app.AppId} denied: {reply.DenialCode} {reply.DenialMessage}");
                }
            }
            catch (Exception exception)
            {
                AppendStatus($"Launch {app.AppId} failed: " + exception.Message);
            }
        }

        AppendStatus($"Launch Selected: {launched}/{selected.Length} accepted by the broker (not an AI workflow run).");
        await RefreshApplicationsAsync();
    }

    private AppRow? RowFromButton(object sender) => (sender as System.Windows.Controls.Button)?.Tag as AppRow;

    private async void OnLaunchAppClick(object sender, RoutedEventArgs e)
    {
        if (_client is null || RowFromButton(sender) is not { } app)
        {
            return;
        }

        try
        {
            var reply = await _client.LaunchAppAsync(app.AppId, TraceIdGenerator.NewTraceId());
            if (reply.Accepted)
            {
                _appProcessIds[app.AppId] = reply.ProcessId;
                app.Status = "STARTING";
                AppendStatus($"Launched {app.AppId} as PID {reply.ProcessId}; health will refresh.");
            }
            else
            {
                AppendStatus($"Launch denied: {reply.DenialCode} {reply.DenialMessage}");
            }

            await RefreshApplicationsAsync();
        }
        catch (Exception exception)
        {
            AppendStatus("Launch failed: " + exception.Message);
        }
    }

    private void OnFocusAppClick(object sender, RoutedEventArgs e)
    {
        if (RowFromButton(sender) is not { } app)
        {
            return;
        }

        if (!_appProcessIds.TryGetValue(app.AppId, out var processId))
        {
            AppendStatus($"Focus {app.AppId}: no process id recorded; launch it first.");
            return;
        }

        try
        {
            var process = Process.GetProcessById(processId);
            if (process.MainWindowHandle == IntPtr.Zero)
            {
                AppendStatus($"Focus {app.AppId}: process {processId} has no top-level window yet.");
                return;
            }

            ShowWindow(process.MainWindowHandle, 9);
            SetForegroundWindow(process.MainWindowHandle);
            AppendStatus($"Focused {app.AppId} (PID {processId}).");
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            AppendStatus($"Focus {app.AppId} failed: " + exception.Message);
        }
    }

    private async void OnStopAppClick(object sender, RoutedEventArgs e)
    {
        if (_client is null || RowFromButton(sender) is not { } app)
        {
            return;
        }

        try
        {
            var reply = await _client.StopAppAsync(app.AppId, instanceId: null, TraceIdGenerator.NewTraceId());
            AppendStatus(reply.Stopped
                ? $"Stop {app.AppId}: accepted."
                : $"Stop {app.AppId} denied: {reply.DenialCode} {reply.DenialMessage}");
            _appProcessIds.Remove(app.AppId);
            await RefreshApplicationsAsync();
        }
        catch (Exception exception)
        {
            AppendStatus("Stop failed: " + exception.Message);
        }
    }

    private async void OnRestartAppClick(object sender, RoutedEventArgs e)
    {
        if (_client is null || RowFromButton(sender) is not { } app)
        {
            return;
        }

        try
        {
            var reply = await _client.RestartAppAsync(app.AppId, TraceIdGenerator.NewTraceId());
            AppendStatus(reply.Accepted
                ? $"Restart {app.AppId}: accepted (instance {reply.InstanceId}); health will refresh."
                : $"Restart {app.AppId} denied: {reply.DenialCode} {reply.DenialMessage}");
            await RefreshApplicationsAsync();
        }
        catch (Exception exception)
        {
            AppendStatus("Restart failed: " + exception.Message);
        }
    }

    private void OnDemoPresetClick(object sender, RoutedEventArgs e)
    {
        foreach (var app in _apps)
        {
            app.IsSelected = app.AppId is "sample.watchlist" or "sample.risk" or DeterministicCommandPlanner.WebView2ChartAppId;
        }

        SelectionModeCombo.SelectedIndex = 1; // Strict
        PlannerCombo.SelectedIndex = 0;       // deterministic reference demo
        _activeTemplateId = null;
        PopulateTemplateCombo(null);
        InstructionTitleBox.Text = "WebView2 workspace demo";
        CommandBox.Text =
            "Open Northstar bond analytics, show the associated risk view, synchronize this instrument across the workspace and summarize the exposure privately.";
        PhaseText.Text = "PRESET";
        AppendStatus("WebView2 demo preset: watchlist + risk + WebView2 chart selected, Strict mode, deterministic planner. Click Run Instruction.");
    }

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    private void CaptureWorkflowProcessIds(WorkflowResult result)
    {
        if (result.Plan is null || result.Execution?.Steps is null)
        {
            return;
        }

        for (var index = 0; index < Math.Min(result.Plan.Steps.Count, result.Execution.Steps.Count); index++)
        {
            var step = result.Plan.Steps[index];
            var artifacts = result.Execution.Steps[index].Artifacts;
            if (!string.Equals(step.Action, ActionKinds.LaunchApp, StringComparison.Ordinal)
                || step.AppId is null
                || artifacts is null
                || !artifacts.TryGetValue("processId", out var processText)
                || !int.TryParse(processText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var processId))
            {
                continue;
            }

            _appProcessIds[step.AppId] = processId;
        }
    }

    private void RenderProcessDetails(WorkflowResult? result = null)
    {
        var lines = new List<string>
        {
            "Local transport: " + (_endpoint?.Transport ?? "not connected"),
            "Broker endpoint: " + (_endpoint?.Endpoint ?? "not connected"),
            "Operator identity: " + (_endpoint?.OperatorAppId ?? "not registered"),
            "Selection mode: " + (SelectionModeCombo.SelectedIndex == 1 ? "Strict" : "Preferred"),
        };
        if (result is not null)
        {
            lines.Add("Trace: " + result.TraceId);
            lines.Add("Planner: " + result.PlannerProvider + " (usedModel=" + result.UsedModel + ")");
            lines.Add("Plan: " + (result.PlanId ?? "none"));
            lines.Add("Result: " + (result.Success ? "success" : "denied/failed " + (result.DenialCode ?? "")));
        }

        lines.Add("Application process IDs:");
        foreach (var app in _apps.OrderBy(app => app.AppId, StringComparer.Ordinal))
        {
            lines.Add($"  {app.AppId}  status={app.Status}  pid={(_appProcessIds.TryGetValue(app.AppId, out var pid) ? pid.ToString(CultureInfo.InvariantCulture) : "n/a")}");
        }

        ProcessesBox.Text = string.Join(Environment.NewLine, lines);
    }

    private void RenderPlanPreview(WorkflowResult result)
    {
        if (result.Plan is null)
        {
            PlanPreviewLabel.Text = result.Success ? "No plan preview." : "Plan unavailable: " + (result.DenialCode ?? "denied");
            PlanPreviewBox.Text = string.Empty;
            return;
        }

        var lines = new List<string>
        {
            $"plan={result.Plan.PlanId} schema={result.Plan.SchemaVersion}",
            $"planner={result.PlannerProvider} usedModel={result.UsedModel}",
            result.Success ? "validation=PASS" : "validation=DENIED " + (result.DenialCode ?? ""),
        };
        lines.AddRange(result.Plan.Steps.Select((step, index) =>
            $"{index + 1}. {step.Action}" +
            (step.AppId is null ? "" : $" app={step.AppId}") +
            (step.ContextRef is null ? "" : $" context={step.ContextRef}") +
            (step.Layout is null ? "" : $" layout={step.Layout}") +
            (step.Mode is null ? "" : $" mode={step.Mode}")));
        PlanPreviewBox.Text = string.Join(Environment.NewLine, lines);
        PlanPreviewLabel.Text = "Validated plan preview (read-only; broker/PIDs/trace are in Inspector):";
    }

    private async void OnExportDiagnostics(object sender, RoutedEventArgs e)
    {
        if (_lastResult is null)
        {
            DiagnosticsStatusText.Text = "Run a workflow first.";
            return;
        }

        try
        {
            var result = _lastResult;
            var timeline = (result.Execution?.Steps ?? [])
                .Select((step, index) => new DiagnosticsTimelineEntry(index + 1, step.Action, step.State.ToString().ToLowerInvariant(), step.Code, null))
                .ToArray();
            IReadOnlyList<DiagnosticsApp> apps = [];
            if (_client is not null)
            {
                var snapshot = await _client.GetSnapshotAsync(result.TraceId);
                var health = await _client.HealthProbeAsync();
                apps = snapshot.Apps
                    .Select(app => new DiagnosticsApp(
                        app.AppId,
                        health.AppHealth.FirstOrDefault(entry => entry.StartsWith(app.AppId + "=", StringComparison.Ordinal)) ?? "UNKNOWN",
                        0))
                    .ToArray();
            }

            var entries = _auditSink.Snapshot();
            var verification = _auditSink.Verify();
            var counts = entries
                .GroupBy(entry => entry.EventType, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
            var cloud = CloudConsentCheck.IsChecked == true;
            var bundle = DiagnosticsBundleBuilder.Build(
                new DiagnosticsRun(
                    result.Success ? "succeeded" : "denied",
                    result.TraceId,
                    result.PlanId,
                    result.Classification.ToWireName(),
                    result.DenialCode,
                    result.DurationMs,
                    result.PlannerProvider,
                    result.UsedModel),
                timeline,
                apps,
                new DiagnosticsAudit(verification.EntryCount, verification.Valid, 0, true, counts),
                new DiagnosticsProviders(cloud ? "cloud-consent" : "local-private", "DeterministicLocal", null, null, null, cloud, cloud, false, false),
                offline: true,
                generatedAtUtc: DateTimeOffset.UtcNow);
            var path = DiagnosticsBundleBuilder.WriteFile(
                bundle,
                Path.Combine(Path.GetTempPath(), "aether-diagnostics-" + result.TraceId + ".json"));
            DiagnosticsStatusText.Text = "Saved: " + path;
            AppendStatus("Sanitized diagnostics exported to " + path + " (no telemetry upload).");
        }
        catch (Exception exception)
        {
            DiagnosticsStatusText.Text = "Export failed: " + exception.Message;
            AppendStatus("Diagnostics export failed: " + exception.Message);
        }
    }

    private async void OnDemoLayoutClick(object sender, RoutedEventArgs e)
    {
        if (_client is null)
        {
            AppendStatus("Broker is not connected; demo layout cannot be arranged.");
            return;
        }

        try
        {
            var area = SystemParameters.WorkArea;
            this.WindowState = System.Windows.WindowState.Normal;
            Left = area.Left;
            Top = area.Top;
            Width = Math.Max(720, area.Width);
            Height = 220;

            var reply = await _client.ArrangeWindowsAsync(Layouts.DemoGrid, TraceIdGenerator.NewTraceId());
            AppendStatus(reply.Applied
                ? $"Demo layout: {reply.Layout?.Placements.Count ?? 0} windows arranged below the command surface."
                : $"Demo layout denied: {reply.DenialCode} {reply.DenialMessage}");
        }
        catch (Exception exception)
        {
            AppendStatus("Demo layout failed: " + exception.Message);
        }
    }

    private void RenderTelemetry(UiTelemetrySnapshot? telemetry)
    {
        if (telemetry is null)
        {
            UiTelemetryText.Text = "ui telemetry: no frames captured";
            return;
        }

        if (telemetry.FrameCount == 0)
        {
            UiTelemetryText.Text = FormattableString.Invariant(
                $"ui telemetry: no frames captured command={telemetry.CommandDurationMs}ms ws={telemetry.WorkingSetMb:0.#}MB gc={telemetry.ManagedMemoryMb:0.#}MB");
            return;
        }

        // Machine-readable evidence must stay locale-independent (Russian Windows uses comma decimals).
        UiTelemetryText.Text = FormattableString.Invariant($"ui frames={telemetry.FrameCount} p50={telemetry.P50FrameMs:0.##}ms p95={telemetry.P95FrameMs:0.##}ms p99={telemetry.P99FrameMs:0.##}ms max={telemetry.MaxFrameMs:0.##}ms long={telemetry.LongFrames} command={telemetry.CommandDurationMs}ms ws={telemetry.WorkingSetMb:0.#}MB gc={telemetry.ManagedMemoryMb:0.#}MB");
    }

    private static void WriteTelemetryIfConfigured(WorkflowResult result, UiTelemetrySnapshot telemetry)
    {
        var path = Environment.GetEnvironmentVariable("AETHER_UI_TELEMETRY_FILE");
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            var line = JsonSerializer.Serialize(new
            {
                timestampUtc = DateTimeOffset.UtcNow,
                traceId = result.TraceId,
                success = result.Success,
                planner = result.PlannerProvider,
                usedModel = result.UsedModel,
                commandDurationMs = telemetry.CommandDurationMs,
                frameCount = telemetry.FrameCount,
                p50FrameMs = telemetry.P50FrameMs,
                p95FrameMs = telemetry.P95FrameMs,
                p99FrameMs = telemetry.P99FrameMs,
                maxFrameMs = telemetry.MaxFrameMs,
                longFrames = telemetry.LongFrames,
                workingSetMb = telemetry.WorkingSetMb,
                managedMemoryMb = telemetry.ManagedMemoryMb,
            });
            File.AppendAllText(path, line + Environment.NewLine);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        _runCancellation?.Cancel();
        AppendStatus("Cancellation requested; the executor will stop at the next step boundary.");
    }

    private void RenderResult(WorkflowResult result)
    {
        _lastResult = result;
        _inspector.Clear();
        var sequence = 1;
        _inspector.Add(new InspectorRow(sequence++, "Broker IPC", "connected", _endpoint?.Transport ?? "local"));
        _inspector.Add(new InspectorRow(sequence++, "Plan validated", result.Success ? "succeeded" : "denied", result.DenialCode ?? ""));
        foreach (var step in result.Execution?.Steps ?? [])
        {
            _inspector.Add(new InspectorRow(
                sequence++,
                ReadableAction(step.Action),
                step.State.ToString().ToLowerInvariant(),
                step.Code ?? ""));
        }

        if (!string.IsNullOrWhiteSpace(result.DenialCode))
        {
            _inspector.Add(new InspectorRow(sequence, "Policy denied", "denied", result.DenialCode));
        }

        foreach (var step in result.Execution?.Steps ?? [])
        {
            _steps.Add(new StepRow(
                step.Index + 1,
                $"{step.Action}",
                step.State.ToString(),
                step.Code ?? ""));
        }

        if (result.Summary is not null)
        {
            SummaryBox.Text = result.Summary.Text;
        }

        var verification = _auditSink.Verify();
        if (_lastTelemetry is not null)
        {
            RenderTelemetry(_lastTelemetry);
        }

        TraceBox.Text =
            $"trace: {result.TraceId}{Environment.NewLine}" +
            $"plan: {result.PlanId ?? "n/a"} ({result.PlannerProvider}, model={result.UsedModel}){Environment.NewLine}" +
            $"classification: {result.Classification.ToWireName()} ({string.Join(", ", result.ClassificationReasons)}){Environment.NewLine}" +
            $"duration: {result.DurationMs} ms{Environment.NewLine}" +
            $"audit: {verification.EntryCount} entries, chain valid={verification.Valid}{Environment.NewLine}" +
            (result.Summary is null
                ? ""
                : $"summary provider: {result.Summary.Provider}, output classification: {result.Summary.OutputClassification.ToWireName()}{Environment.NewLine}") +
            (result.DenialCode is null
                ? ""
                : $"denial: {result.DenialCode} ({result.DenialCategory}){Environment.NewLine}") +
            (result.PlanningNotes.Count == 0
                ? ""
                : "planning notes: " + string.Join("; ", result.PlanningNotes) + Environment.NewLine) +
            (_lastTelemetry is null || _lastTelemetry.FrameCount == 0
                ? ""
                : FormattableString.Invariant($"ui: frames={_lastTelemetry.FrameCount} p50={_lastTelemetry.P50FrameMs:0.##}ms p95={_lastTelemetry.P95FrameMs:0.##}ms p99={_lastTelemetry.P99FrameMs:0.##}ms max={_lastTelemetry.MaxFrameMs:0.##}ms long={_lastTelemetry.LongFrames} command={_lastTelemetry.CommandDurationMs}ms{Environment.NewLine}"));

        if (result.Success)
        {
            AppendStatus("Workflow succeeded. Trace " + result.TraceId);
        }
        else
        {
            AppendStatus($"Workflow denied: {result.DenialCode} ({result.DenialCategory}). {result.DenialMessage}");
        }
    }

    private void SetRunningState(bool running)
    {
        _running = running;
        RunButton.IsEnabled = !running;
        CancelButton.IsEnabled = running;
        CommandBox.IsEnabled = !running;
        ClassificationCombo.IsEnabled = !running;
        CloudConsentCheck.IsEnabled = !running;
        PlannerCombo.IsEnabled = !running;
        SelectionModeCombo.IsEnabled = !running;
        NewInstructionButton.IsEnabled = !running;
        SaveInstructionButton.IsEnabled = !running;
        DuplicateInstructionButton.IsEnabled = !running;
        DeleteInstructionButton.IsEnabled = !running;
        SavedInstructionsCombo.IsEnabled = !running;
        InstructionTitleBox.IsEnabled = !running;
        AppsGrid.IsEnabled = !running;
        DemoPresetButton.IsEnabled = !running;
        DemoLayoutButton.IsEnabled = !running;
    }

    private string SelectedClassification() =>
        (ClassificationCombo.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Content?.ToString() ?? "UNKNOWN";

    private void AppendStatus(string message) =>
        StatusBox.AppendText($"[{DateTimeOffset.Now:HH:mm:ss}] {message}{Environment.NewLine}");

    private static BrokerEndpointInfo LoadEndpoint()
    {
        var configured = Environment.GetEnvironmentVariable("AETHER_ENDPOINT_FILE");
        var candidates = new List<string>();
        if (!string.IsNullOrWhiteSpace(configured))
        {
            candidates.Add(configured);
        }

        candidates.Add(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AetherWorkspace",
            "broker-endpoint.json"));
        candidates.Add(Path.Combine(Environment.CurrentDirectory, ".aether", "demo", "broker-endpoint.json"));

        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
            {
                return BrokerEndpointInfo.Read(candidate);
            }
        }

        throw new FileNotFoundException("Broker handshake file not found. Start the broker with --endpoint-file and set AETHER_ENDPOINT_FILE.");
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _runCancellation?.Cancel();
        _client?.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    private sealed record StepRow(int Index, string Action, string State, string Code);

    private static string ReadableAction(string action) => action switch
    {
        "LaunchApp" => "Apps registered",
        "PublishContext" => "Context published",
        "ArrangeWindows" => "Layout completed",
        "SummarizeContext" => "Summary produced",
        "ValidatePlan" => "Plan validated",
        _ => action,
    };
}

public sealed record InspectorRow(int Sequence, string Action, string State, string Code);

/// <summary>One selectable app row in the AI Command Center; selection is trusted Shell UI state.</summary>
public sealed class AppRow : INotifyPropertyChanged
{
    private bool _isSelected;
    private string _status;

    public AppRow(string appId, string displayName, string runtimeType, string status)
    {
        AppId = appId;
        DisplayName = displayName;
        RuntimeType = runtimeType;
        _status = status;
    }

    public string AppId { get; }

    public string DisplayName { get; }

    public string RuntimeType { get; }

    public bool Launchable { get; init; }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value)
            {
                return;
            }

            _isSelected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
        }
    }

    public string Status
    {
        get => _status;
        set
        {
            if (_status == value)
            {
                return;
            }

            _status = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Status)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
