using Blackpaw.Data;
using Blackpaw.Reporting;

namespace Blackpaw.Analysis;

/// <summary>
/// Compares two performance runs and produces structured comparison results.
/// </summary>
public class RunComparison
{
    public required RunSummary Baseline { get; init; }
    public required RunSummary Target { get; init; }
    public required ComparisonResult Result { get; init; }

    /// <summary>
    /// Creates a comparison between two runs.
    /// </summary>
    public static RunComparison Compare(ReportData baselineData, ReportData targetData)
    {
        var baseline = RunSummary.FromReportData(baselineData);
        var target = RunSummary.FromReportData(targetData);

        var result = new ComparisonResult
        {
            System = CompareSystemMetrics(baseline.System, target.System),
            Processes = CompareProcessMetrics(baseline.Processes, target.Processes),
            DotNetApps = CompareDotNetMetrics(baseline.DotNetApps, target.DotNetApps),
            HttpEndpoints = CompareHttpMetrics(baseline.HttpEndpoints, target.HttpEndpoints),
            Sql = baseline.Sql != null && target.Sql != null
                ? CompareSqlMetrics(baseline.Sql, target.Sql)
                : null
        };

        // Calculate overall verdict
        result.CalculateVerdict();

        return new RunComparison
        {
            Baseline = baseline,
            Target = target,
            Result = result
        };
    }

    private static SystemMetricsComparison CompareSystemMetrics(SystemMetricsSummary baseline, SystemMetricsSummary target)
    {
        return new SystemMetricsComparison
        {
            CpuAvg = CompareMetric("CPU (avg)", baseline.Cpu.Avg, target.Cpu.Avg, "%", lowerIsBetter: true),
            CpuP95 = CompareMetric("CPU (p95)", baseline.Cpu.P95, target.Cpu.P95, "%", lowerIsBetter: true),
            CpuMax = CompareMetric("CPU (max)", baseline.Cpu.Max, target.Cpu.Max, "%", lowerIsBetter: true),
            MemoryAvgMb = CompareMetric("Memory (avg)", baseline.MemoryMb.Avg, target.MemoryMb.Avg, "MB", lowerIsBetter: true),
            MemoryMaxMb = CompareMetric("Memory (max)", baseline.MemoryMb.Max, target.MemoryMb.Max, "MB", lowerIsBetter: true),
            DiskReadAvg = CompareMetric("Disk Read (avg)", baseline.DiskReadBytesPerSec.Avg, target.DiskReadBytesPerSec.Avg, "B/s", lowerIsBetter: false),
            DiskWriteAvg = CompareMetric("Disk Write (avg)", baseline.DiskWriteBytesPerSec.Avg, target.DiskWriteBytesPerSec.Avg, "B/s", lowerIsBetter: false),
            NetSentAvg = CompareMetric("Net Sent (avg)", baseline.NetSentBytesPerSec.Avg, target.NetSentBytesPerSec.Avg, "B/s", lowerIsBetter: false),
            NetReceivedAvg = CompareMetric("Net Received (avg)", baseline.NetReceivedBytesPerSec.Avg, target.NetReceivedBytesPerSec.Avg, "B/s", lowerIsBetter: false)
        };
    }

