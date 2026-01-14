using System.Data;
using Microsoft.Data.SqlClient;
using Blackpaw.Configuration;
using Blackpaw.Data;
using Blackpaw.Diagnostics;

namespace Blackpaw.Monitoring;

public class SqlDmvSampler : IDisposable
{
    private readonly DatabaseService _database;
    private readonly long _runId;
    private readonly string _connectionString;
    private readonly double _intervalSeconds;
    private readonly CancellationTokenSource _cts = new();
    private Task? _loop;

    private long _prevReads;
    private long _prevReadMs;
    private long _prevReadBytes;
    private long _prevWrites;
    private long _prevWriteMs;
    private long _prevWriteBytes;
    private DateTime _prevTimestamp = DateTime.UtcNow;

    public SqlDmvSampler(DatabaseService database, long runId, string connectionString, double intervalSeconds)
    {
        _database = database;
        _runId = runId;
        _connectionString = connectionString;
        _intervalSeconds = intervalSeconds <= 0 ? 5 : intervalSeconds;
    }

    public void Start()
    {
        _loop = Task.Run(() => RunAsync(_cts.Token));
    }

    private async Task RunAsync(CancellationToken token)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(_intervalSeconds));
        while (await timer.WaitForNextTickAsync(token))
        {
            try
            {
                await CaptureAsync(token);
            }
            catch (Exception ex)
            {
                Logger.Warning("SQL DMV capture failed", ex);
            }
        }
    }

    private async Task CaptureAsync(CancellationToken token)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(token);

        var sample = new SqlDmvSample
        {
            RunId = _runId,
            TimestampUtc = DateTime.UtcNow,
            DatabaseName = "ALL"
        };

        sample.ActiveRequestsCount = await ExecuteScalarAsync<int?>(connection, "SELECT COUNT(*) FROM sys.dm_exec_requests WHERE session_id > 50 AND status IN ('running','runnable','suspended')", token);
        sample.BlockedRequestsCount = await ExecuteScalarAsync<int?>(connection, "SELECT COUNT(*) FROM sys.dm_exec_requests WHERE blocking_session_id <> 0", token);
        sample.UserConnections = await ExecuteScalarAsync<int?>(connection, "SELECT COUNT(*) FROM sys.dm_exec_sessions WHERE is_user_process = 1", token);
        sample.SessionsRunning = await ExecuteScalarAsync<int?>(connection, "SELECT COUNT(*) FROM sys.dm_exec_requests WHERE status = 'running' AND session_id > 50", token);

        using (var waitCmd = connection.CreateCommand())
        {
            waitCmd.CommandText = "SELECT wait_type, SUM(wait_duration_ms) AS WaitMs FROM sys.dm_os_waiting_tasks wt JOIN sys.dm_exec_sessions s ON wt.session_id = s.session_id WHERE s.is_user_process = 1 GROUP BY wait_type ORDER BY WaitMs DESC";
            await using var reader = await waitCmd.ExecuteReaderAsync(token);
            double? total = 0;
            string? topType = null;
            double? topMs = null;
            while (await reader.ReadAsync(token))
            {
                var waitType = reader.GetString(0);
                var waitMs = reader.IsDBNull(1) ? 0 : Convert.ToDouble(reader.GetInt64(1));
                total += waitMs;
                if (topType == null)
                {
                    topType = waitType;
                    topMs = waitMs;
                }
            }

            sample.TopWaitType = topType;
            sample.TopWaitTimeMs = topMs;
            sample.TotalWaitTimeMs = total;
        }

        using (var ioCmd = connection.CreateCommand())
        {
            ioCmd.CommandText = "SELECT SUM(num_of_reads), SUM(io_stall_read_ms), SUM(num_of_bytes_read), SUM(num_of_writes), SUM(io_stall_write_ms), SUM(num_of_bytes_written) FROM sys.dm_io_virtual_file_stats(NULL, NULL)";
            await using var reader = await ioCmd.ExecuteReaderAsync(token);
            if (await reader.ReadAsync(token))
            {
                var reads = reader.IsDBNull(0) ? 0 : reader.GetInt64(0);
                var readMs = reader.IsDBNull(1) ? 0 : reader.GetInt64(1);
                var readBytes = reader.IsDBNull(2) ? 0 : reader.GetInt64(2);
                var writes = reader.IsDBNull(3) ? 0 : reader.GetInt64(3);
                var writeMs = reader.IsDBNull(4) ? 0 : reader.GetInt64(4);
                var writeBytes = reader.IsDBNull(5) ? 0 : reader.GetInt64(5);

                var elapsed = Math.Max((DateTime.UtcNow - _prevTimestamp).TotalSeconds, 1);
                if (_prevTimestamp == default)
                {
                    elapsed = _intervalSeconds;
                }

                var deltaReads = Math.Max(reads - _prevReads, 0);
                var deltaReadMs = Math.Max(readMs - _prevReadMs, 0);
                var deltaReadBytes = Math.Max(readBytes - _prevReadBytes, 0);

                var deltaWrites = Math.Max(writes - _prevWrites, 0);
                var deltaWriteMs = Math.Max(writeMs - _prevWriteMs, 0);
                var deltaWriteBytes = Math.Max(writeBytes - _prevWriteBytes, 0);

                sample.ReadStallMsPerRead = deltaReads == 0 ? 0 : deltaReadMs / (double)deltaReads;
                sample.WriteStallMsPerWrite = deltaWrites == 0 ? 0 : deltaWriteMs / (double)deltaWrites;
                sample.ReadBytesPerSec = deltaReadBytes / elapsed;
                sample.WriteBytesPerSec = deltaWriteBytes / elapsed;

                _prevReads = reads;
                _prevReadMs = readMs;
                _prevReadBytes = readBytes;
                _prevWrites = writes;
                _prevWriteMs = writeMs;
                _prevWriteBytes = writeBytes;
                _prevTimestamp = DateTime.UtcNow;
            }
        }

        _database.InsertSqlDmvSample(sample);
    }

    private static async Task<T?> ExecuteScalarAsync<T>(SqlConnection connection, string commandText, CancellationToken token)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = commandText;
        var result = await command.ExecuteScalarAsync(token);
        if (result == null || result is DBNull)
        {
            return default;
        }

        // Handle nullable types by converting to the underlying type
        var targetType = Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);
        return (T?)Convert.ChangeType(result, targetType);
    }

    public void Dispose()
    {
        _cts.Cancel();
        try
        {
            _loop?.Wait(1000);
        }
        catch (Exception ex)
        {
            Logger.Debug($"SQL DMV loop wait failed during dispose: {ex.Message}");
        }
    }
}
