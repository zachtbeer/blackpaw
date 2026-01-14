using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Tracing;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls("http://localhost:5099");
var app = builder.Build();

// Shared state
var random = new Random();
var requestCount = 0;
var orderQueue = new ConcurrentQueue<Order>();
var pinnedObjects = new List<byte[]>(); // For gen2 promotion

Console.WriteLine("SampleWebClient - A .NET Core app for testing Blackpaw monitoring");
Console.WriteLine("==================================================================");
Console.WriteLine();
Console.WriteLine("This app generates activity that Blackpaw can monitor:");
Console.WriteLine("  - Incoming HTTP requests (self-hosted on http://localhost:5099)");
Console.WriteLine("  - Outbound HTTP requests to httpbin.org");
Console.WriteLine("  - Handled exceptions");
Console.WriteLine("  - Custom EventSource metrics");
Console.WriteLine("  - Memory allocations (Gen0/Gen1/Gen2/LOH)");
Console.WriteLine("  - Threadpool work and contention");
Console.WriteLine();
Console.WriteLine("Endpoints:");
Console.WriteLine("  GET  /              - Health check");
Console.WriteLine("  GET  /status        - Current status");
Console.WriteLine("  POST /order         - Simulate order processing");
Console.WriteLine("  GET  /slow/{ms}     - Slow endpoint (delays response)");
Console.WriteLine("  GET  /error/{code}  - Returns specified HTTP error");
Console.WriteLine("  GET  /gc/{gen}      - Trigger GC for generation (0, 1, 2)");
Console.WriteLine("  GET  /allocate/{kb} - Allocate memory");
Console.WriteLine("  GET  /throw         - Throws and catches exception");
Console.WriteLine();
Console.WriteLine("Background loops will start automatically.");
Console.WriteLine("Press Ctrl+C to stop.");
Console.WriteLine();

// --- HTTP Endpoints ---

app.MapGet("/", () =>
{
    SampleEventSource.Log.RequestReceived("GET", "/");
    return Results.Ok(new { status = "healthy", timestamp = DateTime.UtcNow });
});

app.MapGet("/status", () =>
{
    SampleEventSource.Log.RequestReceived("GET", "/status");
    var gcInfo = GC.GetGCMemoryInfo();
    return Results.Ok(new
    {
        requestCount = requestCount,
        queueDepth = orderQueue.Count,
        heapSizeBytes = GC.GetTotalMemory(false),
        gen0Collections = GC.CollectionCount(0),
        gen1Collections = GC.CollectionCount(1),
        gen2Collections = GC.CollectionCount(2),
        threadPoolThreads = ThreadPool.ThreadCount,
        pinnedObjectCount = pinnedObjects.Count
    });
});

app.MapPost("/order", async (HttpContext ctx) =>
{
    Interlocked.Increment(ref requestCount);
    SampleEventSource.Log.RequestReceived("POST", "/order");

    var order = new Order
    {
        Id = Guid.NewGuid(),
        CreatedAt = DateTime.UtcNow,
        Value = random.Next(10, 1000)
    };
    orderQueue.Enqueue(order);

    // Simulate async processing with threadpool work
    await Task.Run(() =>
    {
        Thread.SpinWait(random.Next(1000, 10000));
        SampleEventSource.Log.OrderProcessed(order.Id.GetHashCode(), order.Value, random.Next(5, 50));
    });

    return Results.Created($"/order/{order.Id}", order);
});

app.MapGet("/slow/{ms:int}", async (int ms) =>
{
    SampleEventSource.Log.RequestReceived("GET", $"/slow/{ms}");
    var sw = Stopwatch.StartNew();
    await Task.Delay(Math.Clamp(ms, 0, 30000));
    sw.Stop();
    return Results.Ok(new { requestedDelayMs = ms, actualDelayMs = sw.ElapsedMilliseconds });
});

app.MapGet("/error/{code:int}", (int code) =>
{
    SampleEventSource.Log.RequestReceived("GET", $"/error/{code}");
    SampleEventSource.Log.ErrorReturned(code);
    return Results.StatusCode(code);
});

