using Blackpaw.Analysis;
using Blackpaw.Data;
using Blackpaw.Reporting;

namespace Blackpaw.Tests.TestHelpers;

/// <summary>
/// Helper class for building test data objects.
/// </summary>
public static class TestDataBuilder
{
    /// <summary>
    /// Creates a minimal SystemMetricsSummary for testing.
    /// </summary>
    public static SystemMetricsSummary CreateSystemMetrics(
        double cpuAvg = 50,
        double cpuP95 = 75,
        double cpuMax = 100,
        double memoryAvg = 8000,
        double memoryMax = 12000)
    {
        return new SystemMetricsSummary
        {
            Cpu = CreateMetricStats(cpuAvg, cpuP95, cpuMax),
            MemoryMb = CreateMetricStats(memoryAvg, memoryAvg * 1.2, memoryMax),
            MemoryAvailableMb = CreateMetricStats(4000),
            DiskReadBytesPerSec = CreateMetricStats(1000000),
            DiskWriteBytesPerSec = CreateMetricStats(500000),
            DiskReadOpsPerSec = CreateMetricStats(100),
            DiskWriteOpsPerSec = CreateMetricStats(50),
            NetSentBytesPerSec = CreateMetricStats(100000),
            NetReceivedBytesPerSec = CreateMetricStats(200000)
        };
    }

    /// <summary>
    /// Creates a minimal RunSummary for testing comparisons.
    /// </summary>
    public static RunSummary CreateRunSummary(
        long runId = 1,
        string scenario = "test",
        double cpuAvg = 50,
        double memoryAvg = 8000,
        List<ProcessMetricsSummary>? processes = null,
        List<DotNetRuntimeSummary>? dotNetApps = null,
        List<HttpEndpointSummary>? httpEndpoints = null)
    {
        return new RunSummary
        {
            RunId = runId,
            ScenarioName = scenario,
            StartedAtUtc = DateTime.UtcNow,
            DurationSeconds = 60,
            SampleCount = 60,
            System = CreateSystemMetrics(cpuAvg: cpuAvg, memoryAvg: memoryAvg),
            Processes = processes ?? new List<ProcessMetricsSummary>(),
            DotNetApps = dotNetApps ?? new List<DotNetRuntimeSummary>(),
            HttpEndpoints = httpEndpoints ?? new List<HttpEndpointSummary>(),
            Sql = null
        };
    }

    /// <summary>
    /// Creates a ProcessMetricsSummary for testing.
    /// </summary>
    public static ProcessMetricsSummary CreateProcessMetrics(
        string processName,
        double cpuAvg = 25,
        double memoryAvg = 500)
    {
        return new ProcessMetricsSummary
        {
            ProcessName = processName,
            SampleCount = 60,
            Cpu = CreateMetricStats(cpuAvg, cpuAvg * 1.5, cpuAvg * 2),
            WorkingSetMb = CreateMetricStats(memoryAvg),
            PrivateBytesMb = CreateMetricStats(memoryAvg * 0.8),
            ThreadCount = CreateMetricStats(20),
            HandleCount = CreateMetricStats(500)
        };
    }

    /// <summary>
    /// Creates MetricStats with reasonable defaults.
    /// </summary>
    public static MetricStats CreateMetricStats(
        double avg,
        double? p95 = null,
        double? max = null,
        int count = 60)
    {
        var actualP95 = p95 ?? avg * 1.2;
        var actualMax = max ?? avg * 1.5;
        var min = avg * 0.5;

        return new MetricStats
        {
            Count = count,
            Min = min,
            Max = actualMax,
            Sum = avg * count,
            Avg = avg,
            StdDev = avg * 0.1,
            P50 = avg,
            P75 = avg * 1.1,
            P90 = actualP95 * 0.95,
            P95 = actualP95,
            P99 = actualMax * 0.95
        };
    }

    /// <summary>
    /// Creates a minimal ReportData for testing.
    /// </summary>
    public static ReportData CreateReportData(
        long runId = 1,
        string scenario = "test",
        int sampleCount = 10,
        double cpuBase = 50)
    {
        var run = new RunRecord
        {
            RunId = runId,
            ScenarioName = scenario,
            StartedAtUtc = DateTime.UtcNow.AddMinutes(-sampleCount),
            EndedAtUtc = DateTime.UtcNow,
            DurationSeconds = sampleCount,
            ProbeVersion = "test",
            ConfigSnapshot = "{}"
        };

        var systemSamples = new List<SystemSample>();
        for (int i = 0; i < sampleCount; i++)
        {
            systemSamples.Add(new SystemSample
            {
                SampleId = i + 1,
                RunId = runId,
                TimestampUtc = run.StartedAtUtc.AddSeconds(i),
                CpuTotalPercent = cpuBase + (i % 10), // Vary slightly
                MemoryInUseMb = 8000 + (i * 10),
                MemoryAvailableMb = 8000 - (i * 10),
                DiskReadBytesPerSec = 1000000,
                DiskWriteBytesPerSec = 500000,
                DiskReadsPerSec = 100,
                DiskWritesPerSec = 50,
                NetBytesSentPerSec = 100000,
                NetBytesReceivedPerSec = 200000
            });
        }

        return new ReportData
        {
            Run = run,
            SystemSamples = systemSamples,
            ProcessSamples = new List<ProcessSample>(),
            DotNetRuntimeSamples = new List<DotNetRuntimeSample>(),
            DotNetHttpSamples = new List<DotNetHttpSample>(),
            SqlDmvSamples = new List<SqlDmvSample>(),
            Markers = new List<Marker>()
        };
    }
}
