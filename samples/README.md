# Blackpaw Sample Applications

This directory contains sample applications designed for testing and demonstrating Blackpaw's monitoring capabilities.

## SampleWebClient

A .NET Core web application that generates various types of activity for Blackpaw to monitor.

### Purpose

SampleWebClient is designed to exercise all of Blackpaw's monitoring features:

- **Incoming HTTP Requests** - Self-hosted web server with multiple endpoints
- **Outbound HTTP Requests** - Background worker making requests to httpbin.org
- **GC Activity** - Memory pressure across Gen0, Gen1, Gen2, and LOH
- **Threadpool Work** - CPU-bound tasks and queue processing
- **Exceptions** - Handled exceptions of various types
- **Custom EventSource** - Application-specific metrics

### Running the Sample

```bash
# From the repository root
cd samples/SampleWebClient
dotnet run
```

The application starts a web server on `http://localhost:5099` and begins generating background activity automatically.

### Available Endpoints

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/` | GET | Health check - returns status and timestamp |
| `/status` | GET | Current metrics - request count, GC stats, thread info |
| `/order` | POST | Simulate order processing with async work |
| `/slow/{ms}` | GET | Delayed response (useful for latency testing) |
| `/error/{code}` | GET | Returns specified HTTP status code |
| `/gc/{gen}` | GET | Force garbage collection for generation 0, 1, or 2 |
| `/allocate/{kb}` | GET | Allocate specified KB of memory |
| `/throw` | GET | Throws and catches an exception |

### Background Workers

The application runs several background tasks:

1. **HTTP Worker** - Makes outbound requests to httpbin.org every 1-4 seconds
2. **Memory Worker** - Creates memory pressure across different GC generations
3. **Threadpool Worker** - Queues CPU-bound work items
4. **Exception Worker** - Throws and catches various exception types
5. **Order Processor** - Processes items from the order queue
6. **Self-Request Worker** - Makes requests to its own endpoints

### Using with Blackpaw

1. **Start SampleWebClient**
   ```bash
   cd samples/SampleWebClient
   dotnet run
   ```

2. **In another terminal, start Blackpaw**
   ```bash
   blackpaw start --scenario sample-test --processes SampleWebClient
   ```

3. **Or use deep monitoring** with a config file:
   ```json
   {
     "ProcessNames": ["SampleWebClient"],
     "DeepMonitoring": {
       "DotNetCoreApps": [
         {
           "Name": "SampleWebClient",
           "ProcessName": "SampleWebClient",
           "Enabled": true,
           "HttpMonitoring": {
             "Enabled": true,
             "BucketIntervalSeconds": 5
           }
         }
       ]
     }
   }
   ```

4. **Let it run** for a few minutes to collect data

5. **Stop both** with Ctrl+C and generate a report

### Custom EventSource

SampleWebClient includes a custom `EventSource` that emits application-specific events:

| Event | Level | Description |
|-------|-------|-------------|
| HttpRequestCompleted | Info | Outbound HTTP request with status and duration |
| HttpRequestFailed | Warning | Failed outbound request |
| ExceptionCaught | Warning | Handled exception details |
| OrderProcessed | Info | Order processing completion |
| QueueDepthUpdated | Info | Current order queue size |
| MemoryAllocated | Verbose | Memory allocation events |
| LohAllocation | Info | Large Object Heap allocations |
| ThreadpoolBurst | Info | Threadpool work batch completion |

### Customizing the Sample

You can modify the sample to test specific scenarios:

- Adjust `endpoints` array in the HTTP worker for different request patterns
- Change memory allocation sizes in the memory worker
- Modify exception types and frequencies
- Add new endpoints for specific testing needs

### Troubleshooting

**Port 5099 in use:**
Edit `Program.cs` to change the port:
```csharp
builder.WebHost.UseUrls("http://localhost:5100");
```

**httpbin.org requests failing:**
The outbound HTTP worker will log errors but continue running. This doesn't affect other monitoring features.

**High CPU usage:**
The threadpool worker intentionally creates CPU load. Reduce `iterations` in the worker if needed.
