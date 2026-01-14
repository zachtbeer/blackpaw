using Blackpaw.Configuration;
using Blackpaw.Data;
using Blackpaw.Monitoring;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Blackpaw.Tests.EndToEnd;

/// <summary>
/// End-to-end tests for .NET Framework runtime monitoring.
/// These tests launch a real .NET Framework 4.8 application and verify that
/// Blackpaw can capture Performance Counter metrics from it.
/// </summary>
[Trait("Category", "EndToEnd")]
public class NetFrameworkAppMonitoringTests : IAsyncLifetime
{
    private DatabaseService? _database;
    private string? _dbPath;
    private long _runId;

    public Task InitializeAsync()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"blackpaw-e2e-fw-{Guid.NewGuid():N}.db");
        _database = new DatabaseService(_dbPath);
        _database.Initialize();

        _runId = _database.InsertRun(new RunRecord
        {
            ScenarioName = "e2e-framework-test",
            StartedAtUtc = DateTime.UtcNow,
            ProbeVersion = "test",
            ConfigSnapshot = "{}"
        });

        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        if (_dbPath != null)
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(_dbPath))
            {
                try { File.Delete(_dbPath); }
                catch { /* Best effort */ }
            }
        }
        return Task.CompletedTask;
    }

    [Fact(Skip = "Performance Counter access requires elevated privileges or specific environment setup")]
    public async Task DotNetFrameworkMonitor_CapturesRuntimeMetrics()
    {
        // Arrange - Start the .NET Core app first (it provides the HTTP server)
        await using var coreApp = await TestAppLauncher.StartNetCoreAppAsync(durationSeconds: 15);

        Assert.True(coreApp.IsRunning, "Core test app should be running");
        Assert.True(coreApp.Port.HasValue, "Core test app should have reported its port");

        // Start the .NET Framework app connecting to the Core app's server
        await using var fwApp = await TestAppLauncher.StartNetFrameworkAppAsync(
            $"http://127.0.0.1:{coreApp.Port}", durationSeconds: 12);

        Assert.True(fwApp.IsRunning, "Framework test app should be running");

        var apps = new List<DotNetAppConfig>
        {
            new()
            {
                Name = "FrameworkTestApp",
                ProcessName = "Blackpaw.TestApp.NetFramework",
                Enabled = true
            }
        };

        using var monitor = new DotNetFrameworkRuntimeMonitor(_database!, _runId, apps, intervalSeconds: 1);

        // Act - Start monitoring and collect samples
        monitor.Start();
        await Task.Delay(TimeSpan.FromSeconds(8));

        // Assert
        var samples = _database!.GetDotNetRuntimeSamples(_runId);

        Assert.NotEmpty(samples);
        Assert.All(samples, s => Assert.Equal("Framework", s.RuntimeKind));
        Assert.All(samples, s => Assert.Equal(_runId, s.RunId));

        // Verify we captured some GC/heap data
        Assert.Contains(samples, s => s.HeapSizeMb > 0);
    }

    [Fact(Skip = "Depends on EventPipe (.NET Core) and Performance Counters (.NET Framework) - both have environment issues")]
    public async Task CoreAndFrameworkApps_CanBeMonitoredSimultaneously()
    {
        // Arrange - Start both apps
        await using var coreApp = await TestAppLauncher.StartNetCoreAppAsync(durationSeconds: 15);
        Assert.True(coreApp.Port.HasValue, "Core test app should have reported its port");

        await using var fwApp = await TestAppLauncher.StartNetFrameworkAppAsync(
            $"http://127.0.0.1:{coreApp.Port}", durationSeconds: 12);

        var coreApps = new List<DotNetAppConfig>
        {
            new()
            {
                Name = "CoreTestApp",
                ProcessName = "Blackpaw.TestApp.NetCore",
                Enabled = true
            }
        };

        var fwApps = new List<DotNetAppConfig>
        {
            new()
            {
                Name = "FrameworkTestApp",
                ProcessName = "Blackpaw.TestApp.NetFramework",
                Enabled = true
            }
        };

        using var coreMonitor = new DotNetCoreRuntimeMonitor(_database!, _runId, coreApps, intervalSeconds: 1);
        using var fwMonitor = new DotNetFrameworkRuntimeMonitor(_database!, _runId, fwApps, intervalSeconds: 1);

        // Act - Attach/start both monitors
        coreMonitor.AttachToExisting();
        fwMonitor.Start();
        await Task.Delay(TimeSpan.FromSeconds(8));

        // Assert - Both should have captured data
        var samples = _database!.GetDotNetRuntimeSamples(_runId);

        var coreSamples = samples.Where(s => s.RuntimeKind == "Core").ToList();
        var fwSamples = samples.Where(s => s.RuntimeKind == "Framework").ToList();

        Assert.NotEmpty(coreSamples);
        Assert.NotEmpty(fwSamples);

        // Both should show GC activity
        Assert.Contains(coreSamples, s => s.HeapSizeMb > 0);
        Assert.Contains(fwSamples, s => s.HeapSizeMb > 0);
    }

    [Fact]
    public async Task FrameworkMonitor_HandlesProcessNotFound_Gracefully()
    {
        // Arrange - Configure monitoring for a process that doesn't exist
        var apps = new List<DotNetAppConfig>
        {
            new()
            {
                Name = "NonExistentApp",
                ProcessName = "NonExistentProcess12345",
                Enabled = true
            }
        };

        using var monitor = new DotNetFrameworkRuntimeMonitor(_database!, _runId, apps, intervalSeconds: 1);

        // Act - Start monitoring (should not throw)
        monitor.Start();
        await Task.Delay(TimeSpan.FromSeconds(2));

        // Assert - No samples captured, but no exceptions
        var samples = _database!.GetDotNetRuntimeSamples(_runId);
        Assert.Empty(samples);
    }

    [Fact(Skip = "Performance Counter access requires elevated privileges or specific environment setup")]
    public async Task FrameworkMonitor_CapturesThreadPoolData()
    {
        // Arrange
        await using var coreApp = await TestAppLauncher.StartNetCoreAppAsync(durationSeconds: 15);
        await using var fwApp = await TestAppLauncher.StartNetFrameworkAppAsync(
            $"http://127.0.0.1:{coreApp.Port}", durationSeconds: 12);

        var apps = new List<DotNetAppConfig>
        {
            new()
            {
                Name = "FrameworkTestApp",
                ProcessName = "Blackpaw.TestApp.NetFramework",
                Enabled = true
            }
        };

        using var monitor = new DotNetFrameworkRuntimeMonitor(_database!, _runId, apps, intervalSeconds: 1);

        // Act
        monitor.Start();
        await Task.Delay(TimeSpan.FromSeconds(6));

        // Assert - Should have thread data
        var samples = _database!.GetDotNetRuntimeSamples(_runId);
        Assert.NotEmpty(samples);

        // Framework monitor captures thread count via Performance Counters
        Assert.Contains(samples, s => s.ThreadPoolThreadCount > 0 || s.HeapSizeMb > 0);
    }
}
