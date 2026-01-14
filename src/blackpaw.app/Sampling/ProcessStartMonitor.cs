using System.Diagnostics;
using System.Management;
using Blackpaw.Diagnostics;

namespace Blackpaw.Sampling;

public sealed class ProcessStartMonitor : IDisposable
{
    private readonly HashSet<string> _targetNames;
    private ManagementEventWatcher? _startWatcher;

    public event Action<int, string>? ProcessStarted;

    public ProcessStartMonitor(IEnumerable<string> processNames)
    {
        _targetNames = new HashSet<string>(processNames.Select(NormalizeName), StringComparer.OrdinalIgnoreCase);
    }

    public void Start()
    {
        if (_targetNames.Count == 0)
        {
            return;
        }

        SeedExisting();

        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        try
        {
            _startWatcher = new ManagementEventWatcher(new WqlEventQuery("SELECT * FROM Win32_ProcessStartTrace"));
            _startWatcher.EventArrived += OnProcessStarted;
            _startWatcher.Start();
        }
        catch (Exception ex)
        {
            Logger.Warning("Failed to start WMI process watcher (requires elevated privileges)", ex);
            Dispose();
        }
    }

    private void SeedExisting()
    {
        foreach (var name in _targetNames)
        {
            Process[]? processes = null;
            try
            {
                processes = Process.GetProcessesByName(name);
            }
            catch (Exception ex)
            {
                Logger.Debug($"Failed to enumerate processes for {name}: {ex.Message}");
            }

            if (processes == null)
            {
                continue;
            }

            foreach (var process in processes)
            {
                Raise(process.Id, process.ProcessName);
                process.Dispose();
            }
        }
    }

    private void OnProcessStarted(object sender, EventArrivedEventArgs e)
    {
        var name = NormalizeName(e.NewEvent?["ProcessName"]?.ToString());
        if (string.IsNullOrWhiteSpace(name) || !_targetNames.Contains(name))
        {
            return;
        }

        if (int.TryParse(e.NewEvent?["ProcessID"]?.ToString(), out var pid))
        {
            Raise(pid, name);
        }
    }

    private void Raise(int pid, string name)
    {
        ProcessStarted?.Invoke(pid, name);
    }

    private static string NormalizeName(string? name) => string.IsNullOrWhiteSpace(name) ? string.Empty : Path.GetFileNameWithoutExtension(name);

    public void Dispose()
    {
        _startWatcher?.Dispose();
    }
}
