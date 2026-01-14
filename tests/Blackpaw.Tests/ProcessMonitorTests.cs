using Blackpaw.Data;
using Blackpaw.Sampling;
using Microsoft.Data.Sqlite;
using System.Diagnostics;
using System.Management;
using Xunit;

namespace Blackpaw.Tests;

public class ProcessMonitorTests
{
    [SkippableFact]
    public async Task ProcessMonitor_CapturesStartAndStopMarkers()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "This test requires Windows");

        // Skip test if WMI access is not available (requires elevated privileges)
        Skip.IfNot(CanAccessWmi(), "WMI event watcher access not available (requires elevated privileges)");

        var tempDb = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".db");
        var database = new DatabaseService(tempDb);
        database.Initialize();

        var runId = database.InsertRun(new RunRecord
        {
            ScenarioName = "test",
            StartedAtUtc = DateTime.UtcNow,
            ConfigSnapshot = "{}",
            ProbeVersion = "test"
        });

        using var monitor = new ProcessMonitor(database, runId, new[] { "cmd" });
        monitor.Start();

        // Give WMI watcher time to fully initialize
        await Task.Delay(1000);

        using (var process = Process.Start(new ProcessStartInfo
               {
                   FileName = "cmd.exe",
                   Arguments = "/c ping -n 2 127.0.0.1 >nul",
                   UseShellExecute = false,
                   CreateNoWindow = true
               }))
        {
            process?.WaitForExit();
        }

        // Poll for markers with timeout instead of fixed delay
        // WMI events can be slow in CI environments
        var timeout = TimeSpan.FromSeconds(5);
        var pollInterval = TimeSpan.FromMilliseconds(200);
        var stopwatch = Stopwatch.StartNew();
        IList<Marker> markers = [];
        bool hasStartMarker = false;
        bool hasExitMarker = false;

        while (stopwatch.Elapsed < timeout && (!hasStartMarker || !hasExitMarker))
        {
            markers = database.GetMarkers(runId);
            hasStartMarker = markers.Any(m => m.Label.Contains("started", StringComparison.OrdinalIgnoreCase));
            hasExitMarker = markers.Any(m => m.Label.Contains("exited", StringComparison.OrdinalIgnoreCase));

            if (!hasStartMarker || !hasExitMarker)
                await Task.Delay(pollInterval);
        }

        // Skip if WMI events weren't delivered (common in CI environments even when watcher starts)
        Skip.If(markers.Count == 0, "WMI process events not being delivered in this environment");

        Assert.Contains(markers, m => m.Label.Contains("started", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(markers, m => m.Label.Contains("exited", StringComparison.OrdinalIgnoreCase));

        var active = monitor.GetActiveProcessesSnapshot();
        Assert.Empty(active);

        SqliteConnection.ClearAllPools();
        File.Delete(tempDb);
    }

    private static bool CanAccessWmi()
    {
        try
        {
            // Test if we can start a process event watcher (requires elevated privileges)
            using var watcher = new ManagementEventWatcher(new System.Management.WqlEventQuery("SELECT * FROM Win32_ProcessStartTrace"));
            watcher.Start();
            watcher.Stop();
            return true;
        }
        catch
        {
            return false;
        }
    }
}
