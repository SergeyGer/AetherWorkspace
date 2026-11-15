using Aether.Contracts;
using Aether.Security.Classification;
using Xunit;

namespace Aether.Security.Tests;

public class ClassificationEscalationTests
{
    private static readonly RuleBasedSensitiveEntityDetector Detector = new();

    private static SensitiveEntityDetectionResult Findings(params (string Category, int Count)[] findings) =>
        new(findings.Select(finding => new SensitiveEntityFinding(finding.Category, finding.Count)).ToArray(), Failed: false);

    [Theory]
    [InlineData("synthetic-account-id", "Posting ACC-00000001 was received.")]
    [InlineData("email-like", "Contact analyst@example.invalid for details.")]
    [InlineData("iban-like", "Wire to GB00AETH00000000000001 today.")]
    [InlineData("phone-like", "Call 555-123-4567 before the close.")]
    [InlineData("phone-like", "Dial +15551234567 when ready.")]
    [InlineData("lei-like", "LEI AETH0000000000000123 is approved.")]
    [InlineData("isin-like", "ISIN XS1234567890 is held.")]
    public void DefaultDetectorDetectsEachSyntheticCategory(string expectedCategory, string content)
    {
        var detection = Detector.Detect(content);

        Assert.False(detection.Failed);
        Assert.Contains(detection.Findings, finding => finding.Category == expectedCategory && finding.Count >= 1);
    }

    [Fact]
    public void DefaultDetectorCanonicalUppercaseIdentifiersAreDetected()
    {
        var detection = Detector.Detect("LEI QXZYQNMR4JZ5RIRN4T31 and ISIN US5949181045.");

        Assert.False(detection.Failed);
        Assert.Contains(detection.Findings, finding => finding.Category == "lei-like");
        Assert.Contains(detection.Findings, finding => finding.Category == "isin-like");
    }

    [Theory]
    [InlineData("The quarterly report is ready for review.")]
    [InlineData("Please restart the service before 17:30.")]
    [InlineData("Version 10.20.30 was released.")]
    [InlineData("The review meeting is scheduled for 2024-01-15.")]
    [InlineData("Ordinary workspace text without identifiers.")]
    [InlineData("abcdefghijklmnopqrstuvwxyz")]
    [InlineData("trace 0123456789abcdef0123456789abcdef")]
    [InlineData("release candidate build 12345")]
    public void RegexFalsePositiveGuardsKeepOrdinaryProseUnflagged(string content)
    {
        var detection = Detector.Detect(content);

        Assert.False(detection.Failed);
        Assert.Empty(detection.Findings);
    }

    [Fact]
    public void DetectorIsDeterministicAndReportsOnlyCategoriesAndCounts()
    {
        const string content = "ACC-00000001 email analyst@example.invalid ISIN XS1234567890";
        var first = Detector.Detect(content);
        var second = Detector.Detect(content);

        Assert.False(first.Failed);
        Assert.Equal(first.TotalCount, second.TotalCount);
        Assert.Equal(
            first.Findings.Select(finding => (finding.Category, finding.Count)),
            second.Findings.Select(finding => (finding.Category, finding.Count)));
        Assert.DoesNotContain("ACC-00000001", first.Findings.Select(finding => finding.Category));
        Assert.All(first.Findings, finding => Assert.False(string.IsNullOrWhiteSpace(finding.Category)));
    }

    [Fact]
    public void DetectorRemainsBoundedForNewPatterns()
    {
        var bounded = new RuleBasedSensitiveEntityDetector(
            new SensitiveEntityDetectorOptions { MaxContentLength = 1024 });
        var content = new string('a', 100_000) + " ISIN XS1234567890 555-123-4567";

        var detection = bounded.Detect(content);

        Assert.False(detection.Failed);
        Assert.Empty(detection.Findings);
    }

    [Fact]
    public void UndefinedClassificationsFailClosedToUnknown()
    {
        var escalation = ClassificationEscalator.Evaluate((DataClassification)99, Findings(("email-like", 1)));

        Assert.Equal(DataClassification.Unknown, escalation.ResultingClassification);
        Assert.Equal(DataClassification.Unknown, escalation.CurrentClassification);
    }

    [Theory]
    [InlineData(DataClassification.Public, "synthetic-account-id", DataClassification.Restricted)]
    [InlineData(DataClassification.Public, "email-like", DataClassification.Restricted)]
    [InlineData(DataClassification.Public, "iban-like", DataClassification.Restricted)]
    [InlineData(DataClassification.Public, "phone-like", DataClassification.Restricted)]
    [InlineData(DataClassification.Public, "lei-like", DataClassification.Restricted)]
    [InlineData(DataClassification.Public, "isin-like", DataClassification.Restricted)]
    [InlineData(DataClassification.Internal, "email-like", DataClassification.Restricted)]
    [InlineData(DataClassification.Restricted, "isin-like", DataClassification.Restricted)]
    [InlineData(DataClassification.Unknown, "iban-like", DataClassification.Unknown)]
    [InlineData(DataClassification.Public, "custom-marker", DataClassification.Internal)]
    [InlineData(DataClassification.Internal, "custom-marker", DataClassification.Internal)]
    [InlineData(DataClassification.Restricted, "custom-marker", DataClassification.Restricted)]
    [InlineData(DataClassification.Unknown, "custom-marker", DataClassification.Unknown)]
    public void EscalationMatrixNeverDowngrades(
        DataClassification current,
        string category,
        DataClassification expected)
    {
        var escalation = ClassificationEscalator.Evaluate(current, Findings((category, 1)));

        Assert.Equal(expected, escalation.ResultingClassification);
        Assert.True(escalation.ResultingClassification.SensitivityRank() >= current.SensitivityRank());
    }

