using Microsoft.EntityFrameworkCore;

namespace Blackpaw.Data;

public class DatabaseService
{
    private readonly string _dbPath;

    public DatabaseService(string dbPath)
    {
        // Validate and normalize path to prevent directory traversal
        if (string.IsNullOrWhiteSpace(dbPath))
        {
            throw new ArgumentException("Database path cannot be empty", nameof(dbPath));
        }

        // Get full path to resolve any relative path components
        var fullPath = Path.GetFullPath(dbPath);

        // Validate the path doesn't contain invalid characters
        if (fullPath.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
        {
            throw new ArgumentException("Database path contains invalid characters", nameof(dbPath));
        }

        _dbPath = fullPath;

        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }

    public void Initialize()
    {
        using var context = CreateContext();
        context.Database.EnsureCreated();

        // Ensure SchemaInfo table exists (for databases created before this feature)
        try
        {
            context.Database.ExecuteSqlRaw(@"
                CREATE TABLE IF NOT EXISTS SchemaInfo (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    SchemaVersion INTEGER NOT NULL,
                    CreatedAtUtc TEXT NOT NULL
                )");

            // Seed schema version if not present
            if (!context.SchemaInfo.Any())
            {
                context.SchemaInfo.Add(new SchemaInfo
                {
                    SchemaVersion = 1,
                    CreatedAtUtc = DateTime.UtcNow
                });
                context.SaveChanges();
            }
        }
        catch
        {
            // Ignore if table operations fail - not critical for operation
        }
    }

    public int GetSchemaVersion()
    {
        using var context = CreateContext();
        try
        {
            return context.SchemaInfo.Max(s => (int?)s.SchemaVersion) ?? 0;
        }
        catch
        {
            return 0; // Table doesn't exist (legacy DB)
        }
    }

    public long InsertRun(RunRecord run)
    {
        using var context = CreateContext();
        context.Runs.Add(run);
        context.SaveChanges();
        return run.RunId;
    }

    public void UpdateRunEnd(long runId, DateTime endedAtUtc, double durationSeconds)
    {
        using var context = CreateContext();
        var run = context.Runs.SingleOrDefault(r => r.RunId == runId);
        if (run == null)
        {
            return;
        }

        run.EndedAtUtc = endedAtUtc;
        run.DurationSeconds = durationSeconds;
        context.SaveChanges();
    }

    public long InsertSystemSample(SystemSample sample)
    {
        using var context = CreateContext();
        context.SystemSamples.Add(sample);
        context.SaveChanges();
        return sample.SampleId;
    }

    public void InsertProcessSamples(IEnumerable<ProcessSample> samples)
    {
        using var context = CreateContext();
        context.ProcessSamples.AddRange(samples);
        context.SaveChanges();
    }

    public void InsertDbSnapshot(DbSnapshot snapshot)
    {
        using var context = CreateContext();
        context.DbSnapshots.Add(snapshot);
        context.SaveChanges();
    }

    public void InsertMarker(Marker marker)
    {
        using var context = CreateContext();
        context.Markers.Add(marker);
        context.SaveChanges();
    }

    public void InsertDotNetRuntimeSamples(IEnumerable<DotNetRuntimeSample> samples)
    {
        using var context = CreateContext();
        context.DotNetRuntimeSamples.AddRange(samples);
        context.SaveChanges();
    }

    public void InsertSqlDmvSample(SqlDmvSample sample)
    {
        using var context = CreateContext();
        context.SqlDmvSamples.Add(sample);
        context.SaveChanges();
    }

    public void InsertDotNetHttpSamples(IEnumerable<DotNetHttpSample> samples)
    {
        using var context = CreateContext();
        context.DotNetHttpSamples.AddRange(samples);
        context.SaveChanges();
    }

    public List<Marker> GetMarkers(long runId)
    {
        using var context = CreateContext();
        return context.Markers
            .Where(m => m.RunId == runId)
            .OrderBy(m => m.TimestampUtc)
            .ToList();
    }

    public List<RunRecord> GetRuns()
    {
        using var context = CreateContext();
        return context.Runs
            .OrderByDescending(r => r.RunId)
            .Select(r => new RunRecord
            {
                RunId = r.RunId,
                ScenarioName = r.ScenarioName,
                StartedAtUtc = r.StartedAtUtc,
                EndedAtUtc = r.EndedAtUtc,
                Notes = r.Notes
            })
            .ToList();
    }

    public (int runCount, DateTime? firstStart, DateTime? lastEnd) GetRunInfo()
    {
        using var context = CreateContext();
        var runCount = context.Runs.Count();
        var firstStart = context.Runs.Min(r => (DateTime?)r.StartedAtUtc);
        var lastEnd = context.Runs.Max(r => r.EndedAtUtc);
        return (runCount, firstStart, lastEnd);
    }

    // Report query methods
    public RunRecord? GetRunById(long runId)
    {
        using var context = CreateContext();
        return context.Runs.SingleOrDefault(r => r.RunId == runId);
    }

    public List<SystemSample> GetSystemSamples(long runId)
    {
        using var context = CreateContext();
        return context.SystemSamples
            .Where(s => s.RunId == runId)
            .OrderBy(s => s.TimestampUtc)
            .ToList();
    }

    public List<ProcessSample> GetProcessSamplesForRun(long runId)
    {
        using var context = CreateContext();
        var sampleIds = context.SystemSamples
            .Where(s => s.RunId == runId)
            .Select(s => s.SampleId)
            .ToList();

        return context.ProcessSamples
            .Where(p => sampleIds.Contains(p.SampleId))
            .ToList();
    }

    public List<DotNetRuntimeSample> GetDotNetRuntimeSamples(long runId)
    {
        using var context = CreateContext();
        return context.DotNetRuntimeSamples
            .Where(s => s.RunId == runId)
            .OrderBy(s => s.TimestampUtc)
            .ToList();
    }

    public List<DotNetHttpSample> GetDotNetHttpSamples(long runId)
    {
        using var context = CreateContext();
        return context.DotNetHttpSamples
            .Where(s => s.RunId == runId)
            .OrderBy(s => s.TimestampUtc)
            .ToList();
    }

    public List<SqlDmvSample> GetSqlDmvSamples(long runId)
    {
        using var context = CreateContext();
        return context.SqlDmvSamples
            .Where(s => s.RunId == runId)
            .OrderBy(s => s.TimestampUtc)
            .ToList();
    }

    public List<DbSnapshot> GetDbSnapshots(long runId)
    {
        using var context = CreateContext();
        return context.DbSnapshots
            .Where(s => s.RunId == runId)
            .OrderBy(s => s.TimestampUtc)
            .ToList();
    }

    private BlackpawContext CreateContext()
    {
        var context = new BlackpawContext(_dbPath);
        context.ChangeTracker.AutoDetectChangesEnabled = true;
        return context;
    }
}
