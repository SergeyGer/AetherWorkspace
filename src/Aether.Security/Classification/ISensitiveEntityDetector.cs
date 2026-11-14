namespace Aether.Security.Classification;

/// <summary>One category of sensitive entities found in content.</summary>
public sealed record SensitiveEntityFinding(string Category, int Count);

/// <summary>
/// Result of a local detector run. <see cref="Failed"/> must be treated as a
/// fail-closed signal: detector failure may escalate but never downgrade a
/// classification.
/// </summary>
public sealed record SensitiveEntityDetectionResult(IReadOnlyList<SensitiveEntityFinding> Findings, bool Failed)
{
    public static readonly SensitiveEntityDetectionResult None = new([], false);

    public static readonly SensitiveEntityDetectionResult Failure = new([], true);

    public int TotalCount => Findings.Sum(finding => finding.Count);
}

/// <summary>Optional local sensitive-entity detector (rules, NER, and so on).</summary>
public interface ISensitiveEntityDetector
{
    string DetectorId { get; }

    SensitiveEntityDetectionResult Detect(string? content);
}
