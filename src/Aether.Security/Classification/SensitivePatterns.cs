using System.Text.RegularExpressions;

namespace Aether.Security.Classification;

/// <summary>A single configurable detection rule.</summary>
public sealed record SensitivePattern(string Category, string Pattern);

/// <summary>Configuration for the deterministic rule-based detector.</summary>
public sealed class SensitiveEntityDetectorOptions
{
    public int MaxContentLength { get; init; } = 256 * 1024;

    public IReadOnlyList<SensitivePattern> Patterns { get; init; } = DefaultPatterns();

    /// <summary>Categories that escalate the content to RESTRICTED when found.</summary>
    public IReadOnlyList<string> EscalatingCategories { get; init; } =
    [
        "synthetic-account-id",
        "email-like",
        "iban-like",
    ];

    public static SensitiveEntityDetectorOptions Default => new();

    public static IReadOnlyList<SensitivePattern> DefaultPatterns() =>
    [
        new("synthetic-account-id", @"\bACC-[0-9]{8,12}\b"),
        new("email-like", @"\b[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}\b"),
        new("iban-like", @"\b[A-Z]{2}[0-9]{2}[A-Z0-9]{11,30}\b"),
    ];
}

/// <summary>
/// Deterministic, bounded, fail-closed sensitive entity detector. Uses
/// non-backtracking regular expressions with an overall content size limit so
/// untrusted workspace text cannot cause catastrophic backtracking.
/// </summary>
public sealed partial class RuleBasedSensitiveEntityDetector : ISensitiveEntityDetector
{
    private readonly SensitiveEntityDetectorOptions _options;
    private readonly IReadOnlyList<(SensitivePattern Pattern, Regex Regex)> _rules;

    public RuleBasedSensitiveEntityDetector(SensitiveEntityDetectorOptions? options = null)
    {
        _options = options ?? SensitiveEntityDetectorOptions.Default;
        _rules = _options.Patterns
            .Select(pattern => (
                pattern,
                new Regex(
                    pattern.Pattern,
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking,
                    TimeSpan.FromMilliseconds(250))))
            .ToArray();
    }

    public string DetectorId => "rules-v1";

    public SensitiveEntityDetectionResult Detect(string? content)
    {
        if (string.IsNullOrEmpty(content))
        {
            return SensitiveEntityDetectionResult.None;
        }

        var boundedContent = content.Length <= _options.MaxContentLength
            ? content
            : content[.._options.MaxContentLength];

        var findings = new List<SensitiveEntityFinding>();
        foreach (var (pattern, regex) in _rules)
        {
            if (!_options.EscalatingCategories.Contains(pattern.Category, StringComparer.Ordinal))
            {
                continue;
            }

            try
            {
                var count = regex.Matches(boundedContent).Count;
                if (count > 0)
                {
                    findings.Add(new SensitiveEntityFinding(pattern.Category, count));
                }
            }
            catch (RegexMatchTimeoutException)
            {
                return SensitiveEntityDetectionResult.Failure;
            }
        }

        return new SensitiveEntityDetectionResult(findings, Failed: false);
    }

    /// <summary>Builds detector options from the trusted policy document patterns.</summary>
    public static SensitiveEntityDetectorOptions FromPolicy(string accountIdPattern, int maxContentLength = 256 * 1024)
    {
        var patterns = SensitiveEntityDetectorOptions.DefaultPatterns()
            .Where(pattern => !string.Equals(pattern.Category, "synthetic-account-id", StringComparison.Ordinal))
            .ToList();
        patterns.Add(new SensitivePattern("synthetic-account-id", accountIdPattern));
        return new SensitiveEntityDetectorOptions { Patterns = patterns, MaxContentLength = maxContentLength };
    }
}
