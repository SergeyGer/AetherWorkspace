using Aether.AI.Providers;
using Aether.Contracts;
using Aether.Contracts.Models;
using Aether.Execution.Actions;
using Aether.Security.Classification;

namespace Aether.Testing;

/// <summary>Detector fake that always fails (used to prove fail-closed behavior).</summary>
public sealed class FailingSensitiveEntityDetector : ISensitiveEntityDetector
{
    public string DetectorId => "failing";

    public SensitiveEntityDetectionResult Detect(string? content) => SensitiveEntityDetectionResult.Failure;
}

/// <summary>Detector fake that reports a fixed finding set.</summary>
public sealed class FixedSensitiveEntityDetector : ISensitiveEntityDetector
{
    private readonly IReadOnlyList<SensitiveEntityFinding> _findings;

    public FixedSensitiveEntityDetector(params SensitiveEntityFinding[] findings) => _findings = findings;

    public string DetectorId => "fixed";

    public SensitiveEntityDetectionResult Detect(string? content) => new(_findings, Failed: false);
}

/// <summary>Records handler invocations so tests can prove denial happens before side effects.</summary>
public sealed class RecordingActionHandler : IActionHandler
{
    private readonly bool _succeed;

    public RecordingActionHandler(ActionKind kind, bool succeed = true)
    {
        Kind = kind;
        _succeed = succeed;
    }

    public ActionKind Kind { get; }

    public List<ActionExecutionRequest> Executions { get; } = [];

    public List<ActionExecutionRequest> Compensations { get; } = [];

    public Task<HandlerResult> ExecuteAsync(ActionExecutionRequest request, CancellationToken cancellationToken)
    {
        Executions.Add(request);
        return Task.FromResult(_succeed
            ? HandlerResult.Ok(new Dictionary<string, string> { ["recorded"] = request.StepId })
            : HandlerResult.Fail("test.handler.failed", "The test handler was configured to fail."));
    }

    public Task<CompensationResult> CompensateAsync(ActionExecutionRequest request, CancellationToken cancellationToken)
    {
        Compensations.Add(request);
        return Task.FromResult(CompensationResult.Succeeded());
    }
}

/// <summary>Counting cloud transport used to prove zero external calls on denied paths.</summary>
public sealed class RecordingCloudTransport : ICloudModelTransport
{
    private long _calls;

    public string ProviderId => "recording-cloud";

    public long Calls => Interlocked.Read(ref _calls);

    public List<CloudModelRequest> Requests { get; } = [];

    public Func<CloudModelRequest, CloudModelResponse>? Responder { get; set; }

    public Task<CloudModelResponse> SendAsync(CloudModelRequest request, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _calls);
        Requests.Add(request);
        var response = Responder?.Invoke(request) ?? CloudModelResponse.Ok("{\"summary\":\"synthetic test response\"}");
        return Task.FromResult(response);
    }
}

/// <summary>Counting loopback local transport for optional local-model adapter tests.</summary>
public sealed class RecordingLocalTransport : ILocalModelTransport
{
    private long _calls;

    public string ProviderId => "recording-local";

    public long Calls => Interlocked.Read(ref _calls);

    public Func<CloudModelRequest, CloudModelResponse>? Responder { get; set; }

    public Task<CloudModelResponse> SendAsync(CloudModelRequest request, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _calls);
        var response = Responder?.Invoke(request) ?? CloudModelResponse.Ok("{\"summary\":\"local synthetic summary\"}");
        return Task.FromResult(response);
    }
}

/// <summary>In-memory audit sink that records drafts for assertions.</summary>
public sealed class RecordingAuditSink : Aether.Security.Audit.IAuditSink
{
    private readonly List<Aether.Security.Audit.AuditEventDraft> _drafts = [];

    public IReadOnlyList<Aether.Security.Audit.AuditEventDraft> Drafts
    {
        get
        {
            lock (_drafts)
            {
                return _drafts.ToArray();
            }
        }
    }

    public void Append(Aether.Security.Audit.AuditEventDraft draft)
    {
        lock (_drafts)
        {
            _drafts.Add(draft);
        }
    }

    public IReadOnlyList<Aether.Security.Audit.AuditEvent> Snapshot() => [];
}
