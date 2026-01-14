using System.Diagnostics;
using Hardware.Info;
using Blackpaw.Configuration;
using Blackpaw.Data;
using Blackpaw.Diagnostics;
using Blackpaw.Monitoring;

namespace Blackpaw.Sampling;

public class SamplingSession : IDisposable
{
    private readonly DatabaseService _database;
    private readonly AppConfig _config;
    private readonly ProcessCpuTracker _processCpuTracker = new();
    private readonly HardwareInfo _hardwareInfo = new();
    private readonly PerformanceCounter? _cpuTotalCounter;
    private readonly PerformanceCounter? _diskReadsPerSecCounter;
    private readonly PerformanceCounter? _diskWritesPerSecCounter;
    private readonly PerformanceCounter? _diskReadBytesPerSecCounter;
    private readonly PerformanceCounter? _diskWriteBytesPerSecCounter;
    private readonly List<PerformanceCounter> _netSentCounters = new();
    private readonly List<PerformanceCounter> _netReceivedCounters = new();
    private bool _disposed;

    public SamplingSession(DatabaseService database, AppConfig config)
    {
        _database = database;
        _config = config;

        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("PerfProbe sampling requires Windows counters.");
        }

        _cpuTotalCounter = CreateCounter("Processor", "% Processor Time", "_Total");

        if (config.EnableDiskMetrics)
        {
            _diskReadsPerSecCounter = CreateCounter("PhysicalDisk", "Disk Reads/sec", "_Total");
            _diskWritesPerSecCounter = CreateCounter("PhysicalDisk", "Disk Writes/sec", "_Total");
            _diskReadBytesPerSecCounter = CreateCounter("PhysicalDisk", "Disk Read Bytes/sec", "_Total");
            _diskWriteBytesPerSecCounter = CreateCounter("PhysicalDisk", "Disk Write Bytes/sec", "_Total");
        }

        if (config.EnableNetworkMetrics)
        {
            try
            {
                var category = new PerformanceCounterCategory("Network Interface");
                foreach (var instance in category.GetInstanceNames())
                {
                    _netSentCounters.Add(new PerformanceCounter("Network Interface", "Bytes Sent/sec", instance, true));
                    _netReceivedCounters.Add(new PerformanceCounter("Network Interface", "Bytes Received/sec", instance, true));
                }
            }
            catch (Exception ex)
            {
                Logger.Warning("Failed to create network counters", ex);
            }
        }

