using Blackpaw.Analysis;
using Xunit;

namespace Blackpaw.Tests.Analysis;

public class StatisticsTests
{
    [Fact]
    public void Calculate_WithValidValues_ReturnsCorrectStats()
    {
        // Arrange: 1, 2, 3, 4, 5 - easy to verify by hand
        var values = new[] { 1.0, 2.0, 3.0, 4.0, 5.0 };

        // Act
        var stats = Statistics.Calculate(values);

        // Assert
        Assert.Equal(5, stats.Count);
        Assert.Equal(1.0, stats.Min);
        Assert.Equal(5.0, stats.Max);
        Assert.Equal(15.0, stats.Sum);
        Assert.Equal(3.0, stats.Avg);
        Assert.Equal(3.0, stats.P50); // Median of 1,2,3,4,5 is 3
    }

    [Fact]
    public void Calculate_WithEmptyCollection_ReturnsEmpty()
    {
        // Arrange
        var values = Array.Empty<double>();

        // Act
        var stats = Statistics.Calculate(values);

        // Assert
        Assert.Equal(0, stats.Count);
        Assert.Equal(0, stats.Avg);
        Assert.Equal(0, stats.Min);
        Assert.Equal(0, stats.Max);
        Assert.False(stats.HasData);
    }

    [Fact]
    public void Calculate_WithSingleValue_ReturnsValueAsAllStats()
    {
        // Arrange
        var values = new[] { 42.0 };

        // Act
        var stats = Statistics.Calculate(values);

        // Assert
        Assert.Equal(1, stats.Count);
        Assert.Equal(42.0, stats.Min);
        Assert.Equal(42.0, stats.Max);
        Assert.Equal(42.0, stats.Avg);
        Assert.Equal(42.0, stats.P50);
        Assert.Equal(42.0, stats.P95);
        Assert.Equal(0, stats.StdDev); // No variance with single value
    }

    [Fact]
    public void Calculate_WithNaNValues_FiltersThemOut()
    {
        // Arrange
        var values = new[] { 1.0, double.NaN, 2.0, double.PositiveInfinity, 3.0 };

        // Act
        var stats = Statistics.Calculate(values);

        // Assert - should only include 1, 2, 3
        Assert.Equal(3, stats.Count);
        Assert.Equal(1.0, stats.Min);
        Assert.Equal(3.0, stats.Max);
        Assert.Equal(2.0, stats.Avg);
    }

    [Fact]
    public void Calculate_Percentiles_CorrectlyInterpolated()
    {
        // Arrange: 10 values from 1 to 10
        var values = Enumerable.Range(1, 10).Select(i => (double)i).ToArray();

        // Act
        var stats = Statistics.Calculate(values);

        // Assert
        Assert.Equal(10, stats.Count);
        // P50 should be around 5.5 (interpolated between 5 and 6)
        Assert.True(stats.P50 >= 5 && stats.P50 <= 6);
        // P90 should be near 9
        Assert.True(stats.P90 >= 8 && stats.P90 <= 10);
        // P99 should be very close to 10
        Assert.True(stats.P99 >= 9);
    }