    private static List<ProcessMetricsComparison> CompareProcessMetrics(
        List<ProcessMetricsSummary> baseline,
        List<ProcessMetricsSummary> target)
    {
        var results = new List<ProcessMetricsComparison>();
        var allProcessNames = baseline.Select(p => p.ProcessName)
            .Union(target.Select(p => p.ProcessName))
            .OrderBy(n => n);

        foreach (var name in allProcessNames)
        {
            var baselineProc = baseline.FirstOrDefault(p => p.ProcessName == name);
            var targetProc = target.FirstOrDefault(p => p.ProcessName == name);

            if (baselineProc == null)
            {
                results.Add(new ProcessMetricsComparison
                {
                    ProcessName = name,
                    Status = ComparisonStatus.New,
                    CpuAvg = null,
                    MemoryAvgMb = null
                });
            }
            else if (targetProc == null)
            {
                results.Add(new ProcessMetricsComparison
                {
                    ProcessName = name,
                    Status = ComparisonStatus.Removed,
                    CpuAvg = null,
                    MemoryAvgMb = null
                });
            }
            else
            {
                results.Add(new ProcessMetricsComparison
                {
                    ProcessName = name,
                    Status = ComparisonStatus.Present,
                    CpuAvg = CompareMetric($"{name} CPU (avg)", baselineProc.Cpu.Avg, targetProc.Cpu.Avg, "%", lowerIsBetter: true),
                    CpuP95 = CompareMetric($"{name} CPU (p95)", baselineProc.Cpu.P95, targetProc.Cpu.P95, "%", lowerIsBetter: true),
                    CpuMax = CompareMetric($"{name} CPU (max)", baselineProc.Cpu.Max, targetProc.Cpu.Max, "%", lowerIsBetter: true),
                    MemoryAvgMb = CompareMetric($"{name} Memory (avg)", baselineProc.WorkingSetMb.Avg, targetProc.WorkingSetMb.Avg, "MB", lowerIsBetter: true),
                    MemoryP95Mb = CompareMetric($"{name} Memory (p95)", baselineProc.WorkingSetMb.P95, targetProc.WorkingSetMb.P95, "MB", lowerIsBetter: true),
                    MemoryMaxMb = CompareMetric($"{name} Memory (max)", baselineProc.WorkingSetMb.Max, targetProc.WorkingSetMb.Max, "MB", lowerIsBetter: true),
                    PrivateBytesAvgMb = CompareMetric($"{name} Private Bytes (avg)", baselineProc.PrivateBytesMb.Avg, targetProc.PrivateBytesMb.Avg, "MB", lowerIsBetter: true),
                    ThreadCountAvg = CompareMetric($"{name} Threads (avg)", baselineProc.ThreadCount.Avg, targetProc.ThreadCount.Avg, "", lowerIsBetter: false),
                    HandleCountAvg = CompareMetric($"{name} Handles (avg)", baselineProc.HandleCount.Avg, targetProc.HandleCount.Avg, "", lowerIsBetter: false)
                });
            }
        }

        return results;
    }

    private static List<DotNetMetricsComparison> CompareDotNetMetrics(
        List<DotNetRuntimeSummary> baseline,
        List<DotNetRuntimeSummary> target)
    {
        var results = new List<DotNetMetricsComparison>();
        var allAppNames = baseline.Select(a => a.AppName)
            .Union(target.Select(a => a.AppName))
            .OrderBy(n => n);

        foreach (var name in allAppNames)
        {
            var baselineApp = baseline.FirstOrDefault(a => a.AppName == name);
            var targetApp = target.FirstOrDefault(a => a.AppName == name);

            if (baselineApp == null || targetApp == null)
            {
                results.Add(new DotNetMetricsComparison
                {
                    AppName = name,
                    Status = baselineApp == null ? ComparisonStatus.New : ComparisonStatus.Removed
                });
                continue;
            }

            results.Add(new DotNetMetricsComparison
            {
                AppName = name,
                Status = ComparisonStatus.Present,
                HeapSizeAvgMb = CompareMetric("Heap (avg)", baselineApp.HeapSizeMb.Avg, targetApp.HeapSizeMb.Avg, "MB", lowerIsBetter: true),
                GcTimeAvg = CompareMetric("GC Time (avg)", baselineApp.GcTimePercent.Avg, targetApp.GcTimePercent.Avg, "%", lowerIsBetter: true),
                Gen0Collections = CompareMetric("Gen0 Collections", baselineApp.TotalGen0Collections, targetApp.TotalGen0Collections, "", lowerIsBetter: true),
                Gen1Collections = CompareMetric("Gen1 Collections", baselineApp.TotalGen1Collections, targetApp.TotalGen1Collections, "", lowerIsBetter: true),
                Gen2Collections = CompareMetric("Gen2 Collections", baselineApp.TotalGen2Collections, targetApp.TotalGen2Collections, "", lowerIsBetter: true),
                Exceptions = CompareMetric("Exceptions", baselineApp.TotalExceptionsEstimate, targetApp.TotalExceptionsEstimate, "", lowerIsBetter: true),
                ThreadPoolAvg = CompareMetric("ThreadPool (avg)", baselineApp.ThreadPoolThreadCount.Avg, targetApp.ThreadPoolThreadCount.Avg, "", lowerIsBetter: false),
                ThreadPoolQueueAvg = CompareMetric("ThreadPool Queue (avg)", baselineApp.ThreadPoolQueueLength.Avg, targetApp.ThreadPoolQueueLength.Avg, "", lowerIsBetter: true)
            });
        }

        return results;
    }

