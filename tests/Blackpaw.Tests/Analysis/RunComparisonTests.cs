using Blackpaw.Analysis;
using Blackpaw.Tests.TestHelpers;
using Xunit;

namespace Blackpaw.Tests.Analysis;

public class RunComparisonTests
{
    [Fact]
    public void Compare_HigherTargetCpu_CalculatesPositivePercentChange()
    {
        // Arrange
        var baseline = TestDataBuilder.CreateReportData(runId: 1, cpuBase: 50);
        var target = TestDataBuilder.CreateReportData(runId: 2, cpuBase: 75); // 50% higher

        // Act
        var comparison = RunComparison.Compare(baseline, target);

        // Assert
        Assert.NotNull(comparison.Result.System.CpuAvg.PercentChange);
        Assert.True(comparison.Result.System.CpuAvg.PercentChange > 0);
    }

    [Fact]
    public void Compare_LowerTargetCpu_CalculatesNegativePercentChange()
    {
        // Arrange
        var baseline = TestDataBuilder.CreateReportData(runId: 1, cpuBase: 80);
        var target = TestDataBuilder.CreateReportData(runId: 2, cpuBase: 40); // 50% lower

        // Act
        var comparison = RunComparison.Compare(baseline, target);

        // Assert
        Assert.NotNull(comparison.Result.System.CpuAvg.PercentChange);
        Assert.True(comparison.Result.System.CpuAvg.PercentChange < 0);
        // Lower CPU = improvement (lower is better for CPU)
        Assert.Equal(ChangeDirection.Improved, comparison.Result.System.CpuAvg.Direction);
    }

    [Fact]
    public void MetricComparison_LowerIsBetter_DecreaseIsImproved()
    {
        // Arrange - create a comparison where value decreased
        var baseline = TestDataBuilder.CreateReportData(runId: 1, cpuBase: 80);
        var target = TestDataBuilder.CreateReportData(runId: 2, cpuBase: 50);

        // Act
        var comparison = RunComparison.Compare(baseline, target);

        // Assert - CPU decreased, which is an improvement
        Assert.Equal(ChangeDirection.Improved, comparison.Result.System.CpuAvg.Direction);
    }

    [Fact]
    public void CalculateVerdict_AllMetricsImproved_ReturnsImproved()
    {
        // Arrange - target has significantly lower (better) CPU
        var baseline = TestDataBuilder.CreateReportData(runId: 1, cpuBase: 80);
        var target = TestDataBuilder.CreateReportData(runId: 2, cpuBase: 30);

        // Act
        var comparison = RunComparison.Compare(baseline, target);

        // Assert - should be Improved or MostlyImproved
        Assert.True(
            comparison.Result.Verdict == ComparisonVerdict.Improved ||
            comparison.Result.Verdict == ComparisonVerdict.MostlyImproved,
            $"Expected Improved or MostlyImproved but got {comparison.Result.Verdict}");
        Assert.True(comparison.Result.ImprovedCount > 0);
    }

    [Fact]
    public void CalculateVerdict_AllMetricsRegressed_ReturnsRegressed()
    {
        // Arrange - target has significantly higher (worse) CPU
        var baseline = TestDataBuilder.CreateReportData(runId: 1, cpuBase: 30);
        var target = TestDataBuilder.CreateReportData(runId: 2, cpuBase: 80);

        // Act
        var comparison = RunComparison.Compare(baseline, target);

        // Assert
        Assert.True(
            comparison.Result.Verdict == ComparisonVerdict.Regressed ||
            comparison.Result.Verdict == ComparisonVerdict.MostlyRegressed,
            $"Expected Regressed or MostlyRegressed but got {comparison.Result.Verdict}");
        Assert.True(comparison.Result.RegressedCount > 0);
    }

    [Fact]
    public void MetricComparison_FormatChange_ShowsArrowAndPercent()
    {
        // Arrange
        var baseline = TestDataBuilder.CreateReportData(runId: 1, cpuBase: 50);
        var target = TestDataBuilder.CreateReportData(runId: 2, cpuBase: 75);

        // Act
        var comparison = RunComparison.Compare(baseline, target);
        var formatted = comparison.Result.System.CpuAvg.FormatChange();

        // Assert - should contain an arrow and percent
        Assert.Contains("▲", formatted); // Increase arrow
        Assert.Contains("%", formatted);
    }

    [Fact]
    public void MetricComparison_GetIcon_ReturnsCorrectSymbol()
    {
        // Arrange
        var baseline = TestDataBuilder.CreateReportData(runId: 1, cpuBase: 80);
        var target = TestDataBuilder.CreateReportData(runId: 2, cpuBase: 40);

        // Act
        var comparison = RunComparison.Compare(baseline, target);
        var icon = comparison.Result.System.CpuAvg.GetIcon();

        // Assert - improvement should show checkmark
        Assert.Equal("✓", icon);
    }

    [Fact]
    public void Compare_BaselineAndTargetSummaries_ArePopulated()
    {
        // Arrange
        var baseline = TestDataBuilder.CreateReportData(runId: 1, scenario: "baseline");
        var target = TestDataBuilder.CreateReportData(runId: 2, scenario: "target");

        // Act
        var comparison = RunComparison.Compare(baseline, target);

        // Assert
        Assert.Equal(1, comparison.Baseline.RunId);
        Assert.Equal("baseline", comparison.Baseline.ScenarioName);
        Assert.Equal(2, comparison.Target.RunId);
        Assert.Equal("target", comparison.Target.ScenarioName);
    }
}
