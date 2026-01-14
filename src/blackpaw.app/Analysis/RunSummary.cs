using Blackpaw.Reporting;

namespace Blackpaw.Analysis;

/// <summary>
/// Aggregated summary statistics for a performance run.
/// Provides a structured view of all metrics for reporting and comparison.
/// </summary>
public class RunSummary
{
    public required long RunId { get; init; }
    public required string ScenarioName { get; init; }
    public required DateTime StartedAtUtc { get; init; }
    public required double? DurationSeconds { get; init; }
    public required int SampleCount { get; init; }

    // System metrics
    public required SystemMetricsSummary System { get; init; }

    // Per-process metrics
    public required List<ProcessMetricsSummary> Processes { get; init; }

    // .NET runtime metrics (per app)
    public required List<DotNetRuntimeSummary> DotNetApps { get; init; }

    // HTTP endpoint metrics (per endpoint)
    public required List<HttpEndpointSummary> HttpEndpoints { get; init; }

    // SQL Server metrics
    public required SqlMetricsSummary? Sql { get; init; }

    /// <summary>
    /// Creates a RunSummary from ReportData.
    /// </summary>
    public static RunSummary FromReportData(ReportData data)
    {
        var run = data.Run;

        return new RunSummary
        {
            RunId = run.RunId,
            ScenarioName = run.ScenarioName,
            StartedAtUtc = run.StartedAtUtc,
            DurationSeconds = run.DurationSeconds,
            SampleCount = data.SystemSamples.Count,

            System = CalculateSystemMetrics(data.SystemSamples),
            Processes = CalculateProcessMetrics(data.ProcessSamples),
            DotNetApps = CalculateDotNetMetrics(data.DotNetRuntimeSamples),
            HttpEndpoints = CalculateHttpMetrics(data.DotNetHttpSamples),
            Sql = CalculateSqlMetrics(data.SqlDmvSamples)
        };
    }

    private static SystemMetricsSummary CalculateSystemMetrics(List<Data.SystemSample> samples)
    {
        return new SystemMetricsSummary
        {
            Cpu = Statistics.Calculate(samples.Select(s => s.CpuTotalPercent)),
            MemoryMb = Statistics.Calculate(samples.Select(s => s.MemoryInUseMb)),
            MemoryAvailableMb = Statistics.Calculate(samples.Select(s => s.MemoryAvailableMb)),
            DiskReadBytesPerSec = Statistics.Calculate(samples.Select(s => s.DiskReadBytesPerSec)),
            DiskWriteBytesPerSec = Statistics.Calculate(samples.Select(s => s.DiskWriteBytesPerSec)),
            DiskReadOpsPerSec = Statistics.Calculate(samples.Select(s => s.DiskReadsPerSec)),
            DiskWriteOpsPerSec = Statistics.Calculate(samples.Select(s => s.DiskWritesPerSec)),
            NetSentBytesPerSec = Statistics.Calculate(samples.Select(s => s.NetBytesSentPerSec)),
            NetReceivedBytesPerSec = Statistics.Calculate(samples.Select(s => s.NetBytesReceivedPerSec))
        };
    }

    private static List<ProcessMetricsSummary> CalculateProcessMetrics(List<Data.ProcessSample> samples)
    {
        return samples
            .GroupBy(s => s.ProcessName)
            .Select(g => new ProcessMetricsSummary
            {
                ProcessName = g.Key,
                SampleCount = g.Count(),
                Cpu = Statistics.Calculate(g.Select(s => s.CpuPercent)),
                WorkingSetMb = Statistics.Calculate(g.Select(s => s.WorkingSetMb)),
                PrivateBytesMb = Statistics.Calculate(g.Select(s => s.PrivateBytesMb)),
                ThreadCount = Statistics.Calculate(g.Select(s => s.ThreadCount)),
                HandleCount = Statistics.Calculate(g.Select(s => s.HandleCount))
            })
            .OrderBy(p => p.ProcessName)
            .ToList();
    }

    private static List<DotNetRuntimeSummary> CalculateDotNetMetrics(List<Data.DotNetRuntimeSample> samples)
    {
        return samples
            .GroupBy(s => s.AppName)
            .Select(g => new DotNetRuntimeSummary
            {
                AppName = g.Key,
                SampleCount = g.Count(),
                HeapSizeMb = Statistics.Calculate(g.Select(s => s.HeapSizeMb)),
                GcTimePercent = Statistics.Calculate(g.Select(s => s.GcTimePercent)),
                Gen0CollectionsPerSec = Statistics.Calculate(g.Select(s => s.Gen0CollectionsPerSec)),
                Gen1CollectionsPerSec = Statistics.Calculate(g.Select(s => s.Gen1CollectionsPerSec)),
                Gen2CollectionsPerSec = Statistics.Calculate(g.Select(s => s.Gen2CollectionsPerSec)),
                // Estimate total collections (sum of per-second rates * sample count in seconds)
                TotalGen0Collections = (long)g.Sum(s => s.Gen0CollectionsPerSec ?? 0),
                TotalGen1Collections = (long)g.Sum(s => s.Gen1CollectionsPerSec ?? 0),
                TotalGen2Collections = (long)g.Sum(s => s.Gen2CollectionsPerSec ?? 0),
                ExceptionsPerSec = Statistics.Calculate(g.Select(s => s.ExceptionsPerSec)),
                TotalExceptionsEstimate = (int)g.Sum(s => s.ExceptionsPerSec ?? 0),
                ThreadPoolThreadCount = Statistics.Calculate(g.Select(s => s.ThreadPoolThreadCount)),
                ThreadPoolQueueLength = Statistics.Calculate(g.Select(s => s.ThreadPoolQueueLength))
            })
            .OrderBy(a => a.AppName)
            .ToList();
    }