        // Prime counters
        _cpuTotalCounter?.NextValue();
        _diskReadsPerSecCounter?.NextValue();
        _diskWritesPerSecCounter?.NextValue();
        _diskReadBytesPerSecCounter?.NextValue();
        _diskWriteBytesPerSecCounter?.NextValue();
        foreach (var counter in _netSentCounters.Concat(_netReceivedCounters))
        {
            counter.NextValue();
        }
    }

    public async Task RunAsync(long runId, CancellationToken cancellationToken)
    {
        var monitoredNames = new HashSet<string>(_config.ProcessNames, StringComparer.OrdinalIgnoreCase);
        foreach (var app in _config.DeepMonitoring.DotNetCoreApps.Concat(_config.DeepMonitoring.DotNetFrameworkApps))
        {
            monitoredNames.Add(app.ProcessName);
        }

        using var processMonitor = new ProcessMonitor(_database, runId, monitoredNames);
        processMonitor.Start();

        using var coreMonitor = new DotNetCoreRuntimeMonitor(_database, runId, _config.DeepMonitoring.DotNetCoreApps, _config.SampleIntervalSeconds);
        using var httpMonitor = new DotNetCoreHttpMonitor(_database, runId, _config.DeepMonitoring.DotNetCoreApps);
        var deepStartMonitor = new ProcessStartMonitor(_config.DeepMonitoring.DotNetCoreApps.Select(a => a.ProcessName));
        deepStartMonitor.ProcessStarted += coreMonitor.NotifyProcessStarted;
        deepStartMonitor.ProcessStarted += httpMonitor.NotifyProcessStarted;
        deepStartMonitor.Start();
        coreMonitor.AttachToExisting();
        httpMonitor.AttachToExisting();

        using var frameworkMonitor = new DotNetFrameworkRuntimeMonitor(_database, runId, _config.DeepMonitoring.DotNetFrameworkApps, _config.SampleIntervalSeconds);
        frameworkMonitor.Start();

        SqlDmvSampler? dmvSampler = null;
        if (_config.DeepMonitoring.SqlDmvSampling.Enabled && !string.IsNullOrWhiteSpace(_config.SqlConnectionString))
        {
            dmvSampler = new SqlDmvSampler(_database, runId, _config.SqlConnectionString!, _config.DeepMonitoring.SqlDmvSampling.SampleIntervalSeconds);
            dmvSampler.Start();
        }

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(_config.SampleIntervalSeconds));
        while (await timer.WaitForNextTickAsync(cancellationToken))
        {
            var timestamp = DateTime.UtcNow;
            var systemSample = CollectSystemSample(runId, timestamp);
            var sampleId = _database.InsertSystemSample(systemSample);
            var activeProcesses = processMonitor.GetActiveProcessesSnapshot();
            _processCpuTracker.TrimToActive(activeProcesses);
            var processSamples = CollectProcessSamples(sampleId, activeProcesses);
            if (processSamples.Count > 0)
            {
                _database.InsertProcessSamples(processSamples);
            }

            foreach (var process in activeProcesses)
            {
                process.Dispose();
            }
        }

        deepStartMonitor.Dispose();
        dmvSampler?.Dispose();
    }

    private SystemSample CollectSystemSample(long runId, DateTime timestampUtc)
    {
        var systemSample = new SystemSample
        {
            RunId = runId,
            TimestampUtc = timestampUtc
        };

        systemSample.CpuTotalPercent = SafeRead(_cpuTotalCounter);

        try
        {
            _hardwareInfo.RefreshMemoryStatus();
            var mem = _hardwareInfo.MemoryStatus;
            systemSample.MemoryAvailableMb = mem.AvailablePhysical / (1024d * 1024d);
            systemSample.MemoryInUseMb = mem.TotalPhysical / (1024d * 1024d) - systemSample.MemoryAvailableMb;
        }
        catch (Exception ex)
        {
            Logger.Debug($"Memory read failed: {ex.Message}");
        }

        if (_config.EnableDiskMetrics)
        {
            systemSample.DiskReadsPerSec = SafeRead(_diskReadsPerSecCounter);
            systemSample.DiskWritesPerSec = SafeRead(_diskWritesPerSecCounter);
            systemSample.DiskReadBytesPerSec = SafeRead(_diskReadBytesPerSecCounter);
            systemSample.DiskWriteBytesPerSec = SafeRead(_diskWriteBytesPerSecCounter);
        }

        if (_config.EnableNetworkMetrics && _netSentCounters.Count > 0)
        {
            systemSample.NetBytesSentPerSec = _netSentCounters
                .Select(SafeRead)
                .Where(v => v.HasValue)
                .Sum(v => v!.Value);
            systemSample.NetBytesReceivedPerSec = _netReceivedCounters
                .Select(SafeRead)
                .Where(v => v.HasValue)
                .Sum(v => v!.Value);
        }

        return systemSample;
    }

    private List<ProcessSample> CollectProcessSamples(long sampleId, List<Process> activeProcesses)
    {
        var results = new List<ProcessSample>();
        if (_config.ProcessNames.Count == 0 && _config.DeepMonitoring.DotNetCoreApps.Count == 0 && _config.DeepMonitoring.DotNetFrameworkApps.Count == 0)
        {
            return results;
        }

        foreach (var group in activeProcesses.GroupBy(p => p.ProcessName, StringComparer.OrdinalIgnoreCase))
        {
            double cpuPercent = 0;
            double workingSet = 0;
            double privateBytes = 0;
            int threadCount = 0;
            int handleCount = 0;

            foreach (var process in group)
            {
                try
                {
                    cpuPercent += _processCpuTracker.CalculateCpuPercent(process, _config.SampleIntervalSeconds, Environment.ProcessorCount);
                    workingSet += process.WorkingSet64 / (1024d * 1024d);
                    privateBytes += process.PrivateMemorySize64 / (1024d * 1024d);
                    threadCount += process.Threads.Count;
                    handleCount += process.HandleCount;
                }
                catch (Exception ex)
                {
                    Logger.Debug($"Process metrics read failed for {process.Id}: {ex.Message}");
                }
            }

            results.Add(new ProcessSample
            {
                SampleId = sampleId,
                ProcessName = group.Key,
                CpuPercent = cpuPercent,
                WorkingSetMb = workingSet,
                PrivateBytesMb = privateBytes,
                ThreadCount = threadCount,
                HandleCount = handleCount
            });
        }

        return results;
    }

    private static double? SafeRead(PerformanceCounter? counter)
    {
        if (counter == null)
        {
            return null;
        }

        try
        {
            return Math.Round(counter.NextValue(), 2);
        }
        catch (Exception ex)
        {
            Logger.Debug($"Counter read failed for {counter.CounterName}: {ex.Message}");
            return null;
        }
    }

    private static PerformanceCounter? CreateCounter(string category, string counterName, string instance)
    {
        try
        {
            return new PerformanceCounter(category, counterName, instance, true);
        }
        catch (Exception ex)
        {
            Logger.Warning($"Failed to create counter {category}/{counterName}", ex);
            return null;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        _cpuTotalCounter?.Dispose();
        _diskReadsPerSecCounter?.Dispose();
        _diskWritesPerSecCounter?.Dispose();
        _diskReadBytesPerSecCounter?.Dispose();
        _diskWriteBytesPerSecCounter?.Dispose();

        foreach (var counter in _netSentCounters)
        {
            counter.Dispose();
        }

        foreach (var counter in _netReceivedCounters)
        {
            counter.Dispose();
        }

        _netSentCounters.Clear();
        _netReceivedCounters.Clear();
    }
}
