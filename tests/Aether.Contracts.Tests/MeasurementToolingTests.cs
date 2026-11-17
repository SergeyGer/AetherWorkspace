using Aether.Testing;
using Xunit;

namespace Aether.Contracts.Tests;

/// <summary>Guards the v1.1 AW-065/AW-066 physical measurement tooling contracts.</summary>
public class MeasurementToolingTests
{
    [Fact]
    public void WtMatrixDocumentsEveryReferenceDesktopTestId()
    {
        var matrix = File.ReadAllText(TestRepo.PathTo("docs", "testing", "wt-matrix.md"));
        for (var id = 1; id <= 8; id++)
        {
            Assert.Contains($"WT-04-{id:00}", matrix, StringComparison.Ordinal);
        }

        for (var id = 1; id <= 10; id++)
        {
            Assert.Contains($"WT-05-{id:00}", matrix, StringComparison.Ordinal);
        }

        Assert.Contains("MANUAL_PENDING", matrix, StringComparison.Ordinal);
        Assert.Contains("restore 100%", matrix, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void WtLedgerExporterMapsTestsAndMergesManualEntries()
    {
        var script = File.ReadAllText(TestRepo.PathTo("tools", "windows-test", "Export-WtLedger.ps1"));

        Assert.Contains("WT-04-", script, StringComparison.Ordinal);
        Assert.Contains("WT-05-", script, StringComparison.Ordinal);
        Assert.Contains("ManualLedgerPath", script, StringComparison.Ordinal);
        Assert.Contains("wt-ledger-", script, StringComparison.Ordinal);
        Assert.Contains("MANUAL_PENDING", script, StringComparison.Ordinal);
    }

    [Fact]
    public void UiResponsivenessRunnerAndShellTelemetryAreWired()
    {
        var runner = File.ReadAllText(TestRepo.PathTo("tools", "windows-test", "Measure-UiResponsiveness.ps1"));
        var shell = File.ReadAllText(TestRepo.PathTo("src", "Aether.Shell", "UiFrameTelemetry.cs"));
        var doc = File.ReadAllText(TestRepo.PathTo("docs", "testing", "ui-responsiveness.md"));

        Assert.Contains("AETHER_UI_CYCLES", runner, StringComparison.Ordinal);
        Assert.Contains("AETHER_UI_TELEMETRY_OUT", runner, StringComparison.Ordinal);
        Assert.Contains("SKIPPED_ENVIRONMENT", runner, StringComparison.Ordinal);
        Assert.Contains("p95FrameMs", runner, StringComparison.Ordinal);
        Assert.Contains("DispatcherTimer", shell, StringComparison.Ordinal);
        Assert.Contains("WorkingSetMb", shell, StringComparison.Ordinal);
        Assert.Contains("100-cycle", doc, StringComparison.OrdinalIgnoreCase);
    }
}
