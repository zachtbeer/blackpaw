using System.Collections.Concurrent;
using System.Diagnostics.Tracing;
using Microsoft.Diagnostics.NETCore.Client;
using Microsoft.Diagnostics.Tracing;
using Blackpaw.Configuration;
using Blackpaw.Data;
using Blackpaw.Diagnostics;

namespace Blackpaw.Monitoring;

public class DotNetCoreHttpMonitor : IDisposable
{
    private readonly DatabaseService _database;
    private readonly long _runId;
    private readonly List<DotNetAppConfig> _apps;
    private readonly CancellationTokenSource _cts = new();
    private readonly ConcurrentDictionary<int, Task> _sessions = new();
    private readonly Dictionary<string, HttpBucketAggregator> _aggregators = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<int, ConcurrentDictionary<string, ActiveRequest>> _activeRequestsByPid = new();
    private readonly TimeSpan _flushInterval;
    private readonly TimeSpan _orphanedRequestTimeout = TimeSpan.FromMinutes(5);
    private readonly object _attachLock = new();
    private Task? _flushTask;

    public DotNetCoreHttpMonitor(DatabaseService database, long runId, IEnumerable<DotNetAppConfig> apps)
    {
        _database = database;
        _runId = runId;
        _apps = apps.Where(a => a.Enabled && a.HttpMonitoring.Enabled).ToList();

        foreach (var app in _apps)
        {
            _aggregators[app.Name] = new HttpBucketAggregator(app);
        }

        var intervalSeconds = _apps.Count == 0
            ? 5
            : Math.Max(1, _apps.Min(a => a.HttpMonitoring.BucketIntervalSeconds <= 0 ? 5 : a.HttpMonitoring.BucketIntervalSeconds));
        _flushInterval = TimeSpan.FromSeconds(intervalSeconds);

        if (_apps.Count > 0)
        {
            _flushTask = Task.Run(() => FlushLoopAsync(_cts.Token));
        }
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
        lock (_attachLock)
        {
            if (!_sessions.TryAdd(pid, Task.CompletedTask))
            {
                return;
            }

            _sessions[pid] = Task.Run(() => CollectAsync(app, pid, _cts.Token));
        }
    }

