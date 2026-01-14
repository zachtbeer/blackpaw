# Quickstart Guide

Get started with Blackpaw in 5 minutes.

## Prerequisites

- Windows 10 or later
- Administrator access (recommended for full functionality)
- A .NET application to monitor (or use our sample app)

## Installation

1. **Download the latest release** from the [Releases page](../../releases)
2. **Extract** `blackpaw.exe` to a directory in your PATH (or run it directly)

No installation required - Blackpaw is a self-contained executable.

## Your First Capture

### Step 1: Start Your Application

For this quickstart, we'll use the included sample application:

```bash
cd samples/SampleWebClient
dotnet run
```

This starts a web server on `http://localhost:5099` that generates various types of activity.

> **Don't have the source?** You can monitor any running Windows process - just skip to Step 2.

### Step 2: Start Blackpaw

Open a new terminal and run:

```bash
blackpaw start --scenario my-first-test --processes SampleWebClient
```

You'll see output like:
```
Blackpaw Performance Monitor v1.0.0
===================================

Run ID: 1
Scenario: my-first-test
Database: blackpaw.db

Monitoring processes: SampleWebClient
Sample interval: 1.0 seconds

Press Ctrl+C to stop...

[12:00:01] System: CPU 15.2% | Memory 62.4% | Processes: 1 active
[12:00:02] System: CPU 18.7% | Memory 62.5% | Processes: 1 active
```

### Step 3: Let It Run

Keep both applications running for 1-2 minutes to collect meaningful data.

### Step 4: Stop and Review

Press `Ctrl+C` in the Blackpaw terminal to stop the capture.

**List your runs:**
```bash
blackpaw list-runs
```

**View database info:**
```bash
blackpaw info
```

## Adding Deep Monitoring

Basic monitoring captures system and process metrics. For deeper insights into .NET applications, create a configuration file.

### Create config.json

```json
{
  "DatabasePath": "blackpaw.db",
  "SampleIntervalSeconds": 1.0,
  "ProcessNames": ["SampleWebClient"],
  "EnableDiskMetrics": true,
  "EnableNetworkMetrics": true,
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

### Run with Configuration

```bash
blackpaw start --scenario deep-monitoring-test --config config.json
```

Now you'll capture:
- GC activity (collections, heap sizes, pause times)
- HTTP request metrics (endpoints, latencies, status codes)
- Thread pool statistics
- Exception counts

## Real-World Example: Monitoring a Web API

Let's monitor a production-like scenario with SQL Server integration.

### config.json for Web API

```json
{
  "DatabasePath": "api-metrics.db",
  "SampleIntervalSeconds": 1.0,
  "ProcessNames": ["MyWebApi", "sqlservr"],
  "EnableDiskMetrics": true,
  "EnableNetworkMetrics": true,
  "DeepMonitoring": {
    "DotNetCoreApps": [
      {
        "Name": "MyWebApi",
        "ProcessName": "MyWebApi",
        "Enabled": true,
        "HttpMonitoring": {
          "Enabled": true,
          "BucketIntervalSeconds": 5
        }
      }
    ],
    "SqlDmvSampling": {
      "Enabled": true,
      "SampleIntervalSeconds": 5,
      "SqlConnectionString": "Server=localhost;Database=master;Integrated Security=true;TrustServerCertificate=true;"
    }
  }
}
```

### Run a Load Test Scenario

```bash
# Start monitoring
blackpaw start \
  --scenario load-test-v2 \
  --config config.json \
  --notes "Testing new caching layer with 100 concurrent users" \
  --workload-type api-stress

# In another terminal, run your load test
# ... your load testing tool ...

# Stop Blackpaw with Ctrl+C when done
```

## Command Reference

| Command | Description |
|---------|-------------|
| `blackpaw start --scenario <name>` | Start a capture run |
| `blackpaw list-runs` | List all captured runs |
| `blackpaw info` | Show database statistics |
| `blackpaw version` | Display version info |

### Start Command Options

| Option | Description |
|--------|-------------|
| `--scenario <name>` | **Required.** Label for this run |
| `--notes <text>` | Free-form notes about the test |
| `--config <path>` | Configuration file (default: config.json) |
| `--db <path>` | SQLite database path |
| `--processes <names>` | Comma-separated process names |
| `--sample-interval <sec>` | Override sampling interval |
| `--workload-type <type>` | Workload descriptor |

## What's Captured?

### System Metrics (every sample)
- Total CPU usage
- Available/used memory
- Disk read/write throughput
- Network throughput (if enabled)

### Per-Process Metrics
- CPU percentage
- Working set and private bytes
- Thread and handle counts
- Process start/stop events

### .NET Core Deep Metrics (if configured)
- GC collections by generation
- Heap sizes and fragmentation
- Thread pool queue length
- HTTP request counts and latencies
- Exception counts by type

### SQL Server Metrics (if configured)
- Wait type statistics
- I/O stall times
- Blocking chain information
- Connection counts

## Next Steps

- **[Configuration Reference](../README.md#configuration)** - Full config options
- **[Sample Application](../samples/README.md)** - Explore the test app
- **[Contributing](../CONTRIBUTING.md)** - Help improve Blackpaw

## Troubleshooting

**"Access denied" errors:**
Run as Administrator for full access to performance counters and WMI.

**Process not found:**
Ensure the process name matches exactly (without .exe). Use Task Manager to verify.

**No .NET metrics appearing:**
- Verify the process is a .NET Core application
- Check the `ProcessName` in config matches the actual process name
- Ensure the process is running before starting Blackpaw

**SQL Server connection fails:**
- Verify the connection string
- Try `Integrated Security=true` instead of username/password
- Ensure SQL Server is running and accessible