app.MapGet("/gc/{gen:int}", (int gen) =>
{
    SampleEventSource.Log.RequestReceived("GET", $"/gc/{gen}");
    var before = GC.GetTotalMemory(false);
    GC.Collect(Math.Clamp(gen, 0, 2), GCCollectionMode.Forced, true);
    var after = GC.GetTotalMemory(false);
    SampleEventSource.Log.GcForced(gen, before - after);
    return Results.Ok(new
    {
        generation = gen,
        beforeBytes = before,
        afterBytes = after,
        freedBytes = before - after
    });
});

app.MapGet("/allocate/{kb:int}", (int kb) =>
{
    SampleEventSource.Log.RequestReceived("GET", $"/allocate/{kb}");
    var bytes = Math.Clamp(kb, 1, 100_000) * 1024;
    var buffer = new byte[bytes];
    random.NextBytes(buffer.AsSpan(0, Math.Min(1024, bytes))); // Touch some memory
    SampleEventSource.Log.MemoryAllocated(bytes);
    return Results.Ok(new { allocatedBytes = bytes, hashCode = buffer.GetHashCode() });
});

app.MapGet("/throw", () =>
{
    SampleEventSource.Log.RequestReceived("GET", "/throw");
    try
    {
        throw new InvalidOperationException("Intentional test exception");
    }
    catch (Exception ex)
    {
        SampleEventSource.Log.ExceptionCaught(ex.GetType().Name, ex.Message);
        return Results.Ok(new { caught = true, exceptionType = ex.GetType().Name, message = ex.Message });
    }
});

// --- Background workers ---

var cts = new CancellationTokenSource();

// Worker 1: Outbound HTTP requests
var httpWorker = Task.Run(async () =>
{
    using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
    var endpoints = new[]
    {
        ("GET", "https://httpbin.org/get"),
        ("GET", "https://httpbin.org/status/200"),
        ("GET", "https://httpbin.org/delay/1"),
        ("POST", "https://httpbin.org/post"),
        ("GET", "https://httpbin.org/status/404"),
        ("GET", "https://httpbin.org/status/500"),
        ("GET", "https://httpbin.org/bytes/1024"),
        ("GET", "https://httpbin.org/uuid"),
    };

    while (!cts.Token.IsCancellationRequested)
    {
        var (method, url) = endpoints[random.Next(endpoints.Length)];
        try
        {
            var sw = Stopwatch.StartNew();
            HttpResponseMessage response;

            if (method == "POST")
            {
                var content = new StringContent($"{{\"timestamp\": \"{DateTime.UtcNow:O}\"}}");
                response = await client.PostAsync(url, content, cts.Token);
            }
            else
            {
                response = await client.GetAsync(url, cts.Token);
            }

            sw.Stop();
            SampleEventSource.Log.HttpRequestCompleted(url, (int)response.StatusCode, sw.ElapsedMilliseconds);
            Console.WriteLine($"[HTTP] {method} {url} => {(int)response.StatusCode} ({sw.ElapsedMilliseconds}ms)");
        }
        catch (OperationCanceledException) when (cts.Token.IsCancellationRequested)
        {
            break;
        }
        catch (Exception ex)
        {
            SampleEventSource.Log.HttpRequestFailed(url, ex.GetType().Name);
            Console.WriteLine($"[HTTP] {method} {url} => ERROR: {ex.Message}");
        }

        await Task.Delay(TimeSpan.FromSeconds(random.Next(1, 4)), cts.Token).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
    }
}, cts.Token);

// Worker 2: Memory pressure (different GC generations)
var memoryWorker = Task.Run(async () =>
{
    while (!cts.Token.IsCancellationRequested)
    {
        // Gen0 pressure: small short-lived allocations
        for (int i = 0; i < random.Next(50, 200); i++)
        {
            var temp = new byte[random.Next(100, 8000)];
            _ = temp.Length; // prevent optimization
        }

        // Occasionally create medium-lived objects (Gen1 candidates)
        if (random.Next(3) == 0)
        {
            var mediumLived = new List<byte[]>();
            for (int i = 0; i < random.Next(5, 20); i++)
            {
                mediumLived.Add(new byte[random.Next(1000, 20000)]);
            }
            await Task.Delay(500, cts.Token).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
            mediumLived.Clear();
        }

        // Occasionally create long-lived objects (Gen2 candidates)
        if (random.Next(10) == 0)
        {
            pinnedObjects.Add(new byte[random.Next(10000, 50000)]);
            if (pinnedObjects.Count > 100)
            {
                pinnedObjects.RemoveRange(0, 50); // Keep bounded
            }
        }

        // Occasionally allocate LOH objects (>85KB)
        if (random.Next(8) == 0)
        {
            var loh = new byte[random.Next(90_000, 200_000)];
            SampleEventSource.Log.LohAllocation(loh.Length);
            Console.WriteLine($"[MEM] LOH allocation: {loh.Length / 1024}KB");
        }

        await Task.Delay(TimeSpan.FromMilliseconds(random.Next(100, 500)), cts.Token).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
    }
}, cts.Token);

