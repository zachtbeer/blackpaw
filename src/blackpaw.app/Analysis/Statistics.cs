namespace Blackpaw.Analysis;

/// <summary>
/// Statistical calculations for performance metrics analysis.
/// </summary>
public static class Statistics
{
    /// <summary>
    /// Calculates comprehensive statistics for a collection of values.
    /// </summary>
    public static MetricStats Calculate(IEnumerable<double> values)
    {
        var sorted = values.Where(v => !double.IsNaN(v) && !double.IsInfinity(v)).OrderBy(v => v).ToList();

        if (sorted.Count == 0)
        {
            return MetricStats.Empty;
        }

        var count = sorted.Count;
        var sum = sorted.Sum();
        var avg = sum / count;

        // Variance and standard deviation
        var variance = sorted.Sum(v => Math.Pow(v - avg, 2)) / count;
        var stdDev = Math.Sqrt(variance);

        return new MetricStats
        {
            Count = count,
            Min = sorted[0],
            Max = sorted[count - 1],
            Sum = sum,
            Avg = avg,
            StdDev = stdDev,
            P50 = Percentile(sorted, 50),
            P75 = Percentile(sorted, 75),
            P90 = Percentile(sorted, 90),
            P95 = Percentile(sorted, 95),
            P99 = Percentile(sorted, 99)
        };
    }

    /// <summary>
    /// Calculates comprehensive statistics for nullable double values.
    /// </summary>
    public static MetricStats Calculate(IEnumerable<double?> values)
    {
        return Calculate(values.Where(v => v.HasValue).Select(v => v!.Value));
    }

    /// <summary>
    /// Calculates comprehensive statistics for integer values.
    /// </summary>
    public static MetricStats Calculate(IEnumerable<int> values)
    {
        return Calculate(values.Select(v => (double)v));
    }

    /// <summary>
    /// Calculates comprehensive statistics for nullable integer values.
    /// </summary>
    public static MetricStats Calculate(IEnumerable<int?> values)
    {
        return Calculate(values.Where(v => v.HasValue).Select(v => (double)v!.Value));
    }

    /// <summary>
    /// Calculates comprehensive statistics for long values.
    /// </summary>
    public static MetricStats Calculate(IEnumerable<long> values)
    {
        return Calculate(values.Select(v => (double)v));
    }

    /// <summary>
    /// Calculates comprehensive statistics for nullable long values.
    /// </summary>
    public static MetricStats Calculate(IEnumerable<long?> values)
    {
        return Calculate(values.Where(v => v.HasValue).Select(v => (double)v!.Value));
    }

    /// <summary>
    /// Calculates a specific percentile from a pre-sorted list.
    /// Uses linear interpolation between closest ranks.
    /// </summary>
    private static double Percentile(List<double> sortedValues, double percentile)
    {
        if (sortedValues.Count == 0) return 0;
        if (sortedValues.Count == 1) return sortedValues[0];

        var n = sortedValues.Count;
        var rank = (percentile / 100.0) * (n - 1);
        var lowerIndex = (int)Math.Floor(rank);
        var upperIndex = (int)Math.Ceiling(rank);

        if (lowerIndex == upperIndex)
        {
            return sortedValues[lowerIndex];
        }

        var fraction = rank - lowerIndex;
        return sortedValues[lowerIndex] + fraction * (sortedValues[upperIndex] - sortedValues[lowerIndex]);
    }

    /// <summary>
    /// Calculates the percentage change between two values.
    /// Returns null if baseline is zero or if either value is invalid.
    /// </summary>
    public static double? PercentChange(double baseline, double target)
    {
        if (baseline == 0 || double.IsNaN(baseline) || double.IsNaN(target))
        {
            return null;
        }

        return ((target - baseline) / baseline) * 100.0;
    }

    /// <summary>
    /// Determines if a metric change represents an improvement.
    /// For most metrics, lower is better (CPU, memory, latency).
    /// </summary>
    public static ChangeDirection GetChangeDirection(double? percentChange, bool lowerIsBetter = true)
    {
        if (!percentChange.HasValue)
        {
            return ChangeDirection.Unknown;
        }

        var threshold = 5.0; // Changes under 5% are considered neutral

        if (Math.Abs(percentChange.Value) < threshold)
        {
            return ChangeDirection.Neutral;
        }

        var isDecrease = percentChange.Value < 0;
        var isImprovement = lowerIsBetter ? isDecrease : !isDecrease;

        return isImprovement ? ChangeDirection.Improved : ChangeDirection.Regressed;
    }
}

/// <summary>
/// Comprehensive statistics for a metric.
/// </summary>
public record MetricStats
{
    public int Count { get; init; }
    public double Min { get; init; }
    public double Max { get; init; }
    public double Sum { get; init; }
    public double Avg { get; init; }
    public double StdDev { get; init; }
    public double P50 { get; init; }
    public double P75 { get; init; }
    public double P90 { get; init; }
    public double P95 { get; init; }
    public double P99 { get; init; }

    public static MetricStats Empty => new()
    {
        Count = 0,
        Min = 0,
        Max = 0,
        Sum = 0,
        Avg = 0,
        StdDev = 0,
        P50 = 0,
        P75 = 0,
        P90 = 0,
        P95 = 0,
        P99 = 0
    };

    public bool HasData => Count > 0;

    /// <summary>
    /// Formats the value with appropriate precision based on magnitude.
    /// </summary>
    public static string Format(double value, string unit = "", int decimals = 1)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            return "—";
        }

        var formatted = value switch
        {
            >= 1_000_000 => $"{value / 1_000_000:N1}M",
            >= 10_000 => $"{value / 1_000:N1}K",
            >= 100 => $"{value:N0}",
            >= 10 => $"{value:N1}",
            >= 1 => $"{value:N1}",
            > 0 => $"{value:N2}",
            _ => $"{value:N1}"
        };

        return string.IsNullOrEmpty(unit) ? formatted : $"{formatted} {unit}";
    }
}

/// <summary>
/// Direction of change between baseline and target metrics.
/// </summary>
public enum ChangeDirection
{
    Unknown,
    Improved,
    Regressed,
    Neutral
}