    [Fact]
    public void PercentChange_BaselineZero_ReturnsNull()
    {
        // Arrange & Act
        var result = Statistics.PercentChange(0, 100);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void PercentChange_ValidValues_ReturnsCorrectPercent()
    {
        // Arrange & Act - 50 to 75 is 50% increase
        var result = Statistics.PercentChange(50, 75);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(50.0, result.Value, precision: 1);

        // 100 to 50 is 50% decrease
        var decrease = Statistics.PercentChange(100, 50);
        Assert.NotNull(decrease);
        Assert.Equal(-50.0, decrease.Value, precision: 1);
    }

    [Fact]
    public void GetChangeDirection_SmallChange_ReturnsNeutral()
    {
        // Arrange - changes under 5% should be neutral
        var smallIncrease = 3.0;
        var smallDecrease = -2.0;

        // Act & Assert
        Assert.Equal(ChangeDirection.Neutral, Statistics.GetChangeDirection(smallIncrease));
        Assert.Equal(ChangeDirection.Neutral, Statistics.GetChangeDirection(smallDecrease));
    }

    [Fact]
    public void GetChangeDirection_LargeDecrease_ReturnsImprovedWhenLowerIsBetter()
    {
        // Arrange
        var largeDecrease = -20.0; // 20% decrease

        // Act
        var direction = Statistics.GetChangeDirection(largeDecrease, lowerIsBetter: true);

        // Assert - decrease when lower is better = improvement
        Assert.Equal(ChangeDirection.Improved, direction);

        // Opposite when lower is NOT better
        var directionHigherBetter = Statistics.GetChangeDirection(largeDecrease, lowerIsBetter: false);
        Assert.Equal(ChangeDirection.Regressed, directionHigherBetter);
    }

    [Fact]
    public void MetricStats_Format_HandlesVariousMagnitudes()
    {
        // Small values
        Assert.Equal("0.50", MetricStats.Format(0.5));
        Assert.Equal("5.0", MetricStats.Format(5));

        // Medium values - returns with decimal for values >= 10
        Assert.Equal("50.0", MetricStats.Format(50));
        Assert.Equal("500", MetricStats.Format(500));

        // Large values get K suffix
        Assert.Equal("50.0K", MetricStats.Format(50000));

        // Very large values get M suffix
        Assert.Equal("5.0M", MetricStats.Format(5000000));

        // With unit
        Assert.Equal("100 MB", MetricStats.Format(100, "MB"));

        // NaN returns dash
        Assert.Equal("—", MetricStats.Format(double.NaN));
    }

    [Fact]
    public void Calculate_NullableDoubles_FiltersNulls()
    {
        // Arrange
        var values = new double?[] { 1.0, null, 2.0, null, 3.0 };

        // Act
        var stats = Statistics.Calculate(values);

        // Assert - should only include 1, 2, 3
        Assert.Equal(3, stats.Count);
        Assert.Equal(2.0, stats.Avg);
    }

    [Fact]
    public void Calculate_Integers_ConvertsCorrectly()
    {
        // Arrange
        var values = new[] { 10, 20, 30 };

        // Act
        var stats = Statistics.Calculate(values);

        // Assert
        Assert.Equal(3, stats.Count);
        Assert.Equal(20.0, stats.Avg);
    }

    [Fact]
    public void Calculate_AllSameValues_ReturnsZeroStdDev()
    {
        // Arrange
        var values = new[] { 5.0, 5.0, 5.0, 5.0 };

        // Act
        var stats = Statistics.Calculate(values);

        // Assert
        Assert.Equal(4, stats.Count);
        Assert.Equal(0, stats.StdDev);
        Assert.Equal(5.0, stats.Min);
        Assert.Equal(5.0, stats.Max);
        Assert.Equal(5.0, stats.Avg);
        Assert.Equal(5.0, stats.P50);
        Assert.Equal(5.0, stats.P95);
    }

    [Fact]
    public void Calculate_NegativeValues_HandledCorrectly()
    {
        // Arrange
        var values = new[] { -10.0, -5.0, 0.0, 5.0, 10.0 };

        // Act
        var stats = Statistics.Calculate(values);

        // Assert
        Assert.Equal(5, stats.Count);
        Assert.Equal(-10.0, stats.Min);
        Assert.Equal(10.0, stats.Max);
        Assert.Equal(0.0, stats.Avg);
    }

    [Fact]
    public void Calculate_VeryLargeValues_NoOverflow()
    {
        // Arrange
        var values = new[] { 1e15, 2e15, 3e15 };

        // Act
        var stats = Statistics.Calculate(values);

        // Assert
        Assert.Equal(3, stats.Count);
        Assert.Equal(1e15, stats.Min);
        Assert.Equal(3e15, stats.Max);
        Assert.Equal(2e15, stats.Avg);
    }

    [Fact]
    public void GetChangeDirection_NullPercentChange_ReturnsUnknown()
    {
        // Act
        var result = Statistics.GetChangeDirection(null);

        // Assert
        Assert.Equal(ChangeDirection.Unknown, result);
    }

    [Fact]
    public void PercentChange_NaNInputs_ReturnsNull()
    {
        // Act & Assert
        Assert.Null(Statistics.PercentChange(double.NaN, 100));
        Assert.Null(Statistics.PercentChange(100, double.NaN));
        Assert.Null(Statistics.PercentChange(double.NaN, double.NaN));
    }
}
