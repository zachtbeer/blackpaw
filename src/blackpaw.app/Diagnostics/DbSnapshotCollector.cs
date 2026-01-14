using Microsoft.Data.SqlClient;
using Blackpaw.Data;

namespace Blackpaw.Diagnostics;

public class DbSnapshotCollector
{
    private readonly string _connectionString;

    public DbSnapshotCollector(string connectionString)
    {
        _connectionString = connectionString;
    }

    public async Task<DbSnapshot?> CaptureAsync(long runId, string label, CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            var counters = await LoadCountersAsync(connection, cancellationToken);
            var activeSessions = await GetActiveSessionCountAsync(connection, cancellationToken);

            var snapshot = new DbSnapshot
            {
                RunId = runId,
                TimestampUtc = DateTime.UtcNow,
                Label = label,
                RequestsPerSec = counters.GetValueOrDefault("Batch Requests/sec"),
                TransactionsPerSec = counters.GetValueOrDefault("Transactions/sec"),
                CompilationsPerSec = counters.GetValueOrDefault("SQL Compilations/sec"),
                RecompilationsPerSec = counters.GetValueOrDefault("SQL Re-Compilations/sec"),
                BufferCacheHitRatio = counters.GetValueOrDefault("Buffer cache hit ratio"),
                PageLifeExpectancySeconds = counters.GetValueOrDefault("Page life expectancy"),
                UserConnectionCount = (int?)counters.GetValueOrDefault("User Connections"),
                LogFlushesPerSec = counters.GetValueOrDefault("Log Flushes/sec"),
                LogBytesFlushedPerSec = counters.GetValueOrDefault("Log Bytes Flushed/sec"),
                ActiveSessionCount = activeSessions
            };

            return snapshot;
        }
        catch (Exception ex)
        {
            Logger.Warning($"Failed to capture DB snapshot '{label}'", ex);
            return null;
        }
    }

    private static async Task<Dictionary<string, double>> LoadCountersAsync(SqlConnection connection, CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.CommandText = @"
        SELECT counter_name, cntr_value
        FROM sys.dm_os_performance_counters
        WHERE counter_name IN (
            'Batch Requests/sec', 'Transactions/sec', 'SQL Compilations/sec', 'SQL Re-Compilations/sec',
            'User Connections', 'Log Flushes/sec', 'Log Bytes Flushed/sec', 'Page life expectancy', 'Buffer cache hit ratio')
        ;
        ";

        var counters = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var name = reader.GetString(0);
            var value = reader.GetDouble(1);
            if (counters.ContainsKey(name))
            {
                counters[name] += value;
            }
            else
            {
                counters[name] = value;
            }
        }

        return counters;
    }

    private static async Task<int?> GetActiveSessionCountAsync(SqlConnection connection, CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sys.dm_exec_sessions WHERE status = 'running';";
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is int count ? count : result is long l ? (int)l : null;
    }
}
