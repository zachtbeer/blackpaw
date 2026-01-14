using Microsoft.Data.SqlClient;

namespace Blackpaw.Tests.Integration;

/// <summary>
/// Utility class to generate SQL Server load for integration testing.
/// Provides methods to create connections, generate I/O activity, and simulate long-running queries.
/// </summary>
public static class SqlServerLoadGenerator
{
    /// <summary>
    /// Creates multiple concurrent connections to the database.
    /// </summary>
    /// <param name="connectionString">The SQL Server connection string.</param>
    /// <param name="count">Number of connections to create.</param>
    /// <returns>List of open connections. Caller is responsible for disposing.</returns>
    public static async Task<List<SqlConnection>> CreateConnectionsAsync(string connectionString, int count)
    {
        var connections = new List<SqlConnection>();
        for (int i = 0; i < count; i++)
        {
            var conn = new SqlConnection(connectionString);
            await conn.OpenAsync();
            connections.Add(conn);
        }
        return connections;
    }

    /// <summary>
    /// Disposes a list of SQL connections.
    /// </summary>
    public static async Task DisposeConnectionsAsync(IEnumerable<SqlConnection> connections)
    {
        foreach (var conn in connections)
        {
            await conn.CloseAsync();
            await conn.DisposeAsync();
        }
    }

    /// <summary>
    /// Executes a WAITFOR DELAY query to simulate long-running work.
    /// </summary>
    /// <param name="connectionString">The SQL Server connection string.</param>
    /// <param name="delay">The delay duration.</param>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    public static async Task ExecuteDelayAsync(
        string connectionString,
        TimeSpan delay,
        CancellationToken cancellationToken = default)
    {
        await using var conn = new SqlConnection(connectionString);
        await conn.OpenAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"WAITFOR DELAY '{delay:hh\\:mm\\:ss}'";
        cmd.CommandTimeout = (int)delay.TotalSeconds + 10;
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>
    /// Generates I/O activity by creating and querying temp tables.
    /// This produces measurable disk read/write activity for DMV testing.
    /// </summary>
    /// <param name="connectionString">The SQL Server connection string.</param>
    /// <param name="rowCount">Number of rows to insert (more rows = more I/O).</param>
    public static async Task GenerateIoActivityAsync(string connectionString, int rowCount = 10000)
    {
        await using var conn = new SqlConnection(connectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();

        // Create temp table, insert data with large strings, query it, then drop
        cmd.CommandText = $@"
            CREATE TABLE #LoadTest (Id INT, Data NVARCHAR(MAX));
            INSERT INTO #LoadTest
            SELECT TOP {rowCount}
                ROW_NUMBER() OVER (ORDER BY (SELECT NULL)),
                REPLICATE('X', 1000)
            FROM sys.objects a CROSS JOIN sys.objects b;
            SELECT SUM(LEN(Data)) FROM #LoadTest;
            DROP TABLE #LoadTest;";
        cmd.CommandTimeout = 60;
        await cmd.ExecuteScalarAsync();
    }

    /// <summary>
    /// Executes a simple query to verify connectivity and generate minimal activity.
    /// </summary>
    public static async Task<int> ExecuteSimpleQueryAsync(string connectionString)
    {
        await using var conn = new SqlConnection(connectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT 1";
        var result = await cmd.ExecuteScalarAsync();
        return Convert.ToInt32(result);
    }
}
