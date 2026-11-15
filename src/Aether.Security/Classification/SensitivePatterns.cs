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
        "phone-like",
        "lei-like",
        "isin-like",
    ];

    public static SensitiveEntityDetectorOptions Default => new();

    /// <summary>
    /// Built-in, synthetic-friendly patterns. They are deterministic, bounded and
    /// intentionally shape-based: no checksum validation and no network access.
    /// LEI and ISIN use canonical uppercase form to keep ordinary lowercase
    /// identifiers out of the result set.
    /// </summary>
    public static IReadOnlyList<SensitivePattern> DefaultPatterns() =>
    [
        new("synthetic-account-id", @"\bACC-[0-9]{8,12}\b"),
        new("email-like", @"\b[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}\b"),
        new("iban-like", @"\b[A-Z]{2}[0-9]{2}[A-Z0-9]{11,30}\b"),
        // Grouped national/international numbers with explicit separators, plus
        // compact "+" numbers. Requires two separators (or an explicit "+" prefix)
        // so dates, versions and plain short numbers are not flagged.
        new("phone-like", @"(?:\+[0-9]{10,15}\b)|(?:\b(?:\+[0-9]{1,3}[ .-]?)?(?:\([0-9]{2,4}\)[ .-]?)?[0-9]{3,4}[ .-][0-9]{3,4}[ .-][0-9]{3,5}\b)"),
        // ISO 17442 LEI: 4 letters, 14 alphanumerics, 2 check digits.
        new("lei-like", @"\b(?-i:[A-Z]{4}[A-Z0-9]{14}[0-9]{2})\b"),
        // ISO 6166 ISIN: 2-letter country code, 9 alphanumerics, 1 check digit.
        new("isin-like", @"\b(?-i:[A-Z]{2}[A-Z0-9]{9}[0-9])\b"),
    ];
}

/// <summary>
/// Deterministic, bounded, fail-closed sensitive entity detector. Uses
/// non-backtracking regular expressions with an overall content size limit so
/// untrusted workspace text cannot cause catastrophic backtracking. Findings
/// contain category/count metadata only, never the matched text.
/// </summary>
public sealed partial class RuleBasedSensitiveEntityDetector : ISensitiveEntityDetector
{
    /// <summary>Identifier for this rule set; findings are only categories and counts.</summary>
    public const string CurrentDetectorId = "rules-v2";

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

    public string DetectorId => CurrentDetectorId;

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
