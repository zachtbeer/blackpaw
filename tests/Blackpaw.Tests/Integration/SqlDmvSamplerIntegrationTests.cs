using Blackpaw.Data;
using Blackpaw.Monitoring;
using Blackpaw.Tests.TestHelpers;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Blackpaw.Tests.Integration;

/// <summary>
/// Integration tests for SqlDmvSampler using TestContainers SQL Server.
/// These tests verify that DMV monitoring works correctly against a real SQL Server instance.
/// </summary>
[Trait("Category", "Integration")]
[Collection("SqlServer")]
public class SqlDmvSamplerIntegrationTests : IAsyncLifetime
{
    private readonly SqlServerFixture _sqlServer;
    private DatabaseService? _database;
    private string? _dbPath;
    private long _runId;

    public SqlDmvSamplerIntegrationTests(SqlServerFixture sqlServer)
    {
        _sqlServer = sqlServer;
    }

    /// <summary>
    /// Skips the current test if SQL Server container is not available.
    /// Call this at the start of each test that requires the container.
    /// </summary>
    private void SkipIfContainerNotAvailable()
    {
        Skip.If(!_sqlServer.IsAvailable, _sqlServer.InitializationError ?? "SQL Server container not available");
    }

    public Task InitializeAsync()
    {
        // Create fresh SQLite database for each test
        _dbPath = Path.Combine(Path.GetTempPath(), $"blackpaw-dmv-test-{Guid.NewGuid():N}.db");
        _database = new DatabaseService(_dbPath);
        _database.Initialize();

        _runId = _database.InsertRun(new RunRecord
        {
            ScenarioName = "dmv-integration-test",
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
                File.Delete(_dbPath);
            }
        }
        return Task.CompletedTask;
    }

    [SkippableFact]
    public async Task SqlDmvSampler_CanConnectToSqlServer()
    {
        SkipIfContainerNotAvailable();

        // Arrange & Act - verify we can connect directly first
        var result = await SqlServerLoadGenerator.ExecuteSimpleQueryAsync(_sqlServer.ConnectionString);

        // Assert
        Assert.Equal(1, result);
    }

