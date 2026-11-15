using Aether.Contracts;

namespace Aether.Security.Classification;

public sealed record ClassificationInput(string Content, DataClassification? TrustedSourceClassification, string SourceId);

public sealed record ClassificationResult(DataClassification Classification, IReadOnlyList<string> ReasonCodes, bool DetectorFailed);

public interface IDataClassifier
{
    ClassificationResult Classify(ClassificationInput input);
}

/// <summary>
/// Source-first classifier: an explicit trusted source classification sets the
/// minimum sensitivity; deterministic detectors may escalate it but can never
/// downgrade it. Missing classification resolves to UNKNOWN (fail-closed).
/// </summary>
public sealed class SourceFirstClassifier : IDataClassifier
{
    private readonly IReadOnlyList<ISensitiveEntityDetector> _detectors;
    private readonly bool _escalateOnAnyFinding;

    public SourceFirstClassifier(IReadOnlyList<ISensitiveEntityDetector>? detectors = null, bool escalateOnAnyFinding = true)
    {
        _detectors = detectors ?? [new RuleBasedSensitiveEntityDetector()];
        _escalateOnAnyFinding = escalateOnAnyFinding;
    }

    public ClassificationResult Classify(ClassificationInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var reasons = new List<string>();
        var classification = input.TrustedSourceClassification ?? DataClassification.Unknown;
        reasons.Add(input.TrustedSourceClassification is null
            ? "trusted-source:missing:unknown"
            : $"trusted-source:{classification.ToWireName()}");

        foreach (var detector in _detectors)
        {
            var detection = detector.Detect(input.Content);
            if (detection.Failed)
            {
                reasons.Add($"detector:{detector.DetectorId}:failed:fail-closed");
                return new ClassificationResult(DataClassification.Unknown, reasons, DetectorFailed: true);
            }

            if (detection.Findings.Count == 0)
            {
                continue;
            }

            reasons.Add($"detector:{detector.DetectorId}:{string.Join("+", detection.Findings.Select(f => $"{f.Category}:{f.Count}"))}");
            if (_escalateOnAnyFinding)
            {
                var escalation = ClassificationEscalator.Evaluate(classification, detection.Findings);
                reasons.AddRange(escalation.ReasonCodes.Select(reason => $"detector:{detector.DetectorId}:{reason}"));
                classification = escalation.ResultingClassification;
            }
        }

        return new ClassificationResult(classification, reasons, DetectorFailed: false);
    }
}
