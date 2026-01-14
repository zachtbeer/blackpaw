using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Data.Sqlite;

// Parse arguments
var duration = args.Length > 0 ? int.Parse(args[0]) : 10;
using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(duration));

// Start minimal HTTP server on random port
var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls("http://127.0.0.1:0");
builder.Logging.ClearProviders(); // Suppress logging noise
var app = builder.Build();

app.MapGet("/api/test", () => Results.Ok(new { timestamp = DateTime.UtcNow, message = "ok" }));
app.MapGet("/api/slow", async () =>
{
    await Task.Delay(100);
    return Results.Ok("slow response");
});
app.MapPost("/api/data", () => Results.Created("/api/data/1", new { id = 1 }));
app.MapGet("/api/error", () => Results.StatusCode(500));

await app.StartAsync();

// Extract port from server address and signal to test harness
var address = app.Urls.First();
var port = address.Split(':').Last();
Console.WriteLine($"PORT:{port}");
Console.Out.Flush();

// SQLite setup
var dbPath = Path.Combine(Path.GetTempPath(), $"testapp-core-{Guid.NewGuid():N}.db");
await using var conn = new SqliteConnection($"Data Source={dbPath}");
await conn.OpenAsync();
await using (var createCmd = conn.CreateCommand())
{
    createCmd.CommandText = "CREATE TABLE items (id INTEGER PRIMARY KEY AUTOINCREMENT, data TEXT, created_at TEXT)";
    await createCmd.ExecuteNonQueryAsync();
}

// Workload loop
using var http = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}") };
var rand = new Random();
var iteration = 0;

try
{
    while (!cts.Token.IsCancellationRequested)
    {
        iteration++;

        // HTTP requests (generates HTTP monitoring events)
        try
        {
            await http.GetAsync("/api/test", cts.Token);
            await http.GetAsync("/api/slow", cts.Token);
            await http.PostAsync("/api/data", new StringContent("{}"), cts.Token);

            // Occasionally hit error endpoint
            if (iteration % 10 == 0)
            {
                await http.GetAsync("/api/error", cts.Token);
            }
        }
        catch (OperationCanceledException) { break; }
        catch { /* Ignore HTTP errors */ }

        // SQL operations
        await using (var insertCmd = conn.CreateCommand())
        {
            insertCmd.CommandText = $"INSERT INTO items (data, created_at) VALUES ('{Guid.NewGuid()}', '{DateTime.UtcNow:O}')";
            await insertCmd.ExecuteNonQueryAsync();
        }

        await using (var selectCmd = conn.CreateCommand())
        {
            selectCmd.CommandText = "SELECT COUNT(*) FROM items";
            await selectCmd.ExecuteScalarAsync();
        }

        // GC pressure - allocate random sized arrays
        var garbage = new byte[rand.Next(1024, 1024 * 100)];
        GC.KeepAlive(garbage);

        // Generate exceptions (caught, for EventPipe to track)
        try
        {
            throw new InvalidOperationException($"Test exception {iteration}");
        }
        catch
        {
            // Expected - we're testing exception tracking
        }

        await Task.Delay(50, cts.Token);
    }
}
catch (OperationCanceledException)
{
    // Normal shutdown
}

// Cleanup
await app.StopAsync();
conn.Close();
SqliteConnection.ClearAllPools();

try { File.Delete(dbPath); }
catch { /* Best effort cleanup */ }

Console.WriteLine("DONE");