    private async Task CollectAsync(DotNetAppConfig app, int pid, CancellationToken token)
    {
        try
        {
            var client = new DiagnosticsClient(pid);
            var providers = new List<EventPipeProvider>
            {
                new("System.Net.Http", EventLevel.Informational)
            };

            using var session = client.StartEventPipeSession(providers);
            using var source = new EventPipeEventSource(session.EventStream);
            var activeRequests = new ConcurrentDictionary<string, ActiveRequest>(StringComparer.OrdinalIgnoreCase);
            _activeRequestsByPid[pid] = activeRequests;

            source.Dynamic.All += traceEvent =>
            {
                if (!traceEvent.EventName.Contains("Request", StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                try
                {
                    HandleHttpEvent(app, traceEvent, activeRequests);
                }
                catch (Exception ex)
                {
                    Logger.Debug($"HTTP event processing failed: {ex.Message}");
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
            _database.InsertMarker(new Marker
            {
                RunId = _runId,
                TimestampUtc = DateTime.UtcNow,
                MarkerType = "tool",
                Level = "error",
                Label = $"HTTP monitor attach failed for {app.ProcessName} (pid {pid}): {ex.Message}"
            });
        }
        finally
        {
            _sessions.TryRemove(pid, out _);
            _activeRequestsByPid.TryRemove(pid, out _);
        }
    }

    private void HandleHttpEvent(DotNetAppConfig app, TraceEvent traceEvent, ConcurrentDictionary<string, ActiveRequest> activeRequests)
    {
        var requestId = GetRequestId(traceEvent);
        if (string.IsNullOrEmpty(requestId))
        {
            return;
        }

        var eventName = traceEvent.EventName ?? string.Empty;
        if (eventName.Contains("Start", StringComparison.OrdinalIgnoreCase))
        {
            var method = GetPayloadValue(traceEvent, "Method") ?? GetPayloadValue(traceEvent, "method") ?? "";
            var host = GetPayloadValue(traceEvent, "Host") ?? GetPayloadValue(traceEvent, "host") ?? string.Empty;
            var path = GetPayloadValue(traceEvent, "PathAndQuery") ?? GetPayloadValue(traceEvent, "Path") ?? string.Empty;
            var timestamp = DateTime.UtcNow;
            activeRequests[requestId] = new ActiveRequest
            {
                StartUtc = timestamp,
                Method = method,
                Host = host,
                Path = path
            };
        }
        else if (eventName.Contains("Stop", StringComparison.OrdinalIgnoreCase) || eventName.Contains("Failed", StringComparison.OrdinalIgnoreCase))
        {
            if (!activeRequests.TryRemove(requestId, out var request))
            {
                return;
            }

            var statusText = GetPayloadValue(traceEvent, "StatusCode") ?? GetPayloadValue(traceEvent, "statusCode");
            int? statusCode = int.TryParse(statusText, out var parsedStatus) ? parsedStatus : null;
            var timestamp = DateTime.UtcNow;
            double? durationMs = null;
            if (traceEvent.PayloadByName("Duration") is double rawDuration)
            {
                durationMs = rawDuration;
            }
            else
            {
                durationMs = (timestamp - request.StartUtc).TotalMilliseconds;
            }

            var aggregator = _aggregators.GetValueOrDefault(app.Name);
            aggregator?.Record(new CompletedRequest
            {
                AppName = app.Name,
                ProcessName = app.ProcessName,
                TimestampUtc = timestamp,
                Method = request.Method,
                Host = request.Host,
                Path = request.Path,
                StatusCode = statusCode,
                DurationMs = durationMs
            });
        }
    }

    private async Task FlushLoopAsync(CancellationToken token)
    {
        using var timer = new PeriodicTimer(_flushInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(token))
            {
                FlushBuckets();
                CleanupOrphanedRequests();
            }
        }
        catch (OperationCanceledException)
        {
            // expected
        }
    }

    private void CleanupOrphanedRequests()
    {
        var cutoff = DateTime.UtcNow - _orphanedRequestTimeout;
        var totalCleaned = 0;

        foreach (var kvp in _activeRequestsByPid.ToArray())
        {
            var activeRequests = kvp.Value;
            var keysToRemove = new List<string>();

            foreach (var reqKvp in activeRequests.ToArray())
            {
                if (reqKvp.Value.StartUtc < cutoff)
                {
                    keysToRemove.Add(reqKvp.Key);
                }
            }

            foreach (var key in keysToRemove)
            {
                if (activeRequests.TryRemove(key, out _))
                {
                    totalCleaned++;
                }
            }
        }

        if (totalCleaned > 0)
        {
            Logger.Debug($"Cleaned up {totalCleaned} orphaned HTTP requests older than {_orphanedRequestTimeout.TotalMinutes} minutes");
        }
    }

    private void FlushBuckets()
    {
        foreach (var aggregator in _aggregators.Values)
        {
            var samples = aggregator.Flush(_runId);
            if (samples.Count > 0)
            {
                _database.InsertDotNetHttpSamples(samples);
            }
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        try
        {
            _flushTask?.Wait(1000);
        }
        catch (Exception ex)
        {
            Logger.Debug($"Flush task wait failed during dispose: {ex.Message}");
        }

        foreach (var session in _sessions.Values)
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

        FlushBuckets();
        _activeRequestsByPid.Clear();
    }

    private static string? GetRequestId(TraceEvent traceEvent)
    {
        return traceEvent.PayloadByName("RequestId")?.ToString()
               ?? traceEvent.PayloadByName("requestId")?.ToString()
               ?? traceEvent.PayloadString(0);
    }

    private static string? GetPayloadValue(TraceEvent traceEvent, string name)
    {
        return traceEvent.PayloadByName(name)?.ToString();
    }

    private class ActiveRequest
    {
        public DateTime StartUtc { get; set; }
        public string Method { get; set; } = string.Empty;
        public string Host { get; set; } = string.Empty;
        public string Path { get; set; } = string.Empty;
    }

    private class CompletedRequest
    {
        public DateTime TimestampUtc { get; set; }
        public string AppName { get; set; } = string.Empty;
        public string ProcessName { get; set; } = string.Empty;
        public string Method { get; set; } = string.Empty;
        public string Host { get; set; } = string.Empty;
        public string Path { get; set; } = string.Empty;
        public int? StatusCode { get; set; }
        public double? DurationMs { get; set; }
    }

    private class HttpBucketAggregator
    {
        private readonly DotNetAppConfig _app;
        private readonly object _lock = new();
        private readonly Dictionary<HttpBucketKey, HttpBucketStats> _buckets = new();
        private readonly double _bucketIntervalSeconds;

        public HttpBucketAggregator(DotNetAppConfig app)
        {
            _app = app;
            _bucketIntervalSeconds = app.HttpMonitoring.BucketIntervalSeconds <= 0 ? 5 : app.HttpMonitoring.BucketIntervalSeconds;
        }

        public void Record(CompletedRequest request)
        {
            var bucketStart = FloorTimestamp(request.TimestampUtc, _bucketIntervalSeconds);
            var endpointGroup = GetEndpointGroup(request.Host, request.Path, _app.HttpMonitoring.EndpointGrouping);
            var key = new HttpBucketKey(bucketStart, _app.Name, _app.ProcessName, endpointGroup);

            lock (_lock)
            {
                if (!_buckets.TryGetValue(key, out var stats))
                {
                    stats = new HttpBucketStats();
                    _buckets[key] = stats;
                }

                stats.RequestCount++;
                if (request.StatusCode is >= 200 and < 300)
                {
                    stats.SuccessCount++;
                }
                else if (request.StatusCode is >= 400 and < 500)
                {
                    stats.Error4xxCount++;
                }
                else if (request.StatusCode is >= 500 and < 600)
                {
                    stats.Error5xxCount++;
                }
                else
                {
                    stats.OtherStatusCount++;
                }

                if (request.DurationMs.HasValue)
                {
                    stats.DurationSamples++;
                    stats.TotalDurationMs += request.DurationMs.Value;
                    stats.MaxDurationMs = stats.MaxDurationMs.HasValue
                        ? Math.Max(stats.MaxDurationMs.Value, request.DurationMs.Value)
                        : request.DurationMs.Value;
                    stats.MinDurationMs = stats.MinDurationMs.HasValue
                        ? Math.Min(stats.MinDurationMs.Value, request.DurationMs.Value)
                        : request.DurationMs.Value;
                }
            }
        }

        public List<DotNetHttpSample> Flush(long runId)
        {
            var results = new List<DotNetHttpSample>();
            lock (_lock)
            {
                foreach (var kvp in _buckets)
                {
                    var key = kvp.Key;
                    var stats = kvp.Value;
                    results.Add(new DotNetHttpSample
                    {
                        RunId = runId,
                        TimestampUtc = key.BucketStartUtc,
                        AppName = key.AppName,
                        ProcessName = key.ProcessName,
                        EndpointGroup = key.EndpointGroup,
                        HttpMethod = "*",
                        RequestCount = stats.RequestCount,
                        SuccessCount = stats.SuccessCount,
                        Error4xxCount = stats.Error4xxCount,
                        Error5xxCount = stats.Error5xxCount,
                        OtherStatusCount = stats.OtherStatusCount,
                        AvgDurationMs = stats.DurationSamples > 0 ? stats.TotalDurationMs / stats.DurationSamples : null,
                        MaxDurationMs = stats.MaxDurationMs,
                        MinDurationMs = stats.MinDurationMs,
                        TotalBytesReceived = stats.TotalBytesReceived == 0 ? null : stats.TotalBytesReceived,
                        TotalBytesSent = stats.TotalBytesSent == 0 ? null : stats.TotalBytesSent
                    });
                }

                _buckets.Clear();
            }

            return results;
        }

        private static DateTime FloorTimestamp(DateTime timestampUtc, double bucketSeconds)
        {
            var intervalTicks = TimeSpan.FromSeconds(bucketSeconds).Ticks;
            var flooredTicks = (timestampUtc.Ticks / intervalTicks) * intervalTicks;
            return new DateTime(flooredTicks, DateTimeKind.Utc);
        }

        private static string GetEndpointGroup(string host, string path, string grouping)
        {
            var normalizedHost = string.IsNullOrWhiteSpace(host) ? "(unknown)" : host.ToLowerInvariant();
            if (!string.Equals(grouping, "HostAndFirstPathSegment", StringComparison.OrdinalIgnoreCase))
            {
                return normalizedHost;
            }

            var firstSegment = ExtractFirstPathSegment(path);
            return string.IsNullOrEmpty(firstSegment)
                ? normalizedHost
                : $"{normalizedHost}:{firstSegment.ToLowerInvariant()}";
        }

        private static string ExtractFirstPathSegment(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return string.Empty;
            }

            var trimmed = path.StartsWith('/') ? path[1..] : path;
            var separatorIndex = trimmed.IndexOf('/');
            return separatorIndex >= 0 ? trimmed[..separatorIndex] : trimmed;
        }

        private record HttpBucketKey(DateTime BucketStartUtc, string AppName, string ProcessName, string EndpointGroup);

        private class HttpBucketStats
        {
            public int RequestCount { get; set; }
            public int SuccessCount { get; set; }
            public int Error4xxCount { get; set; }
            public int Error5xxCount { get; set; }
            public int OtherStatusCount { get; set; }
            public int DurationSamples { get; set; }
            public double TotalDurationMs { get; set; }
            public double? MaxDurationMs { get; set; }
            public double? MinDurationMs { get; set; }
            public long TotalBytesSent { get; set; }
            public long TotalBytesReceived { get; set; }
        }
    }
}
