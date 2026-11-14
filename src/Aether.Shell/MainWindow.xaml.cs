using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using Aether.AI.Planner;
using Aether.AI.Providers;
using Aether.AI.Summarization;
using Aether.Broker.Client;
using Aether.Contracts;
using Aether.Contracts.Broker;
using Aether.Execution.Authorization;
using Aether.Execution.Executor;
using Aether.Execution.Results;
using Aether.Execution.Validation;
using Aether.Security.Audit;
using Aether.Security.Classification;
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
    private readonly HashChainAuditLog _auditSink = new();
    private BrokerClient? _client;
    private BrokerEndpointInfo? _endpoint;
    private AetherWorkflowOrchestrator? _orchestrator;
    private CancellationTokenSource? _runCancellation;
    private bool _running;

    public MainWindow()
    {
        InitializeComponent();
        StepsList.ItemsSource = _steps;
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
        TraceBox.Text = string.Empty;
        StatusBox.Text = string.Empty;
        _runCancellation = new CancellationTokenSource(TimeSpan.FromMinutes(3));

        try
        {
            var request = new WorkflowRequest(CommandBox.Text, classification);
            var result = await _orchestrator.ExecuteAsync(request, _runCancellation.Token);
            RenderResult(result);
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
            SetRunningState(false);
            _runCancellation?.Dispose();
            _runCancellation = null;
        }
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        _runCancellation?.Cancel();
        AppendStatus("Cancellation requested; the executor will stop at the next step boundary.");
    }

    private void RenderResult(WorkflowResult result)
    {
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
                : $"denial: {result.DenialCode} ({result.DenialCategory}){Environment.NewLine}");

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
}