    [Theory]
    [InlineData(DataClassification.Public)]
    [InlineData(DataClassification.Internal)]
    [InlineData(DataClassification.Restricted)]
    [InlineData(DataClassification.Unknown)]
    public void DetectorFailureFailsClosedToUnknown(DataClassification current)
    {
        var escalation = ClassificationEscalator.Evaluate(current, SensitiveEntityDetectionResult.Failure);

        Assert.True(escalation.DetectorFailed);
        Assert.Equal(DataClassification.Unknown, escalation.ResultingClassification);
        Assert.Contains(ClassificationEscalator.DetectorFailureReason, escalation.ReasonCodes);
    }

    [Theory]
    [InlineData(DataClassification.Public)]
    [InlineData(DataClassification.Internal)]
    [InlineData(DataClassification.Restricted)]
    [InlineData(DataClassification.Unknown)]
    public void NoFindingsLeaveClassificationUnchanged(DataClassification current)
    {
        var escalation = ClassificationEscalator.Evaluate(current, SensitiveEntityDetectionResult.None);

        Assert.False(escalation.DetectorFailed);
        Assert.Equal(current, escalation.ResultingClassification);
        Assert.Equal(DataClassification.Public, escalation.MinimumClassification);
        Assert.False(escalation.Escalated);
        Assert.Empty(escalation.ReasonCodes);
    }

    [Fact]
    public void MixedFindingsTakeTheStrongestMinimum()
    {
        var escalation = ClassificationEscalator.Evaluate(
            DataClassification.Public,
            Findings(("custom-marker", 2), ("email-like", 1), ("custom-marker", 3)));

        Assert.Equal(DataClassification.Restricted, escalation.MinimumClassification);
        Assert.Equal(DataClassification.Restricted, escalation.ResultingClassification);
        Assert.Contains($"{ClassificationEscalator.RestrictedReasonPrefix}email-like", escalation.ReasonCodes);
        Assert.Contains($"{ClassificationEscalator.InternalReasonPrefix}custom-marker", escalation.ReasonCodes);
        Assert.Single(escalation.ReasonCodes, code => code.Contains("custom-marker", StringComparison.Ordinal));
    }

    [Fact]
    public void MalformedFindingsFailClosedToUnknown()
    {
        var escalation = ClassificationEscalator.Evaluate(
            DataClassification.Public,
            [new SensitiveEntityFinding(string.Empty, 1)]);

        Assert.Equal(DataClassification.Unknown, escalation.ResultingClassification);
        Assert.Contains(ClassificationEscalator.MalformedFindingReason, escalation.ReasonCodes);
    }

    [Fact]
    public void ZeroCountFindingsAreNotEvidence()
    {
        var escalation = ClassificationEscalator.Evaluate(
            DataClassification.Public,
            Findings(("email-like", 0)));

        Assert.Equal(DataClassification.Public, escalation.ResultingClassification);
        Assert.Empty(escalation.ReasonCodes);
    }

    [Fact]
    public void EscalationIsIdempotent()
    {
        var detection = Findings(("phone-like", 1), ("custom-marker", 1));
        var first = ClassificationEscalator.Evaluate(DataClassification.Public, detection);
        var second = ClassificationEscalator.Evaluate(first.ResultingClassification, detection);

        Assert.Equal(first.ResultingClassification, second.ResultingClassification);
        Assert.Equal(first.MinimumClassification, second.MinimumClassification);
        Assert.Equal(first.ReasonCodes, second.ReasonCodes);
        Assert.False(second.Escalated);
    }

    [Fact]
    public void MinimumClassificationExposesThePureDetectionRequirement()
    {
        Assert.Equal(DataClassification.Restricted, ClassificationEscalator.GetMinimumClassification(Findings(("lei-like", 1))));
        Assert.Equal(DataClassification.Internal, ClassificationEscalator.GetMinimumClassification(Findings(("custom-marker", 1))));
        Assert.Equal(DataClassification.Public, ClassificationEscalator.GetMinimumClassification(SensitiveEntityDetectionResult.None));
        Assert.Equal(DataClassification.Unknown, ClassificationEscalator.GetMinimumClassification(SensitiveEntityDetectionResult.Failure));
    }

    [Fact]
    public void RestrictedCategoriesCoverTheDefaultDetectorCategories()
    {
        var detectorCategories = SensitiveEntityDetectorOptions.DefaultPatterns()
            .Select(pattern => pattern.Category)
            .ToArray();

        Assert.Equal(6, ClassificationEscalator.RestrictedCategories.Count);
        Assert.All(detectorCategories, category => Assert.True(ClassificationEscalator.IsRestrictedCategory(category)));
        Assert.False(ClassificationEscalator.IsRestrictedCategory("custom-marker"));
    }

    [Fact]
    public void SourceFirstClassifierEscalatesNewCategoriesButNeverDowngrades()
    {
        var classifier = new SourceFirstClassifier();

        Assert.Equal(
            DataClassification.Restricted,
            classifier.Classify(new ClassificationInput("Call 555-123-4567 now.", DataClassification.Public, "fixture")).Classification);
        Assert.Equal(
            DataClassification.Restricted,
            classifier.Classify(new ClassificationInput("ISIN XS1234567890 held.", DataClassification.Internal, "fixture")).Classification);
        Assert.Equal(
            DataClassification.Unknown,
            classifier.Classify(new ClassificationInput("ISIN XS1234567890 held.", DataClassification.Unknown, "fixture")).Classification);
        Assert.Equal(
            DataClassification.Public,
            classifier.Classify(new ClassificationInput("Ordinary workspace text.", DataClassification.Public, "fixture")).Classification);
    }
}
