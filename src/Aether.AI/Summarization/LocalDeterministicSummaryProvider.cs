using System.Globalization;
using System.Text.Json;
using Aether.Contracts;
using Aether.Contracts.Errors;
using Aether.Contracts.Json;
using Aether.Contracts.Models.Contexts;
using Aether.Security.Classification;
using Aether.Security.Policy;

namespace Aether.AI.Summarization;

/// <summary>
/// Deterministic, fully local summary provider. This is the reduced-function
/// private path used when no local model is installed: it never performs
/// network I/O and works offline. Output text excludes raw identifiers; only
/// aggregate synthetic figures are reported.
/// </summary>
public sealed class LocalDeterministicSummaryProvider : ISummaryProvider
{
    public const string ProviderName = "local-deterministic";

    private readonly ISensitiveEntityDetector _detector;

    public LocalDeterministicSummaryProvider(ISensitiveEntityDetector? detector = null) =>
        _detector = detector ?? new RuleBasedSensitiveEntityDetector();

    public string ProviderId => ProviderName;

    public AiProviderKind ProviderKind => AiProviderKind.Local;

    public Task<SummaryResult> SummarizeAsync(SummaryRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            if (request.ContextType == ExposureContext.ContextType)
            {
                var exposure = AetherJson.DeserializeStrict<ExposureContext>(request.PayloadJson);
                var summary = SummarizeExposure(exposure);
                var detection = _detector.Detect(request.PayloadJson);
                var redactionCount = detection.Failed ? 0 : detection.TotalCount;
                return Task.FromResult(SummaryResult.Ok(summary, ProviderId, request.Classification, redactionCount));
            }

            if (request.ContextType == InstrumentContext.ContextType)
            {
                var instrument = AetherJson.DeserializeStrict<InstrumentContext>(request.PayloadJson);
                var summary = string.Format(
                    CultureInfo.InvariantCulture,
                    "Private instrument summary: {0}; ticker {1}; ISIN {2}; source classification {3}.",
                    instrument.Name,
                    Safe(instrument.Id.Ticker),
                    Safe(instrument.Id.Isin),
                    request.Classification.ToWireName());
                return Task.FromResult(SummaryResult.Ok(summary, ProviderId, request.Classification, 0));
            }

            return Task.FromResult(SummaryResult.Fail(
                AetherErrorCodes.AiOutputInvalid,
                "The local deterministic provider does not support this context type.",
                ProviderId,
                request.Classification));
        }
        catch (AetherContractException exception)
        {
            return Task.FromResult(SummaryResult.Fail(
                AetherErrorCodes.AiOutputInvalid,
                $"The context payload could not be parsed by the local provider: {exception.Message}",
                ProviderId,
                request.Classification));
        }
        catch (JsonException)
        {
            return Task.FromResult(SummaryResult.Fail(
                AetherErrorCodes.AiOutputInvalid,
                "The context payload is not valid JSON.",
                ProviderId,
                request.Classification));
        }
    }

    private static string SummarizeExposure(ExposureContext exposure)
    {
        var positions = exposure.Positions;
        if (positions.Count == 0)
        {
            return "Private summary (local, deterministic): the synthetic exposure context contains no positions.";
        }

        var grossNotional = positions.Sum(position => position.Notional);
        var netMarketValue = positions.Sum(position => position.MarketValue);
        var totalDv01 = positions.Sum(position => position.Dv01);
        var largest = positions.OrderByDescending(position => Math.Abs(position.Notional)).First();
        var largestShare = grossNotional == 0
            ? 0m
            : Math.Abs(largest.Notional) / Math.Abs(grossNotional) * 100m;

        var summary = string.Format(
            CultureInfo.InvariantCulture,
            "Private summary (local, deterministic): {0} synthetic positions in {1}; gross notional {2}; net market value {3}; total DV01 {4}; largest issuer {5} ({6:0.0}% of gross notional). No external provider was called; no account identifiers are included in this summary.",
            positions.Count,
            exposure.Currency,
            Format(grossNotional),
            Format(netMarketValue),
            Format(totalDv01),
            largest.Issuer,
            largestShare);
        return summary;
    }

    private static string Format(decimal value) => value.ToString("N0", CultureInfo.InvariantCulture);

    private static string Safe(string? value) => string.IsNullOrWhiteSpace(value) ? "n/a" : value;
}
