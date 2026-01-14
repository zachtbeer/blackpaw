using System;
using System.Data.SQLite;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Blackpaw.TestApp.NetFramework
{
    class Program
    {
        static async Task Main(string[] args)
        {
            // Parse arguments: serverUrl, duration
            var serverUrl = args.Length > 0 ? args[0] : "http://127.0.0.1:5000";
            var duration = args.Length > 1 ? int.Parse(args[1]) : 10;

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(duration));

            Console.WriteLine("STARTED:framework");
            Console.Out.Flush();

            // SQLite setup
            var dbPath = Path.Combine(Path.GetTempPath(), $"testapp-fw-{Guid.NewGuid():N}.db");
            SQLiteConnection.CreateFile(dbPath);

            using var conn = new SQLiteConnection($"Data Source={dbPath}");
            conn.Open();

            using (var createCmd = new SQLiteCommand("CREATE TABLE items (id INTEGER PRIMARY KEY AUTOINCREMENT, data TEXT, created_at TEXT)", conn))
            {
                createCmd.ExecuteNonQuery();
            }

            using var http = new HttpClient { BaseAddress = new Uri(serverUrl) };
            var rand = new Random();
            var iteration = 0;

            try
            {
                while (!cts.Token.IsCancellationRequested)
                {
                    iteration++;

                    try
                    {
                        // HTTP requests to .NET Core server
                        await http.GetAsync("/api/test", cts.Token);
                        await http.GetAsync("/api/slow", cts.Token);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"HTTP_ERROR:{ex.Message}");
                    }

                    // SQL operations
                    using (var insertCmd = new SQLiteCommand($"INSERT INTO items (data, created_at) VALUES ('{Guid.NewGuid()}', '{DateTime.UtcNow:O}')", conn))
                    {
                        insertCmd.ExecuteNonQuery();
                    }

                    using (var selectCmd = new SQLiteCommand("SELECT COUNT(*) FROM items", conn))
                    {
                        selectCmd.ExecuteScalar();
                    }

                    // GC pressure
                    var garbage = new byte[rand.Next(1024, 1024 * 100)];
                    GC.KeepAlive(garbage);

                    // Exception (caught, for Performance Counter tracking)
                    try
                    {
                        throw new InvalidOperationException($"Test exception {iteration}");
                    }
                    catch
                    {
                        // Expected
                    }

                    await Task.Delay(50, cts.Token);
                }
            }
            catch (OperationCanceledException)
            {
                // Normal shutdown
            }

            // Cleanup
            conn.Close();
            SQLiteConnection.ClearAllPools();

            try { File.Delete(dbPath); }
            catch { /* Best effort */ }

            Console.WriteLine("DONE");
        }
    }
}
