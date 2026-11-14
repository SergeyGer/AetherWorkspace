using Aether.Contracts;
using Aether.Contracts.Errors;
using Aether.Security.Policy;

namespace Aether.AI.Summarization;

/// <summary>Provider-neutral summary routing used by the workflow orchestrator.</summary>
public interface ISummaryService
{
    Task<SummaryResult> SummarizeAsync(SummaryRequest request, CancellationToken cancellationToken = default);

    Task<SummaryResult> SummarizePrivatelyAsync(SummaryRequest request, CancellationToken cancellationToken = default);
}

public sealed record SummaryServiceOptions(bool PreferCloudForPublic = false)
{
    public static SummaryServiceOptions LocalOnly { get; } = new(false);
}

/// <summary>
/// Provider-neutral summary routing. Private/local remains the default for every
/// classification; cloud is considered only for PUBLIC data when the local
/// configuration explicitly prefers it, and the cloud provider still passes
/// through the egress gateway.
/// </summary>
public sealed class SummaryService : ISummaryService
{
    private readonly ISummaryProvider _localProvider;
    private readonly ISummaryProvider? _cloudProvider;
    private readonly SummaryServiceOptions _options;

    public SummaryService(ISummaryProvider localProvider, ISummaryProvider? cloudProvider = null, SummaryServiceOptions? options = null)
    {
        _localProvider = localProvider ?? throw new ArgumentNullException(nameof(localProvider));
        _cloudProvider = cloudProvider;
        _options = options ?? SummaryServiceOptions.LocalOnly;
    }

    public Task<SummaryResult> SummarizeAsync(SummaryRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var useCloud = _options.PreferCloudForPublic
            && request.Classification == DataClassification.Public
            && _cloudProvider is not null;

        return useCloud ? _cloudProvider!.SummarizeAsync(request, cancellationToken) : _localProvider.SummarizeAsync(request, cancellationToken);
    }

    /// <summary>Local private summary is always available (deterministic reduced-function path).</summary>
    public Task<SummaryResult> SummarizePrivatelyAsync(SummaryRequest request, CancellationToken cancellationToken = default) =>
        _localProvider.ProviderKind == AiProviderKind.Local
            ? _localProvider.SummarizeAsync(request, cancellationToken)
            : Task.FromResult(SummaryResult.Fail(
                AetherErrorCodes.AiProviderUnavailable,
                "A local summary provider is not configured.",
                "none",
                request.Classification));
}
