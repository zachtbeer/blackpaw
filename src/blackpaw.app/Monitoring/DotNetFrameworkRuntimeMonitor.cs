using System.Diagnostics;
using Blackpaw.Configuration;
using Blackpaw.Data;
using Blackpaw.Diagnostics;

namespace Blackpaw.Monitoring;

public class DotNetFrameworkRuntimeMonitor : IDisposable
{
    private readonly DatabaseService _database;
    private readonly long _runId;
    private readonly List<DotNetAppConfig> _apps;
    private readonly double _intervalSeconds;
    private readonly CancellationTokenSource _cts = new();
    private readonly Dictionary<int, string> _instanceMap = new();
    private readonly object _lock = new();
    private Task? _loop;

    public DotNetFrameworkRuntimeMonitor(DatabaseService database, long runId, IEnumerable<DotNetAppConfig> apps, double intervalSeconds)
    {
        _database = database;
        _runId = runId;
        _apps = apps.Where(a => a.Enabled).ToList();
        _intervalSeconds = intervalSeconds <= 0 ? 1 : intervalSeconds;
    }

    public void Start()
    {
        if (_apps.Count == 0)
        {
            return;
        }

        _loop = Task.Run(() => RunAsync(_cts.Token));
    }

    private async Task RunAsync(CancellationToken token)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(_intervalSeconds));
        while (await timer.WaitForNextTickAsync(token))
        {
            foreach (var app in _apps)
            {
                System.Diagnostics.Process[]? processes = null;
                try
                {
                    processes = System.Diagnostics.Process.GetProcessesByName(app.ProcessName);
                }
                catch (Exception ex)
                {
                    Logger.Debug($"Failed to enumerate processes for {app.ProcessName}: {ex.Message}");
                    continue;
                }

                if (processes == null)
                {
                    continue;
                }

                foreach (var process in processes)
                {
                    try
                    {
                        var instance = ResolveInstanceName(process.Id);
                        if (instance == null)
                        {
                            continue;
                        }

                        var sample = CollectSample(app, instance, process, DateTime.UtcNow);
                        if (sample != null)
                        {
                            _database.InsertDotNetRuntimeSamples(new[] { sample });
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Debug($"Failed to collect sample for {app.ProcessName} (PID {process.Id}): {ex.Message}");
                    }
                    finally
                    {
                        process.Dispose();
                    }
                }
            }
        }
    }

    private DotNetRuntimeSample? CollectSample(DotNetAppConfig app, string instanceName, System.Diagnostics.Process process, DateTime timestamp)
    {
        try
        {
            using var heap = new PerformanceCounter(".NET CLR Memory", "# Bytes in all Heaps", instanceName, true);
            using var gen0 = new PerformanceCounter(".NET CLR Memory", "Gen 0 Collections/sec", instanceName, true);
            using var gen1 = new PerformanceCounter(".NET CLR Memory", "Gen 1 Collections/sec", instanceName, true);
            using var gen2 = new PerformanceCounter(".NET CLR Memory", "Gen 2 Collections/sec", instanceName, true);
            using var gcTime = new PerformanceCounter(".NET CLR Memory", "% Time in GC", instanceName, true);
            using var exceptions = new PerformanceCounter(".NET CLR Exceptions", "# of Exceps Thrown / sec", instanceName, true);
            using var threads = new PerformanceCounter(".NET CLR LocksAndThreads", "# of current logical Threads", instanceName, true);

            return new DotNetRuntimeSample
            {
                RunId = _runId,
                TimestampUtc = timestamp,
                AppName = app.Name,
                ProcessName = app.ProcessName,
                RuntimeKind = "Framework",
                HeapSizeMb = heap.NextValue() / (1024d * 1024d),
                Gen0CollectionsPerSec = gen0.NextValue(),
                Gen1CollectionsPerSec = gen1.NextValue(),
                Gen2CollectionsPerSec = gen2.NextValue(),
                GcTimePercent = gcTime.NextValue(),
                ExceptionsPerSec = exceptions.NextValue(),
                ThreadCount = threads.NextValue(),
                ThreadPoolThreadCount = process.Threads?.Count
            };
        }
        catch (Exception ex)
        {
            Logger.Debug($"Failed to read .NET Framework counters for {app.ProcessName}: {ex.Message}");
            return null;
        }
    }

    private string? ResolveInstanceName(int pid)
    {
        lock (_lock)
        {
            if (_instanceMap.TryGetValue(pid, out var existing))
            {
                return existing;
            }
        }

        try
        {
            var category = new PerformanceCounterCategory(".NET CLR Memory");
            foreach (var instance in category.GetInstanceNames())
            {
                try
                {
                    using var counter = new PerformanceCounter(".NET CLR Memory", "ID Process", instance, true);
                    if ((int)counter.NextValue() == pid)
                    {
                        lock (_lock)
                        {
                            _instanceMap[pid] = instance;
                        }
                        return instance;
                    }
                }
                catch
                {
                    // Expected for instances that don't match - continue silently
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Debug($"Failed to resolve .NET CLR instance name for PID {pid}: {ex.Message}");
            return null;
        }

        return null;
    }

    public void Dispose()
    {
        _cts.Cancel();
        try
        {
            _loop?.Wait(1000);
        }
        catch (Exception ex)
        {
            Logger.Debug($"Framework monitor loop wait failed during dispose: {ex.Message}");
        }
    }
}