    private static List<HttpEndpointComparison> CompareHttpMetrics(
        List<HttpEndpointSummary> baseline,
        List<HttpEndpointSummary> target)
    {
        var results = new List<HttpEndpointComparison>();
        var allEndpoints = baseline.Select(e => e.Endpoint)
            .Union(target.Select(e => e.Endpoint))
            .OrderBy(e => e);

        foreach (var endpoint in allEndpoints)
        {
            var baselineEp = baseline.FirstOrDefault(e => e.Endpoint == endpoint);
            var targetEp = target.FirstOrDefault(e => e.Endpoint == endpoint);

            if (baselineEp == null)
            {
                results.Add(new HttpEndpointComparison
                {
                    Endpoint = endpoint,
                    Status = ComparisonStatus.New,
                    TargetRequests = targetEp?.TotalRequests ?? 0,
                    TargetLatencyP95 = targetEp?.LatencyMs.P95 ?? 0,
                    Target4xx = targetEp?.Total4xx ?? 0,
                    Target5xx = targetEp?.Total5xx ?? 0
                });
            }
            else if (targetEp == null)
            {
                results.Add(new HttpEndpointComparison
                {
                    Endpoint = endpoint,
                    Status = ComparisonStatus.Removed,
                    BaselineRequests = baselineEp.TotalRequests,
                    BaselineLatencyP95 = baselineEp.LatencyMs.P95,
                    Baseline4xx = baselineEp.Total4xx,
                    Baseline5xx = baselineEp.Total5xx
                });
            }
            else
            {
                results.Add(new HttpEndpointComparison
                {
                    Endpoint = endpoint,
                    Status = ComparisonStatus.Present,
                    BaselineRequests = baselineEp.TotalRequests,
                    TargetRequests = targetEp.TotalRequests,
                    BaselineLatencyP95 = baselineEp.LatencyMs.P95,
                    TargetLatencyP95 = targetEp.LatencyMs.P95,
                    Baseline4xx = baselineEp.Total4xx,
                    Target4xx = targetEp.Total4xx,
                    Baseline5xx = baselineEp.Total5xx,
                    Target5xx = targetEp.Total5xx,
                    LatencyAvg = CompareMetric($"{endpoint} Latency (avg)", baselineEp.LatencyMs.Avg, targetEp.LatencyMs.Avg, "ms", lowerIsBetter: true),
                    LatencyP95 = CompareMetric($"{endpoint} Latency (p95)", baselineEp.LatencyMs.P95, targetEp.LatencyMs.P95, "ms", lowerIsBetter: true),
                    ErrorRate = CompareMetric($"{endpoint} Error Rate", baselineEp.ErrorRate, targetEp.ErrorRate, "%", lowerIsBetter: true)
                });
            }
        }

        return results.OrderByDescending(e => e.BaselineRequests + e.TargetRequests).ToList();
    }

    private static SqlMetricsComparison CompareSqlMetrics(SqlMetricsSummary baseline, SqlMetricsSummary target)
    {
        return new SqlMetricsComparison
        {
            ActiveRequestsAvg = CompareMetric("Active Requests (avg)", baseline.ActiveRequests.Avg, target.ActiveRequests.Avg, "", lowerIsBetter: true),
            BlockedRequestsAvg = CompareMetric("Blocked Requests (avg)", baseline.BlockedRequests.Avg, target.BlockedRequests.Avg, "", lowerIsBetter: true),
            WaitTimeAvg = CompareMetric("Wait Time (avg)", baseline.TotalWaitTimeMs.Avg, target.TotalWaitTimeMs.Avg, "ms", lowerIsBetter: true),
            ConnectionsAvg = CompareMetric("Connections (avg)", baseline.UserConnections.Avg, target.UserConnections.Avg, "", lowerIsBetter: false)
        };
    }