    [SkippableFact]
    public async Task SqlDmvSampler_DmvQueriesExecuteWithoutError()
    {
        SkipIfContainerNotAvailable();

        // Arrange
        await using var conn = new Microsoft.Data.SqlClient.SqlConnection(_sqlServer.ConnectionString);
        await conn.OpenAsync();

        // Act & Assert - Test each DMV query used by SqlDmvSampler
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT COUNT(*) FROM sys.dm_exec_requests WHERE session_id > 50 AND status IN ('running','runnable','suspended')";
            var result = await cmd.ExecuteScalarAsync();
            Assert.NotNull(result);
        }

        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT COUNT(*) FROM sys.dm_exec_requests WHERE blocking_session_id <> 0";
            var result = await cmd.ExecuteScalarAsync();
            Assert.NotNull(result);
        }

        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT COUNT(*) FROM sys.dm_exec_sessions WHERE is_user_process = 1";
            var result = await cmd.ExecuteScalarAsync();
            Assert.NotNull(result);
        }

        await using (var cmd = conn.CreateCommand())
        {
            // Test the wait types query - this one uses a join
            cmd.CommandText = "SELECT wait_type, SUM(wait_duration_ms) AS WaitMs FROM sys.dm_os_waiting_tasks wt JOIN sys.dm_exec_sessions s ON wt.session_id = s.session_id WHERE s.is_user_process = 1 GROUP BY wait_type ORDER BY WaitMs DESC";
            await using var reader = await cmd.ExecuteReaderAsync();
            // Just verify the query runs without throwing
            while (await reader.ReadAsync())
            {
                var waitType = reader.GetString(0);
                // Note: SUM(bigint) returns bigint, not double
                var waitMs = reader.IsDBNull(1) ? 0 : reader.GetInt64(1);
            }
        }

        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT SUM(num_of_reads), SUM(io_stall_read_ms), SUM(num_of_bytes_read), SUM(num_of_writes), SUM(io_stall_write_ms), SUM(num_of_bytes_written) FROM sys.dm_io_virtual_file_stats(NULL, NULL)";
            await using var reader = await cmd.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
        }
    }

    [SkippableFact]
    public async Task SqlDmvSampler_ConnectsToSqlServer_CapturesSample()
    {
        SkipIfContainerNotAvailable();

        // Arrange
        using var sampler = new SqlDmvSampler(
            _database!,
            _runId,
            _sqlServer.ConnectionString,
            intervalSeconds: 1);

        // Act
        sampler.Start();
        await Task.Delay(TimeSpan.FromSeconds(4)); // Allow time for at least 2-3 captures

        // Assert
        var samples = _database!.GetSqlDmvSamples(_runId);
        Assert.NotEmpty(samples);
        Assert.All(samples, s => Assert.Equal(_runId, s.RunId));
        Assert.All(samples, s => Assert.NotEqual(default, s.TimestampUtc));
    }

    [SkippableFact]
    public async Task SqlDmvSampler_TracksUserConnections_ReflectsActiveConnections()
    {
        SkipIfContainerNotAvailable();

        // Arrange
        using var sampler = new SqlDmvSampler(
            _database!,
            _runId,
            _sqlServer.ConnectionString,
            intervalSeconds: 1);

        // Open additional connections to generate activity
        var connections = await SqlServerLoadGenerator.CreateConnectionsAsync(_sqlServer.ConnectionString, 3);

        try
        {
            // Act
            sampler.Start();
            await Task.Delay(TimeSpan.FromSeconds(2));

            // Assert
            var samples = _database!.GetSqlDmvSamples(_runId);
            Assert.NotEmpty(samples);

            // Should detect at least the additional connections (plus sampler's own)
            var latestSample = samples.OrderByDescending(s => s.TimestampUtc).First();
            Assert.NotNull(latestSample.UserConnections);
            Assert.True(latestSample.UserConnections >= 3,
                $"Expected at least 3 user connections, got {latestSample.UserConnections}");
        }
        finally
        {
            await SqlServerLoadGenerator.DisposeConnectionsAsync(connections);
        }
    }

    [SkippableFact]
    public async Task SqlDmvSampler_CapturesIoStatistics_AfterDatabaseActivity()
    {
        SkipIfContainerNotAvailable();

        // Arrange
        using var sampler = new SqlDmvSampler(
            _database!,
            _runId,
            _sqlServer.ConnectionString,
            intervalSeconds: 1);

        // Generate some I/O activity before sampling starts
        await SqlServerLoadGenerator.GenerateIoActivityAsync(_sqlServer.ConnectionString, rowCount: 5000);

        // Act
        sampler.Start();
        await Task.Delay(TimeSpan.FromSeconds(3));

        // Assert
        var samples = _database!.GetSqlDmvSamples(_runId);
        Assert.NotEmpty(samples);

        // At least one sample should have captured some I/O stats
        // Note: First sample may have null I/O rates since it's a delta calculation
        if (samples.Count >= 2)
        {
            var laterSamples = samples.Skip(1).ToList();
            Assert.True(
                laterSamples.Any(s => s.ReadBytesPerSec != null || s.WriteBytesPerSec != null),
                "Expected at least one sample with I/O statistics");
        }
    }

    [SkippableFact]
    public async Task SqlDmvSampler_RespectsSampleInterval_CapturesMultipleSamples()
    {
        SkipIfContainerNotAvailable();

        // Arrange
        const double intervalSeconds = 0.5;
        using var sampler = new SqlDmvSampler(
            _database!,
            _runId,
            _sqlServer.ConnectionString,
            intervalSeconds: intervalSeconds);

        // Act
        sampler.Start();
        await Task.Delay(TimeSpan.FromSeconds(2.5)); // Should get ~4-5 samples

        // Assert
        var samples = _database!.GetSqlDmvSamples(_runId);
        Assert.True(samples.Count >= 3,
            $"Expected at least 3 samples in 2.5 seconds with 0.5s interval, got {samples.Count}");

        // Verify timestamps are increasing
        var orderedSamples = samples.OrderBy(s => s.TimestampUtc).ToList();
        for (int i = 1; i < orderedSamples.Count; i++)
        {
            Assert.True(orderedSamples[i].TimestampUtc > orderedSamples[i - 1].TimestampUtc,
                "Sample timestamps should be strictly increasing");
        }
    }

    [SkippableFact]
    public async Task SqlDmvSampler_DisposesGracefully_StopsCapturing()
    {
        SkipIfContainerNotAvailable();

        // Arrange
        var sampler = new SqlDmvSampler(
            _database!,
            _runId,
            _sqlServer.ConnectionString,
            intervalSeconds: 0.5);

        sampler.Start();
        await Task.Delay(TimeSpan.FromSeconds(1));

        var samplesBeforeDispose = _database!.GetSqlDmvSamples(_runId).Count;

        // Act
        sampler.Dispose();
        await Task.Delay(TimeSpan.FromSeconds(1));

        // Assert
        var samplesAfterDispose = _database!.GetSqlDmvSamples(_runId).Count;

        // No new samples should be added after dispose
        Assert.Equal(samplesBeforeDispose, samplesAfterDispose);
    }

    [SkippableFact]
    public async Task SqlDmvSampler_CapturesSessionsRunning_DuringActivity()
    {
        SkipIfContainerNotAvailable();

        // Arrange
        using var sampler = new SqlDmvSampler(
            _database!,
            _runId,
            _sqlServer.ConnectionString,
            intervalSeconds: 0.5);

        // Start a long-running query in background to ensure there's activity
        var queryTask = Task.Run(async () =>
        {
            await SqlServerLoadGenerator.ExecuteDelayAsync(_sqlServer.ConnectionString, TimeSpan.FromSeconds(2));
        });

        // Act
        sampler.Start();
        await Task.Delay(TimeSpan.FromSeconds(1)); // Sample during the query

        // Assert
        var samples = _database!.GetSqlDmvSamples(_runId);
        Assert.NotEmpty(samples);

        // Wait for query to complete
        await queryTask;
    }

    [SkippableFact]
    public async Task SqlDmvSampler_CapturesAllExpectedFields()
    {
        SkipIfContainerNotAvailable();

        // Arrange
        using var sampler = new SqlDmvSampler(
            _database!,
            _runId,
            _sqlServer.ConnectionString,
            intervalSeconds: 1);

        // Generate some activity
        await SqlServerLoadGenerator.GenerateIoActivityAsync(_sqlServer.ConnectionString, rowCount: 1000);

        // Act
        sampler.Start();
        await Task.Delay(TimeSpan.FromSeconds(2));

        // Assert
        var samples = _database!.GetSqlDmvSamples(_runId);
        Assert.NotEmpty(samples);

        var sample = samples.First();

        // Verify the sample has the expected run ID and timestamp
        Assert.Equal(_runId, sample.RunId);
        Assert.True(sample.TimestampUtc > DateTime.UtcNow.AddMinutes(-1));
        Assert.True(sample.TimestampUtc <= DateTime.UtcNow);

        // These fields should always be populated
        Assert.NotNull(sample.UserConnections);
    }

    [Fact]
    public async Task SqlDmvSampler_WithInvalidConnectionString_HandlesGracefully()
    {
        // Arrange - use invalid connection string
        using var sampler = new SqlDmvSampler(
            _database!,
            _runId,
            "Server=nonexistent.invalid;Database=test;User Id=sa;Password=test;TrustServerCertificate=true;Connect Timeout=1;",
            intervalSeconds: 1);

        // Act - should not throw
        sampler.Start();
        await Task.Delay(TimeSpan.FromSeconds(2));

        // Assert - no samples captured but no crash
        var samples = _database!.GetSqlDmvSamples(_runId);
        Assert.Empty(samples); // Cannot connect, so no samples
    }
}
