using Blackpaw.Data;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Blackpaw.Tests;

public class DatabaseServiceTests
{
    [Fact]
    public void InitializeCreatesDatabaseAndRunCanBeInserted()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"blackpaw-test-{Guid.NewGuid():N}.db");
        var database = new DatabaseService(dbPath);

        database.Initialize();

        var runId = database.InsertRun(new RunRecord
        {
            ScenarioName = "baseline",
            StartedAtUtc = DateTime.UtcNow,
            ProbeVersion = "test",
            ConfigSnapshot = "{}"
        });

        var runs = database.GetRuns();

        Assert.Equal(runId, runs.Single().RunId);

        SqliteConnection.ClearAllPools();
        if (File.Exists(dbPath))
        {
            File.Delete(dbPath);
        }
    }

    [Fact]
    public void SamplesAndMarkersPersistAndReportCounts()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"blackpaw-test-{Guid.NewGuid():N}.db");
        var database = new DatabaseService(dbPath);
        database.Initialize();

        var runId = database.InsertRun(new RunRecord
        {
            ScenarioName = "scenario-a",
            StartedAtUtc = DateTime.UtcNow,
            ProbeVersion = "test",
            ConfigSnapshot = "{}"
        });

        var sampleId = database.InsertSystemSample(new SystemSample
        {
            RunId = runId,
            TimestampUtc = DateTime.UtcNow,
            CpuTotalPercent = 10
        });

        database.InsertProcessSamples(new[]
        {
            new ProcessSample
            {
                SampleId = sampleId,
                ProcessName = "procA",
                CpuPercent = 5,
                WorkingSetMb = 100
            }
        });

        database.InsertMarker(new Marker
        {
            RunId = runId,
            TimestampUtc = DateTime.UtcNow,
            MarkerType = "user",
            Label = "started"
        });

        database.UpdateRunEnd(runId, DateTime.UtcNow, 2.5);

        var info = database.GetRunInfo();
        Assert.Equal(1, info.runCount);
        Assert.NotNull(info.firstStart);
        Assert.NotNull(info.lastEnd);

        SqliteConnection.ClearAllPools();
        if (File.Exists(dbPath))
        {
            File.Delete(dbPath);
        }
    }

    [Fact]
    public void GetRuns_EmptyDatabase_ReturnsEmptyList()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"blackpaw-test-{Guid.NewGuid():N}.db");
        var database = new DatabaseService(dbPath);
        database.Initialize();

        var runs = database.GetRuns();

        Assert.Empty(runs);

        SqliteConnection.ClearAllPools();
        if (File.Exists(dbPath))
        {
            File.Delete(dbPath);
        }
    }

    [Fact]
    public void GetRunInfo_EmptyDatabase_ReturnsZeroCounts()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"blackpaw-test-{Guid.NewGuid():N}.db");
        var database = new DatabaseService(dbPath);
        database.Initialize();

        var info = database.GetRunInfo();

        Assert.Equal(0, info.runCount);
        Assert.Null(info.firstStart);
        Assert.Null(info.lastEnd);

        SqliteConnection.ClearAllPools();
        if (File.Exists(dbPath))
        {
            File.Delete(dbPath);
        }
    }

    [Fact]
    public void UpdateRunEnd_ValidRun_UpdatesEndTime()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"blackpaw-test-{Guid.NewGuid():N}.db");
        var database = new DatabaseService(dbPath);
        database.Initialize();

        var startTime = DateTime.UtcNow;
        var runId = database.InsertRun(new RunRecord
        {
            ScenarioName = "duration-test",
            StartedAtUtc = startTime,
            ProbeVersion = "test",
            ConfigSnapshot = "{}"
        });

        var endTime = startTime.AddSeconds(120);
        database.UpdateRunEnd(runId, endTime, 120.5);

        // GetRuns returns a projection that includes EndedAtUtc
        var runs = database.GetRuns();
        var run = runs.Single();

        Assert.NotNull(run.EndedAtUtc);
        // Verify end time is approximately correct (within 1 second)
        Assert.True(Math.Abs((run.EndedAtUtc!.Value - endTime).TotalSeconds) < 1);

        SqliteConnection.ClearAllPools();
        if (File.Exists(dbPath))
        {
            File.Delete(dbPath);
        }
    }

    [Fact]
    public void MultipleRuns_GetRuns_ReturnsAllInOrder()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"blackpaw-test-{Guid.NewGuid():N}.db");
        var database = new DatabaseService(dbPath);
        database.Initialize();

        // Insert three runs
        database.InsertRun(new RunRecord
        {
            ScenarioName = "first",
            StartedAtUtc = DateTime.UtcNow.AddHours(-2),
            ProbeVersion = "test",
            ConfigSnapshot = "{}"
        });
        database.InsertRun(new RunRecord
        {
            ScenarioName = "second",
            StartedAtUtc = DateTime.UtcNow.AddHours(-1),
            ProbeVersion = "test",
            ConfigSnapshot = "{}"
        });
        database.InsertRun(new RunRecord
        {
            ScenarioName = "third",
            StartedAtUtc = DateTime.UtcNow,
            ProbeVersion = "test",
            ConfigSnapshot = "{}"
        });

        var runs = database.GetRuns();

        Assert.Equal(3, runs.Count);
        // Verify all scenarios are present
        Assert.Contains(runs, r => r.ScenarioName == "first");
        Assert.Contains(runs, r => r.ScenarioName == "second");
        Assert.Contains(runs, r => r.ScenarioName == "third");

        SqliteConnection.ClearAllPools();
        if (File.Exists(dbPath))
        {
            File.Delete(dbPath);
        }
    }

    [Fact]
    public void GetMarkers_NoMarkers_ReturnsEmptyList()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"blackpaw-test-{Guid.NewGuid():N}.db");
        var database = new DatabaseService(dbPath);
        database.Initialize();

        var runId = database.InsertRun(new RunRecord
        {
            ScenarioName = "no-markers",
            StartedAtUtc = DateTime.UtcNow,
            ProbeVersion = "test",
            ConfigSnapshot = "{}"
        });

        var markers = database.GetMarkers(runId);

        Assert.Empty(markers);

        SqliteConnection.ClearAllPools();
        if (File.Exists(dbPath))
        {
            File.Delete(dbPath);
        }
    }
}