    private static MetricComparison CompareMetric(string name, double baseline, double target, string unit, bool lowerIsBetter)
    {
        var percentChange = Statistics.PercentChange(baseline, target);
        var direction = Statistics.GetChangeDirection(percentChange, lowerIsBetter);

        return new MetricComparison
        {
            Name = name,
            BaselineValue = baseline,
            TargetValue = target,
            Unit = unit,
            PercentChange = percentChange,
            Direction = direction,
            LowerIsBetter = lowerIsBetter
        };
    }
}

/// <summary>
/// A single metric comparison between baseline and target.
/// </summary>
public class MetricComparison
{
    public required string Name { get; init; }
    public double BaselineValue { get; init; }
    public double TargetValue { get; init; }
    public string Unit { get; init; } = "";
    public double? PercentChange { get; init; }
    public ChangeDirection Direction { get; init; }
    public bool LowerIsBetter { get; init; }

    public string FormatChange()
    {
        if (!PercentChange.HasValue) return "—";

        var arrow = PercentChange.Value < 0 ? "▼" : PercentChange.Value > 0 ? "▲" : "—";
        var sign = PercentChange.Value > 0 ? "+" : "";
        return $"{arrow} {sign}{PercentChange.Value:N1}%";
    }

    public string GetIcon()
    {
        return Direction switch
        {
            ChangeDirection.Improved => "✓",
            ChangeDirection.Regressed => "⚠",
            ChangeDirection.Neutral => "—",
            _ => ""
        };
    }
}

/// <summary>
/// Status of an item in comparison (present in both, new, or removed).
/// </summary>
public enum ComparisonStatus
{
    Present,
    New,
    Removed
}

/// <summary>
/// Overall comparison results between two runs.
/// </summary>
public class ComparisonResult
{
    public required SystemMetricsComparison System { get; init; }
    public required List<ProcessMetricsComparison> Processes { get; init; }
    public required List<DotNetMetricsComparison> DotNetApps { get; init; }
    public required List<HttpEndpointComparison> HttpEndpoints { get; init; }
    public SqlMetricsComparison? Sql { get; init; }

    // Verdict summary
    public int ImprovedCount { get; private set; }
    public int RegressedCount { get; private set; }
    public int NeutralCount { get; private set; }
    public List<string> KeyImprovements { get; } = [];
    public List<string> KeyRegressions { get; } = [];
    public ComparisonVerdict Verdict { get; private set; }

    public void CalculateVerdict()
    {
        var allMetrics = GetAllMetrics().ToList();

        ImprovedCount = allMetrics.Count(m => m.Direction == ChangeDirection.Improved);
        RegressedCount = allMetrics.Count(m => m.Direction == ChangeDirection.Regressed);
        NeutralCount = allMetrics.Count(m => m.Direction == ChangeDirection.Neutral || m.Direction == ChangeDirection.Unknown);

        // Find key improvements (> 10% improvement)
        foreach (var metric in allMetrics.Where(m => m.Direction == ChangeDirection.Improved && Math.Abs(m.PercentChange ?? 0) > 10))
        {
            KeyImprovements.Add($"{metric.Name}: {metric.FormatChange()}");
        }

        // Find key regressions (> 5% regression)
        foreach (var metric in allMetrics.Where(m => m.Direction == ChangeDirection.Regressed && Math.Abs(m.PercentChange ?? 0) > 5))
        {
            KeyRegressions.Add($"{metric.Name}: {metric.FormatChange()}");
        }

        // Determine overall verdict
        if (RegressedCount == 0 && ImprovedCount > 0)
        {
            Verdict = ComparisonVerdict.Improved;
        }
        else if (ImprovedCount == 0 && RegressedCount > 0)
        {
            Verdict = ComparisonVerdict.Regressed;
        }
        else if (ImprovedCount > RegressedCount * 2)
        {
            Verdict = ComparisonVerdict.MostlyImproved;
        }
        else if (RegressedCount > ImprovedCount * 2)
        {
            Verdict = ComparisonVerdict.MostlyRegressed;
        }
        else
        {
            Verdict = ComparisonVerdict.Mixed;
        }
    }