    private static List<HttpEndpointSummary> CalculateHttpMetrics(List<Data.DotNetHttpSample> samples)
    {
        return samples
            .GroupBy(s => s.EndpointGroup)
            .Select(g => new HttpEndpointSummary
            {
                Endpoint = g.Key,
                TotalRequests = g.Sum(s => s.RequestCount),
                TotalSuccess = g.Sum(s => s.SuccessCount),
                Total4xx = g.Sum(s => s.Error4xxCount),
                Total5xx = g.Sum(s => s.Error5xxCount),
                LatencyMs = Statistics.Calculate(g.Where(s => s.AvgDurationMs.HasValue).Select(s => s.AvgDurationMs!.Value)),
                MinLatencyMs = g.Min(s => s.MinDurationMs) ?? 0,
                MaxLatencyMs = g.Max(s => s.MaxDurationMs) ?? 0
            })
            .OrderByDescending(e => e.TotalRequests)
            .ToList();
    }

    private static SqlMetricsSummary? CalculateSqlMetrics(List<Data.SqlDmvSample> samples)
    {
        if (samples.Count == 0)
        {
            return null;
        }

        return new SqlMetricsSummary
        {
            SampleCount = samples.Count,
            ActiveRequests = Statistics.Calculate(samples.Select(s => s.ActiveRequestsCount)),
            BlockedRequests = Statistics.Calculate(samples.Select(s => s.BlockedRequestsCount)),
            TotalWaitTimeMs = Statistics.Calculate(samples.Select(s => s.TotalWaitTimeMs)),
            UserConnections = Statistics.Calculate(samples.Select(s => s.UserConnections))
        };
    }
}

public class SystemMetricsSummary
{
    public required MetricStats Cpu { get; init; }
    public required MetricStats MemoryMb { get; init; }
    public required MetricStats MemoryAvailableMb { get; init; }
    public required MetricStats DiskReadBytesPerSec { get; init; }
    public required MetricStats DiskWriteBytesPerSec { get; init; }
    public required MetricStats DiskReadOpsPerSec { get; init; }
    public required MetricStats DiskWriteOpsPerSec { get; init; }
    public required MetricStats NetSentBytesPerSec { get; init; }
    public required MetricStats NetReceivedBytesPerSec { get; init; }
}

public class ProcessMetricsSummary
{
    public required string ProcessName { get; init; }
    public required int SampleCount { get; init; }
    public required MetricStats Cpu { get; init; }
    public required MetricStats WorkingSetMb { get; init; }
    public required MetricStats PrivateBytesMb { get; init; }
    public required MetricStats ThreadCount { get; init; }
    public required MetricStats HandleCount { get; init; }
}

public class DotNetRuntimeSummary
{
    public required string AppName { get; init; }
    public required int SampleCount { get; init; }
    public required MetricStats HeapSizeMb { get; init; }
    public required MetricStats GcTimePercent { get; init; }
    public required MetricStats Gen0CollectionsPerSec { get; init; }
    public required MetricStats Gen1CollectionsPerSec { get; init; }
    public required MetricStats Gen2CollectionsPerSec { get; init; }
    public required long TotalGen0Collections { get; init; }
    public required long TotalGen1Collections { get; init; }
    public required long TotalGen2Collections { get; init; }
    public required MetricStats ExceptionsPerSec { get; init; }
    public required int TotalExceptionsEstimate { get; init; }
    public required MetricStats ThreadPoolThreadCount { get; init; }
    public required MetricStats ThreadPoolQueueLength { get; init; }
}

public class HttpEndpointSummary
{
    public required string Endpoint { get; init; }
    public required int TotalRequests { get; init; }
    public required int TotalSuccess { get; init; }
    public required int Total4xx { get; init; }
    public required int Total5xx { get; init; }
    public required MetricStats LatencyMs { get; init; }
    public required double MinLatencyMs { get; init; }
    public required double MaxLatencyMs { get; init; }

    public double SuccessRate => TotalRequests > 0 ? (double)TotalSuccess / TotalRequests * 100 : 0;
    public double ErrorRate => TotalRequests > 0 ? (double)(Total4xx + Total5xx) / TotalRequests * 100 : 0;
}

public class SqlMetricsSummary
{
    public required int SampleCount { get; init; }
    public required MetricStats ActiveRequests { get; init; }
    public required MetricStats BlockedRequests { get; init; }
    public required MetricStats TotalWaitTimeMs { get; init; }
    public required MetricStats UserConnections { get; init; }
}
