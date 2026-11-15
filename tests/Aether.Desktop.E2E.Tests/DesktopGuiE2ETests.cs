using System.Text;
using Aether.Broker.Client;
using Aether.Contracts;
using Aether.Contracts.Broker;
using Aether.Security.Audit;
using Aether.Testing;
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

        // Broker audit correlation: three successful launches, a context publish and a window
        // arrangement all carry the trace id reported by the WPF shell.
        var audit = broker.ReadBrokerAudit();
        Assert.NotEmpty(audit);
        foreach (var appId in TestData.SampleAppIds)
        {
            Assert.Contains(
                audit,
                entry => entry.EventType == AuditEventTypes.BrokerLaunch
                    && entry.TraceId == run.TraceId
                    && entry.Outcome == AuditOutcomes.Succeeded
                    && entry.BrokeredAppId == appId);
        }

        Assert.Contains(
            audit,
            entry => entry.EventType == AuditEventTypes.ContextPublish && entry.TraceId == run.TraceId);
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
}