    private IEnumerable<MetricComparison> GetAllMetrics()
    {
        // System metrics
        yield return System.CpuAvg;
        yield return System.CpuP95;
        yield return System.MemoryAvgMb;

        // Process metrics
        foreach (var proc in Processes.Where(p => p.Status == ComparisonStatus.Present))
        {
            if (proc.CpuAvg != null) yield return proc.CpuAvg;
            if (proc.MemoryAvgMb != null) yield return proc.MemoryAvgMb;
        }

        // .NET metrics
        foreach (var app in DotNetApps.Where(a => a.Status == ComparisonStatus.Present))
        {
            if (app.HeapSizeAvgMb != null) yield return app.HeapSizeAvgMb;
            if (app.GcTimeAvg != null) yield return app.GcTimeAvg;
            if (app.Gen2Collections != null) yield return app.Gen2Collections;
            if (app.Exceptions != null) yield return app.Exceptions;
        }

        // HTTP metrics
        foreach (var ep in HttpEndpoints.Where(e => e.Status == ComparisonStatus.Present))
        {
            if (ep.LatencyP95 != null) yield return ep.LatencyP95;
            if (ep.ErrorRate != null) yield return ep.ErrorRate;
        }

        // SQL metrics
        if (Sql != null)
        {
            yield return Sql.BlockedRequestsAvg;
            yield return Sql.WaitTimeAvg;
        }
    }
}

public enum ComparisonVerdict
{
    Improved,
    MostlyImproved,
    Mixed,
    MostlyRegressed,
    Regressed
}

public class SystemMetricsComparison
{
    public required MetricComparison CpuAvg { get; init; }
    public required MetricComparison CpuP95 { get; init; }
    public required MetricComparison CpuMax { get; init; }
    public required MetricComparison MemoryAvgMb { get; init; }
    public required MetricComparison MemoryMaxMb { get; init; }
    public required MetricComparison DiskReadAvg { get; init; }
    public required MetricComparison DiskWriteAvg { get; init; }
    public required MetricComparison NetSentAvg { get; init; }
    public required MetricComparison NetReceivedAvg { get; init; }
}

public class ProcessMetricsComparison
{
    public required string ProcessName { get; init; }
    public ComparisonStatus Status { get; init; }
    public MetricComparison? CpuAvg { get; init; }
    public MetricComparison? CpuP95 { get; init; }
    public MetricComparison? CpuMax { get; init; }
    public MetricComparison? MemoryAvgMb { get; init; }
    public MetricComparison? MemoryP95Mb { get; init; }
    public MetricComparison? MemoryMaxMb { get; init; }
    public MetricComparison? PrivateBytesAvgMb { get; init; }
    public MetricComparison? ThreadCountAvg { get; init; }
    public MetricComparison? HandleCountAvg { get; init; }
}

public class DotNetMetricsComparison
{
    public required string AppName { get; init; }
    public ComparisonStatus Status { get; init; }
    public MetricComparison? HeapSizeAvgMb { get; init; }
    public MetricComparison? GcTimeAvg { get; init; }
    public MetricComparison? Gen0Collections { get; init; }
    public MetricComparison? Gen1Collections { get; init; }
    public MetricComparison? Gen2Collections { get; init; }
    public MetricComparison? Exceptions { get; init; }
    public MetricComparison? ThreadPoolAvg { get; init; }
    public MetricComparison? ThreadPoolQueueAvg { get; init; }
}

public class HttpEndpointComparison
{
    public required string Endpoint { get; init; }
    public ComparisonStatus Status { get; init; }
    public int BaselineRequests { get; init; }
    public int TargetRequests { get; init; }
    public double BaselineLatencyP95 { get; init; }
    public double TargetLatencyP95 { get; init; }
    public int Baseline4xx { get; init; }
    public int Target4xx { get; init; }
    public int Baseline5xx { get; init; }
    public int Target5xx { get; init; }
    public MetricComparison? LatencyAvg { get; init; }
    public MetricComparison? LatencyP95 { get; init; }
    public MetricComparison? ErrorRate { get; init; }
}

public class SqlMetricsComparison
{
    public required MetricComparison ActiveRequestsAvg { get; init; }
    public required MetricComparison BlockedRequestsAvg { get; init; }
    public required MetricComparison WaitTimeAvg { get; init; }
    public required MetricComparison ConnectionsAvg { get; init; }
}
