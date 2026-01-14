using System.Collections.Concurrent;
using System.Diagnostics.Tracing;
using Microsoft.Diagnostics.NETCore.Client;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Parsers;
using Blackpaw.Configuration;
using Blackpaw.Data;
using Blackpaw.Diagnostics;

namespace Blackpaw.Monitoring;

public class DotNetCoreRuntimeMonitor : IDisposable
{
    private readonly DatabaseService _database;
    private readonly long _runId;
    private readonly List<DotNetAppConfig> _apps;
    private readonly double _intervalSeconds;
    private readonly CancellationTokenSource _cts = new();
    private readonly ConcurrentDictionary<int, Task> _activeSessions = new();

    public DotNetCoreRuntimeMonitor(DatabaseService database, long runId, IEnumerable<DotNetAppConfig> apps, double intervalSeconds)
    {
        _database = database;
        _runId = runId;
        _apps = apps.Where(a => a.Enabled).ToList();
        _intervalSeconds = intervalSeconds <= 0 ? 1 : intervalSeconds;
    }

    public void AttachToExisting()
    {
        foreach (var app in _apps)
        {
            try
            {
                foreach (var process in System.Diagnostics.Process.GetProcessesByName(app.ProcessName))
                {
                    TryAttach(app, process.Id);
                    process.Dispose();
                }
            }
            catch (Exception ex)
            {
                Logger.Warning($"Failed to discover existing processes for {app.ProcessName}", ex);
            }
        }
    }

    public void NotifyProcessStarted(int pid, string processName)
    {
        var app = _apps.FirstOrDefault(a => string.Equals(a.ProcessName, processName, StringComparison.OrdinalIgnoreCase));
        if (app != null)
        {
            TryAttach(app, pid);
        }
    }

    private void TryAttach(DotNetAppConfig app, int pid)
    {
        if (!_activeSessions.TryAdd(pid, Task.CompletedTask))
        {
            return;
        }

        _activeSessions[pid] = Task.Run(() => CollectAsync(app, pid, _cts.Token));
    }

    private async Task CollectAsync(DotNetAppConfig app, int pid, CancellationToken token)
    {
        try
        {
            var client = new DiagnosticsClient(pid);
            var providers = new List<EventPipeProvider>
            {
                new("System.Runtime", EventLevel.Informational, (long)ClrTraceEventParser.Keywords.Default, new Dictionary<string, string>
                {
                    {"EventCounterIntervalSec", _intervalSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture)}
                })
            };

            using var session = client.StartEventPipeSession(providers);
            using var source = new EventPipeEventSource(session.EventStream);
            var lastEmit = DateTime.UtcNow;
            var counters = new Dictionary<string, double?>();

            source.Dynamic.All += traceEvent =>
            {
                if (traceEvent.EventName != "EventCounters")
                {
                    return;
                }

                if (traceEvent.PayloadByName("Payload") is not IDictionary<string, object> payload)
                {
                    return;
                }

                var name = payload["Name"]?.ToString();
                if (string.IsNullOrEmpty(name))
                {
                    return;
                }

                if (payload["Mean"] is double mean)
                {
                    counters[name] = mean;
                }
                else if (payload["Increment"] is double increment)
                {
                    counters[name] = increment;
                }

                var now = DateTime.UtcNow;
                if ((now - lastEmit).TotalSeconds >= _intervalSeconds - 0.2)
                {
                    EmitSample(app, counters, now);
                    lastEmit = now;
                }
            };

            var processingTask = Task.Run(() => source.Process());
            var tcs = new TaskCompletionSource();
            await using var registration = token.Register(() => tcs.TrySetResult());
            await Task.WhenAny(processingTask, tcs.Task);
            session.Stop();
        }
        catch (Exception ex)
        {
            Logger.Warning($"Runtime monitor attach failed for {app.ProcessName} (pid {pid})", ex);
        }
        finally
        {
            _activeSessions.TryRemove(pid, out _);
        }
    }

    private void EmitSample(DotNetAppConfig app, Dictionary<string, double?> counters, DateTime timestamp)
    {
        try
        {
            var sample = new DotNetRuntimeSample
            {
                RunId = _runId,
                TimestampUtc = timestamp,
                AppName = app.Name,
                ProcessName = app.ProcessName,
                RuntimeKind = "Core",
                HeapSizeMb = GetMb(counters, "gc-heap-size"),
                AllocRateMbPerSec = GetMb(counters, "allocation-rate"),
                Gen0CollectionsPerSec = GetValue(counters, "gen-0-gc-count"),
                Gen1CollectionsPerSec = GetValue(counters, "gen-1-gc-count"),
                Gen2CollectionsPerSec = GetValue(counters, "gen-2-gc-count"),
                GcTimePercent = GetValue(counters, "time-in-gc"),
                ExceptionsPerSec = GetValue(counters, "exception-count"),
                ThreadCount = GetValue(counters, "threadpool-thread-count"),
                ThreadPoolThreadCount = GetValue(counters, "threadpool-thread-count"),
                ThreadPoolQueueLength = GetValue(counters, "threadpool-queue-length")
            };

            _database.InsertDotNetRuntimeSamples(new[] { sample });
        }
        catch (Exception ex)
        {
            Logger.Warning($"Failed to insert runtime sample for {app.ProcessName}", ex);
        }
    }

    private static double? GetValue(Dictionary<string, double?> counters, string key)
    {
        return counters.TryGetValue(key, out var value) ? value : null;
    }

    private static double? GetMb(Dictionary<string, double?> counters, string key)
    {
        if (!counters.TryGetValue(key, out var value) || value == null)
        {
            return null;
        }

        return value / (1024d * 1024d);
    }

    public void Dispose()
    {
        _cts.Cancel();
        foreach (var session in _activeSessions.Values)
        {
            try
            {
                session.Wait(1000);
            }
            catch (Exception ex)
            {
                Logger.Debug($"Session wait failed during dispose: {ex.Message}");
            }
        }
    }
}
