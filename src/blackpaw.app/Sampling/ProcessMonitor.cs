using System.Diagnostics;
using Blackpaw.Data;
using Blackpaw.Diagnostics;

namespace Blackpaw.Sampling;

public sealed class ProcessMonitor : IDisposable
{
    private readonly DatabaseService _database;
    private readonly long _runId;
    private readonly ProcessStartMonitor _startMonitor;
    private readonly HashSet<int> _activePids = new();
    private readonly Dictionary<int, Process> _processWatchers = new();
    private readonly Dictionary<int, string> _processNames = new();
    private readonly object _syncRoot = new();

    public ProcessMonitor(DatabaseService database, long runId, IEnumerable<string> processNames)
    {
        _database = database;
        _runId = runId;
        _startMonitor = new ProcessStartMonitor(processNames);
        _startMonitor.ProcessStarted += OnProcessStarted;
    }

    public void Start()
    {
        _startMonitor.Start();
    }

    public List<Process> GetActiveProcessesSnapshot()
    {
        var processes = new List<Process>();
        List<int> snapshot;
        lock (_syncRoot)
        {
            snapshot = _activePids.ToList();
        }

        foreach (var pid in snapshot)
        {
            try
            {
                processes.Add(Process.GetProcessById(pid));
            }
            catch (ArgumentException)
            {
                RemoveProcess(pid, null);
            }
        }

        return processes;
    }

    private void OnProcessStarted(int pid, string name)
    {
        lock (_syncRoot)
        {
            if (_activePids.Contains(pid))
            {
                return;
            }

            _activePids.Add(pid);
            try
            {
                var process = Process.GetProcessById(pid);
                process.EnableRaisingEvents = true;
                process.Exited += (_, _) => RemoveProcess(pid, SafeExitCode(process));
                _processWatchers[pid] = process;
                _processNames[pid] = name;

                // Insert start marker before checking exit status
                InsertMarker($"Process {name} (PID {pid}) started.");

                // Handle race condition: process may have exited while we were setting up the handler
                if (process.HasExited)
                {
                    RemoveProcess(pid, SafeExitCode(process));
                }
            }
            catch (Exception ex)
            {
                Logger.Debug($"Failed to attach to process {name} (PID {pid}): {ex.Message}");
                _activePids.Remove(pid);
                return;
            }
        }
    }

    private void RemoveProcess(int pid, int? exitCode)
    {
        string? name = null;
        lock (_syncRoot)
        {
            if (!_activePids.Remove(pid))
            {
                return;
            }

            _processNames.TryGetValue(pid, out name);
            _processNames.Remove(pid);

            if (_processWatchers.TryGetValue(pid, out var process))
            {
                process.Dispose();
                _processWatchers.Remove(pid);
            }
        }

        var label = exitCode.HasValue
            ? $"Process {name ?? pid.ToString()} (PID {pid}) exited with code {exitCode.Value}."
            : $"Process {name ?? pid.ToString()} (PID {pid}) exited.";
        InsertMarker(label);
    }

    private void InsertMarker(string label)
    {
        try
        {
            _database.InsertMarker(new Marker
            {
                RunId = _runId,
                TimestampUtc = DateTime.UtcNow,
                MarkerType = "tool",
                Level = "info",
                Label = label
            });
        }
        catch (Exception ex)
        {
            Logger.Warning($"Failed to insert marker: {label}", ex);
        }
    }

    private static int? SafeExitCode(Process process)
    {
        try
        {
            return process.ExitCode;
        }
        catch
        {
            return null;
        }
    }

    public void Dispose()
    {
        _startMonitor.Dispose();
        lock (_syncRoot)
        {
            foreach (var watcher in _processWatchers.Values)
            {
                watcher.Dispose();
            }
            _processWatchers.Clear();
            _processNames.Clear();
            _activePids.Clear();
        }
    }
}