// Worker 3: Threadpool saturation
var threadpoolWorker = Task.Run(async () =>
{
    while (!cts.Token.IsCancellationRequested)
    {
        // Queue multiple work items to show threadpool activity
        var taskCount = random.Next(5, 20);
        var tasks = new List<Task>();

        for (int i = 0; i < taskCount; i++)
        {
            tasks.Add(Task.Run(() =>
            {
                // CPU-bound work
                var iterations = random.Next(10000, 100000);
                double result = 0;
                for (int j = 0; j < iterations; j++)
                {
                    result += Math.Sqrt(j) * Math.Sin(j);
                }
                return result;
            }));
        }

        await Task.WhenAll(tasks);
        SampleEventSource.Log.ThreadpoolBurst(taskCount);
        Console.WriteLine($"[POOL] Completed {taskCount} threadpool tasks");

        await Task.Delay(TimeSpan.FromSeconds(random.Next(2, 5)), cts.Token).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
    }
}, cts.Token);

// Worker 4: Exception generator
var exceptionWorker = Task.Run(async () =>
{
    var exceptionTypes = new Func<Exception>[]
    {
        () => new InvalidOperationException("Simulated invalid operation"),
        () => new ArgumentException("Simulated argument error", "param"),
        () => new FormatException("Simulated format error"),
        () => new KeyNotFoundException("Simulated key not found"),
        () => new TimeoutException("Simulated timeout"),
        () => new NotSupportedException("Simulated not supported"),
        () => new ArithmeticException("Simulated arithmetic error"),
    };

    while (!cts.Token.IsCancellationRequested)
    {
        // Throw 1-3 exceptions per iteration
        var count = random.Next(1, 4);
        for (int i = 0; i < count; i++)
        {
            var factory = exceptionTypes[random.Next(exceptionTypes.Length)];
            try
            {
                throw factory();
            }
            catch (Exception ex)
            {
                SampleEventSource.Log.ExceptionCaught(ex.GetType().Name, ex.Message);
            }
        }

        Console.WriteLine($"[EXC] Threw and caught {count} exceptions");
        await Task.Delay(TimeSpan.FromSeconds(random.Next(3, 8)), cts.Token).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
    }
}, cts.Token);

// Worker 5: Order queue processor
var orderProcessor = Task.Run(async () =>
{
    while (!cts.Token.IsCancellationRequested)
    {
        var processed = 0;
        while (orderQueue.TryDequeue(out var order))
        {
            // Simulate order processing
            await Task.Delay(random.Next(10, 50), cts.Token).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
            processed++;
        }

        if (processed > 0)
        {
            SampleEventSource.Log.QueueDrained(processed);
            Console.WriteLine($"[QUEUE] Processed {processed} orders");
        }

        SampleEventSource.Log.QueueDepthUpdated(orderQueue.Count);
        await Task.Delay(TimeSpan.FromSeconds(1), cts.Token).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
    }
}, cts.Token);

