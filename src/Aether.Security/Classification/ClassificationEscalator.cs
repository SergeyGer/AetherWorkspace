using System.Collections.Frozen;
using Aether.Contracts;

namespace Aether.Security.Classification;

/// <summary>
/// Outcome of combining a current classification with local detector evidence.
/// <see cref="MinimumClassification"/> is what the evidence alone requires and
/// <see cref="ResultingClassification"/> is <c>Max(current, minimum)</c>, so an
/// escalation can raise but never lower the classification.
/// </summary>
public sealed record ClassificationEscalation(
    DataClassification CurrentClassification,
    DataClassification MinimumClassification,
    DataClassification ResultingClassification,
    IReadOnlyList<string> ReasonCodes,
    bool DetectorFailed)
{
    /// <summary>True when the detector evidence raised the current classification.</summary>
    public bool Escalated => ResultingClassification.SensitivityRank() > CurrentClassification.SensitivityRank();
}

/// <summary>
/// Maps deterministic detector output to the minimum classification required by
/// policy. Strong PII/financial findings (account, IBAN, email, phone, LEI,
/// ISIN) require RESTRICTED, unrecognised but non-empty findings require
/// INTERNAL, and a detector failure requires UNKNOWN (fail-closed). The
/// resulting classification never downgrades the caller's current value.
/// All members are pure; the type is stateless and thread-safe.
/// </summary>
public static class ClassificationEscalator
{
    /// <summary>Reason code added when the detector failed; always fails closed to UNKNOWN.</summary>
    public const string DetectorFailureReason = "escalation:detector-failed:unknown";

    /// <summary>Reason code added when a finding is malformed (missing category); fails closed to UNKNOWN.</summary>
    public const string MalformedFindingReason = "escalation:malformed-finding:unknown";

    /// <summary>Prefix of a reason code for a RESTRICTED-requiring category.</summary>
    public const string RestrictedReasonPrefix = "escalation:restricted:";

    /// <summary>Prefix of a reason code for a weaker INTERNAL-requiring category.</summary>
    public const string InternalReasonPrefix = "escalation:internal:";

    private static readonly FrozenSet<string> RestrictedCategorySet = new[]
    {
        "synthetic-account-id",
        "email-like",
        "iban-like",
        "phone-like",
        "lei-like",
        "isin-like",
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>Categories whose presence requires at least <see cref="DataClassification.Restricted"/>.</summary>
    public static IReadOnlySet<string> RestrictedCategories => RestrictedCategorySet;

    /// <summary>True when the category requires at least <see cref="DataClassification.Restricted"/>.</summary>
    public static bool IsRestrictedCategory(string? category) =>
        category is not null && RestrictedCategorySet.Contains(category);

    /// <summary>Returns the minimum classification required by the detector output alone.</summary>
    public static DataClassification GetMinimumClassification(SensitiveEntityDetectionResult detection)
    {
        ArgumentNullException.ThrowIfNull(detection);
        return Evaluate(DataClassification.Public, detection).MinimumClassification;
    }

    /// <summary>
    /// Combines the current classification with a detector result. Findings are
    /// only categories and counts; no raw content is accepted or returned.
    /// </summary>
    public static ClassificationEscalation Evaluate(
        DataClassification currentClassification,
        SensitiveEntityDetectionResult detection)
    {
        ArgumentNullException.ThrowIfNull(detection);
        return Evaluate(currentClassification, detection.Findings, detection.Failed);
    }

    /// <summary>
    /// Combines the current classification with detector findings. A non-empty
    /// finding without a known strong category escalates to INTERNAL only, so a
    /// custom detector can never silently downgrade the evidence it reported.
    /// </summary>
    public static ClassificationEscalation Evaluate(
        DataClassification currentClassification,
        IReadOnlyList<SensitiveEntityFinding> findings,
        bool detectorFailed = false)
    {
        ArgumentNullException.ThrowIfNull(findings);

        // Undefined labels and unknown sensitivity always resolve fail-closed.
        var current = Enum.IsDefined(currentClassification) ? currentClassification : DataClassification.Unknown;
        var minimum = DataClassification.Public;
        var reasons = new List<string>();

        if (detectorFailed)
        {
            minimum = DataClassification.Unknown;
            reasons.Add(DetectorFailureReason);
        }
        else
        {
            var reportedCategories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var malformedReported = false;
            foreach (var finding in findings)
            {
                if (finding is null || string.IsNullOrWhiteSpace(finding.Category))
                {
                    minimum = DataClassification.Unknown;
                    if (!malformedReported)
                    {
                        malformedReported = true;
                        reasons.Add(MalformedFindingReason);
                    }

                    continue;
                }

                if (finding.Count <= 0 || !reportedCategories.Add(finding.Category))
                {
                    continue;
                }

                if (RestrictedCategorySet.Contains(finding.Category))
                {
                    minimum = minimum.Max(DataClassification.Restricted);
                    reasons.Add($"{RestrictedReasonPrefix}{finding.Category}");
                }
                else
                {
                    minimum = minimum.Max(DataClassification.Internal);
                    reasons.Add($"{InternalReasonPrefix}{finding.Category}");
                }
            }
        }

        var resulting = current.Max(minimum);
        return new ClassificationEscalation(current, minimum, resulting, reasons, detectorFailed);
    }
}
