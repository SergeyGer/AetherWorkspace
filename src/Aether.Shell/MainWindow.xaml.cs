using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using Aether.AI.Planner;
using Aether.AI.Providers;
using Aether.AI.Summarization;
using Aether.Broker.Client;
using Aether.Contracts;
using Aether.Contracts.Broker;
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
        Loaded += OnLoaded;
        Closed += OnClosed;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            _endpoint = LoadEndpoint();
            BackendModeText.Text = $"{_endpoint.Transport} (local IPC only)";
            await ConnectAsync();
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
        _orchestrator = BuildOrchestrator(_client);
    }

    private AetherWorkflowOrchestrator BuildOrchestrator(BrokerClient client)
    {
        var policy = PolicyDocument.CreateDefault().EnsureValid();
        var policyEngine = new PolicyEngine(policy);
        var egressGateway = new EgressGateway(policyEngine, _auditSink);
        var detector = new RuleBasedSensitiveEntityDetector();
        var classifier = new SourceFirstClassifier([detector]);
        var summaryService = new SummaryService(new LocalDeterministicSummaryProvider(detector));
        var validator = new ActionPlanValidator(new ActionPlanValidationOptions { MaxActions = 6 });
        var options = new WorkflowOrchestratorOptions
        {
            MaxActionsPerPlan = 6,
            PlanningMode = PlannerCombo.SelectedIndex == 1
                ? PlanGenerationMode.CloudPreferred
                : PlanGenerationMode.DeterministicPreferred,
            Execution = new ExecutionOptions { MaxActionsPerPlan = 6, StepTimeout = TimeSpan.FromSeconds(20) },
        };

        IPlanGenerator? cloudPlanner = null;
        if (CloudConsentCheck.IsChecked == true)
        {
            cloudPlanner = new CloudPlanProvider(
                new HttpCloudModelTransport(new EnvironmentSecretProvider()),
                egressGateway,
                new PlanProviderOptions(CloudEnabled: true, CloudConsentGranted: true));
        }

        return new AetherWorkflowOrchestrator(
            classifier,
            new DeterministicCommandPlanner(),
            cloudPlanner,
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

        if (_client is null || _orchestrator is null)
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

        if (_orchestrator is null)
        {
            return;
        }

        if (!DataClassificationExtensions.TryParseWire(SelectedClassification(), out var classification))
        {
            classification = DataClassification.Unknown;
        }

        SetRunningState(true);
        _steps.Clear();
        SummaryBox.Text = string.Empty;
        if (_lastTelemetry is not null)
        {
            RenderTelemetry(_lastTelemetry);
        }

        TraceBox.Text = string.Empty;
        StatusBox.Text = string.Empty;
        _runCancellation = new CancellationTokenSource(TimeSpan.FromMinutes(3));

        _uiTelemetry.Start();
        UiTelemetryText.Text = "ui telemetry: capturing…";
        try
        {
            var request = new WorkflowRequest(CommandBox.Text, classification);
            var result = await _orchestrator.ExecuteAsync(request, _runCancellation.Token);
            _lastTelemetry = _uiTelemetry.Stop(result.DurationMs);
            RenderResult(result);
            WriteTelemetryIfConfigured(result, _lastTelemetry);
        }
        catch (OperationCanceledException)
        {
            AppendStatus("Workflow cancelled by the operator.");
        }
        catch (Exception exception)
        {
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
        if (telemetry is null || telemetry.FrameCount == 0)
        {
            UiTelemetryText.Text = "ui telemetry: no frames captured";
            return;
        }

        UiTelemetryText.Text =
            $"ui frames={telemetry.FrameCount} p50={telemetry.P50FrameMs:0.##}ms p95={telemetry.P95FrameMs:0.##}ms " +
            $"p99={telemetry.P99FrameMs:0.##}ms max={telemetry.MaxFrameMs:0.##}ms long={telemetry.LongFrames} " +
            $"command={telemetry.CommandDurationMs}ms ws={telemetry.WorkingSetMb:0.#}MB gc={telemetry.ManagedMemoryMb:0.#}MB";
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
                : $"ui: frames={_lastTelemetry.FrameCount} p50={_lastTelemetry.P50FrameMs:0.##}ms p95={_lastTelemetry.P95FrameMs:0.##}ms p99={_lastTelemetry.P99FrameMs:0.##}ms max={_lastTelemetry.MaxFrameMs:0.##}ms long={_lastTelemetry.LongFrames} command={_lastTelemetry.CommandDurationMs}ms{Environment.NewLine}");

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