// Worker 6: Self-requests (to generate incoming HTTP traffic)
var selfRequestWorker = Task.Run(async () =>
{
    using var client = new HttpClient { BaseAddress = new Uri("http://localhost:5099") };

    // Wait for server to start
    await Task.Delay(2000, cts.Token).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);

    while (!cts.Token.IsCancellationRequested)
    {
        try
        {
            // Mix of different endpoint calls
            switch (random.Next(6))
            {
                case 0:
                    await client.GetAsync("/", cts.Token);
                    break;
                case 1:
                    await client.GetAsync("/status", cts.Token);
                    break;
                case 2:
                    await client.PostAsync("/order", new StringContent("{}"), cts.Token);
                    break;
                case 3:
                    await client.GetAsync($"/slow/{random.Next(50, 200)}", cts.Token);
                    break;
                case 4:
                    await client.GetAsync($"/error/{(random.Next(2) == 0 ? 400 : 500)}", cts.Token);
                    break;
                case 5:
                    await client.GetAsync("/throw", cts.Token);
                    break;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Ignore errors
        }

        await Task.Delay(TimeSpan.FromMilliseconds(random.Next(200, 1000)), cts.Token).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
    }
}, cts.Token);

// Handle shutdown
app.Lifetime.ApplicationStopping.Register(() =>
{
    Console.WriteLine("\nShutting down background workers...");
    cts.Cancel();
});

// Run the app
await app.RunAsync();

// Wait for workers to complete
await Task.WhenAll(httpWorker, memoryWorker, threadpoolWorker, exceptionWorker, orderProcessor, selfRequestWorker)
    .ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);

Console.WriteLine("Shutdown complete.");

// --- Types ---

record Order
{
    public Guid Id { get; init; }
    public DateTime CreatedAt { get; init; }
    public int Value { get; init; }
}

// --- Custom EventSource for metrics ---

[EventSource(Name = "SampleWebClient")]
sealed class SampleEventSource : EventSource
{
    public static readonly SampleEventSource Log = new();

    [Event(1, Level = EventLevel.Informational, Message = "HTTP request to {0} completed with status {1} in {2}ms")]
    public void HttpRequestCompleted(string url, int statusCode, long durationMs)
        => WriteEvent(1, url, statusCode, durationMs);

    [Event(2, Level = EventLevel.Warning, Message = "HTTP request to {0} failed: {1}")]
    public void HttpRequestFailed(string url, string exceptionType)
        => WriteEvent(2, url, exceptionType);

    [Event(3, Level = EventLevel.Warning, Message = "Exception caught: {0} - {1}")]
    public void ExceptionCaught(string exceptionType, string message)
        => WriteEvent(3, exceptionType, message);

    [Event(4, Level = EventLevel.Informational, Message = "Order {0} processed: ${1} in {2}ms")]
    public void OrderProcessed(int orderId, int value, int processingTimeMs)
        => WriteEvent(4, orderId, value, processingTimeMs);

    [Event(5, Level = EventLevel.Informational, Message = "Queue depth: {0}")]
    public void QueueDepthUpdated(int depth)
        => WriteEvent(5, depth);

    [Event(6, Level = EventLevel.Verbose, Message = "Memory allocated: {0} bytes")]
    public void MemoryAllocated(int bytes)
        => WriteEvent(6, bytes);

    [Event(7, Level = EventLevel.Verbose, Message = "CPU work: {0} units in {1}ms")]
    public void CpuWorkCompleted(int workUnits, long durationMs)
        => WriteEvent(7, workUnits, durationMs);

    [Event(8, Level = EventLevel.Informational, Message = "Request received: {0} {1}")]
    public void RequestReceived(string method, string path)
        => WriteEvent(8, method, path);

    [Event(9, Level = EventLevel.Warning, Message = "Error returned: {0}")]
    public void ErrorReturned(int statusCode)
        => WriteEvent(9, statusCode);

    [Event(10, Level = EventLevel.Informational, Message = "GC forced gen {0}, freed {1} bytes")]
    public void GcForced(int generation, long freedBytes)
        => WriteEvent(10, generation, freedBytes);

    [Event(11, Level = EventLevel.Informational, Message = "LOH allocation: {0} bytes")]
    public void LohAllocation(int bytes)
        => WriteEvent(11, bytes);

    [Event(12, Level = EventLevel.Informational, Message = "Threadpool burst: {0} tasks")]
    public void ThreadpoolBurst(int taskCount)
        => WriteEvent(12, taskCount);

    [Event(13, Level = EventLevel.Informational, Message = "Queue drained: {0} items")]
    public void QueueDrained(int count)
        => WriteEvent(13, count);
}
