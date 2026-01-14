namespace Blackpaw.Data;

public class SchemaInfo
{
    public int Id { get; set; }
    public int SchemaVersion { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}

public class RunRecord
{
    public long RunId { get; set; }
    public string ScenarioName { get; set; } = string.Empty;
    public string? Notes { get; set; }
    public DateTime StartedAtUtc { get; set; }
    public DateTime? EndedAtUtc { get; set; }
    public double? DurationSeconds { get; set; }
    public string ProbeVersion { get; set; } = "1.0.0";
    public string ConfigSnapshot { get; set; } = string.Empty;
    public string MachineName { get; set; } = string.Empty;
    public string OsVersion { get; set; } = string.Empty;
    public int CpuLogicalCoreCount { get; set; }
    public string? CpuModel { get; set; }
    public double TotalPhysicalMemoryMb { get; set; }
    public string? SystemDriveType { get; set; }
    public double SystemUptimeSecondsAtStart { get; set; }
    public double SystemDriveFreeSpaceMbAtStart { get; set; }
    public string? WorkloadType { get; set; }
    public int? WorkloadSizeEstimate { get; set; }
    public string? WorkloadNotes { get; set; }
}

public class SystemSample
{
    public long SampleId { get; set; }
    public long RunId { get; set; }
    public DateTime TimestampUtc { get; set; }
    public double? CpuTotalPercent { get; set; }
    public double? MemoryInUseMb { get; set; }
    public double? MemoryAvailableMb { get; set; }
    public double? DiskReadsPerSec { get; set; }
    public double? DiskWritesPerSec { get; set; }
    public double? DiskReadBytesPerSec { get; set; }
    public double? DiskWriteBytesPerSec { get; set; }
    public double? NetBytesSentPerSec { get; set; }
    public double? NetBytesReceivedPerSec { get; set; }
}

public class ProcessSample
{
    public long ProcessSampleId { get; set; }
    public long SampleId { get; set; }
    public string ProcessName { get; set; } = string.Empty;
    public double? CpuPercent { get; set; }
    public double? WorkingSetMb { get; set; }
    public double? PrivateBytesMb { get; set; }
    public int? ThreadCount { get; set; }
    public int? HandleCount { get; set; }
}

public class DbSnapshot
{
    public long DbSnapshotId { get; set; }
    public long RunId { get; set; }
    public DateTime TimestampUtc { get; set; }
    public string Label { get; set; } = string.Empty;
    public double? RequestsPerSec { get; set; }
    public double? TransactionsPerSec { get; set; }
    public double? CompilationsPerSec { get; set; }
    public double? RecompilationsPerSec { get; set; }
    public double? BufferCacheHitRatio { get; set; }
    public double? PageLifeExpectancySeconds { get; set; }
    public double? TotalServerMemoryMb { get; set; }
    public double? TargetServerMemoryMb { get; set; }
    public int? UserConnectionCount { get; set; }
    public int? ActiveSessionCount { get; set; }
    public double? LogFlushesPerSec { get; set; }
    public double? LogBytesFlushedPerSec { get; set; }
}

public class Marker
{
    public long MarkerId { get; set; }
    public long RunId { get; set; }
    public DateTime TimestampUtc { get; set; }
    public string MarkerType { get; set; } = "user";
    public string Label { get; set; } = string.Empty;
    public string? Level { get; set; }
}

public class DotNetRuntimeSample
{
    public int Id { get; set; }
    public long RunId { get; set; }
    public DateTime TimestampUtc { get; set; }
    public string AppName { get; set; } = string.Empty;
    public string ProcessName { get; set; } = string.Empty;
    public string RuntimeKind { get; set; } = string.Empty;
    public double? HeapSizeMb { get; set; }
    public double? AllocRateMbPerSec { get; set; }
    public double? Gen0CollectionsPerSec { get; set; }
    public double? Gen1CollectionsPerSec { get; set; }
    public double? Gen2CollectionsPerSec { get; set; }
    public double? GcTimePercent { get; set; }
    public double? ExceptionsPerSec { get; set; }
    public double? ThreadCount { get; set; }
    public double? ThreadPoolThreadCount { get; set; }
    public double? ThreadPoolQueueLength { get; set; }
}

public class SqlDmvSample
{
    public int Id { get; set; }
    public long RunId { get; set; }
    public DateTime TimestampUtc { get; set; }
    public string? DatabaseName { get; set; }
    public int? ActiveRequestsCount { get; set; }
    public int? WaitingTasksCount { get; set; }
    public int? BlockedRequestsCount { get; set; }
    public int? UserConnections { get; set; }
    public int? SessionsRunning { get; set; }
    public string? TopWaitType { get; set; }
    public double? TopWaitTimeMs { get; set; }
    public double? TotalWaitTimeMs { get; set; }
    public double? ReadStallMsPerRead { get; set; }
    public double? WriteStallMsPerWrite { get; set; }
    public double? ReadBytesPerSec { get; set; }
    public double? WriteBytesPerSec { get; set; }
}

public class DotNetHttpSample
{
    public int Id { get; set; }
    public long RunId { get; set; }
    public DateTime TimestampUtc { get; set; }
    public string AppName { get; set; } = string.Empty;
    public string ProcessName { get; set; } = string.Empty;
    public string EndpointGroup { get; set; } = string.Empty;
    public string HttpMethod { get; set; } = "*";
    public int RequestCount { get; set; }
    public int SuccessCount { get; set; }
    public int Error4xxCount { get; set; }
    public int Error5xxCount { get; set; }
    public int OtherStatusCount { get; set; }
    public double? AvgDurationMs { get; set; }
    public double? MaxDurationMs { get; set; }
    public double? MinDurationMs { get; set; }
    public long? TotalBytesSent { get; set; }
    public long? TotalBytesReceived { get; set; }
}
