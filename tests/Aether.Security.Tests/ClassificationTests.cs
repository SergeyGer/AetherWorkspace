using Aether.Contracts;
using Aether.Security.Classification;
using Aether.Testing;
using Xunit;

namespace Aether.Security.Tests;

public class ClassificationTests
{
    private static readonly SourceFirstClassifier Classifier = new();

    [Fact]
    public void MissingTrustedClassificationResolvesToUnknown()
    {
        var result = Classifier.Classify(new ClassificationInput("ordinary workspace text", null, "fixture"));
        Assert.Equal(DataClassification.Unknown, result.Classification);
        Assert.Contains(result.ReasonCodes, code => code.Contains("missing", StringComparison.Ordinal));
    }

    [Fact]
    public void TrustedSourceSetsTheMinimumClassification()
    {
        var result = Classifier.Classify(new ClassificationInput("ordinary workspace text", DataClassification.Internal, "fixture"));
        Assert.Equal(DataClassification.Internal, result.Classification);
        Assert.False(result.DetectorFailed);
    }

    [Fact]
    public void RuleDetectorEscalatesToRestricted()
    {
        var result = Classifier.Classify(new ClassificationInput(
            "Synthetic account ACC-00000001 with email analyst@example.invalid",
            DataClassification.Public,
            "fixture"));

        Assert.Equal(DataClassification.Restricted, result.Classification);
        Assert.Contains(result.ReasonCodes, code => code.Contains("synthetic-account-id", StringComparison.Ordinal));
    }

    [Fact]
    public void DetectorCanNeverDowngradeTrustedClassification()
    {
        var result = Classifier.Classify(new ClassificationInput("no sensitive patterns here", DataClassification.Restricted, "fixture"));
        Assert.Equal(DataClassification.Restricted, result.Classification);
    }

    [Fact]
    public void DetectorFailureFailsClosedToUnknown()
    {
        var classifier = new SourceFirstClassifier([new FailingSensitiveEntityDetector()]);
        var result = classifier.Classify(new ClassificationInput("any content", DataClassification.Public, "fixture"));

        Assert.Equal(DataClassification.Unknown, result.Classification);
        Assert.True(result.DetectorFailed);
    }

    [Fact]
    public void RuleDetectorReportsFindingsButNotContent()
    {
        var detector = new RuleBasedSensitiveEntityDetector();
        var detection = detector.Detect("ACC-00000001 ACC-00000002 contact analyst@example.invalid");

        Assert.False(detection.Failed);
        Assert.Contains(detection.Findings, finding => finding.Category == "synthetic-account-id" && finding.Count == 2);
        Assert.Contains(detection.Findings, finding => finding.Category == "email-like");
        Assert.DoesNotContain("ACC-00000001", detection.Findings.Select(f => f.Category));
    }

    [Fact]
    public void OversizedContentIsBoundedWithoutThrowing()
    {
        var detector = new RuleBasedSensitiveEntityDetector(
            new SensitiveEntityDetectorOptions { MaxContentLength = 1024 });
        var content = new string('a', 100_000) + " ACC-00000001";

        var detection = detector.Detect(content);
        Assert.False(detection.Failed);
        // The entity past the bound is not scanned; bounded work is the contract.
        Assert.Empty(detection.Findings);
    }

    [Fact]
    public void DetectorOptionsCanBeBuiltFromPolicyPattern()
    {
        var options = RuleBasedSensitiveEntityDetector.FromPolicy(@"ACC-[0-9]{8,12}");
        var detector = new RuleBasedSensitiveEntityDetector(options);
        var detection = detector.Detect("ACC-12345678");

        Assert.Single(detection.Findings);
    }
}
