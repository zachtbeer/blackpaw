using Blackpaw.Data;
using Blackpaw.Sampling;
using Microsoft.Data.Sqlite;
using System.Diagnostics;
using System.Management;
using Xunit;

namespace Blackpaw.Tests;

public class ProcessMonitorTests
{
    [Fact]
    public async Task ProcessMonitor_CapturesStartAndStopMarkers()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        // Skip test if WMI access is not available (requires elevated privileges)
        if (!CanAccessWmi())
        {
            return;
        }

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

        await Task.Delay(500);

        using (var process = Process.Start(new ProcessStartInfo
               {
                   FileName = "cmd.exe",
                   Arguments = "/c timeout /t 1 >nul",
                   UseShellExecute = false,
                   CreateNoWindow = true
               }))
        {
            process?.WaitForExit();
        }

        await Task.Delay(1500);

        var markers = database.GetMarkers(runId);
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
