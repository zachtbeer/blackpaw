# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with this repository.

## Project Overview

**Blackpaw** is a Windows-based CLI performance monitoring tool written in .NET 10. It captures detailed telemetry during scenario-based workload testing and stores all data in SQLite databases for post-run analysis.

Named in memory of Oliver, a beloved Newfoundland.

### Key Capabilities
- System-level metrics: CPU, memory, disk I/O, network throughput
- Per-process metrics: CPU%, working set, private bytes, threads, handles
- .NET Core runtime monitoring via EventPipe (GC, heap, threading, exceptions)
- .NET Framework runtime monitoring via Performance Counters
- HTTP request tracking with endpoint bucketing and latency aggregation
- SQL Server DMV sampling (wait types, I/O stalls, blocking, connections)
- Markers for process start/stop events and user annotations

## Architecture

```
src/
├── blackpaw.app/           # Main application
│   ├── Program.cs          # CLI entry point (start, list-runs, info, version)
│   ├── Configuration/      # JSON-based config loading with merge support
│   ├── Diagnostics/        # Host info collection, DB snapshots
│   ├── Monitoring/         # Deep monitoring (.NET Core/Framework, SQL, HTTP)
│   └── Sampling/           # Core sampling loop, process monitoring
└── blackpaw.data/          # EF Core + SQLite data layer
    ├── Data/               # Models, DbContext, DatabaseService
    └── Migrations/         # EF Core migrations

tests/
└── Blackpaw.Tests/         # XUnit test suite
```

## Build Commands

```bash
# Build
dotnet build src/blackpaw.app/Blackpaw.App.csproj

# Run tests
dotnet test tests/Blackpaw.Tests/Blackpaw.Tests.csproj

# Publish self-contained executable
dotnet publish src/blackpaw.app/Blackpaw.App.csproj -c Release -r win-x64 --self-contained
```

## CLI Usage

```bash
# Start a performance capture run
blackpaw start --scenario <name> [options]

# List all runs in the database
blackpaw list-runs [--db blackpaw.db]

# Show database statistics
blackpaw info [--db blackpaw.db]

# Options for 'start':
#   --scenario <name>            (Required) Scenario label
#   --notes <text>               Free-form notes
#   --config <path>              Config file (default: config.json)
#   --db <path>                  SQLite database path
#   --processes <name1,name2>    Comma-separated process names to track
#   --sample-interval <seconds>  Override sampling interval
#   --workload-type <type>       Workload descriptor
```

## Configuration

Configuration is loaded from `config.json` (or specified path) with CLI overrides. Key settings:

```json
{
  "DatabasePath": "blackpaw.db",
  "SampleIntervalSeconds": 1.0,
  "ProcessNames": ["app1", "app2"],
  "EnableDiskMetrics": true,
  "EnableNetworkMetrics": false,
  "DeepMonitoring": {
    "DotNetCoreApps": [
      { "Name": "MyApp", "ProcessName": "myapp", "Enabled": true,
        "HttpMonitoring": { "Enabled": true, "BucketIntervalSeconds": 5 } }
    ],
    "SqlDmvSampling": { "Enabled": false, "SampleIntervalSeconds": 5 }
  }
}
```

## Key Design Patterns

- **Resilience**: Try-catch wraps risky operations (counter reads, process access, WMI) with best-effort fallbacks
- **Event-driven**: Process start/stop detection via WMI triggers monitor attachment
- **Async sampling**: Uses `PeriodicTimer` for non-blocking intervals
- **EventPipe**: Modern .NET diagnostics for Core runtime metrics (avoids ETW directly)
- **Thread-safe collections**: `ConcurrentDictionary` for active sessions tracking

## Platform Requirements

- **Windows only**: Relies on Performance Counters, WMI, and EventPipe
- **Target**: net10.0-windows10.0.19041.0
- **Admin recommended**: For full access to performance counters and WMI

## Database Schema

SQLite database with these entities:
- `Runs`: Central entity with metadata (machine, OS, CPU, memory, workload info)
- `SystemSamples`: Time-series system metrics (child of Run)
- `ProcessSamples`: Per-process metrics (child of SystemSample)
- `DotNetRuntimeSamples`: .NET runtime telemetry
- `DotNetHttpSamples`: HTTP request aggregations
- `SqlDmvSamples`: SQL Server DMV snapshots
- `DbSnapshots`: Start/end snapshots of DB counters
- `Markers`: User annotations and tool events

## Testing

Tests use temp SQLite databases and require Windows for full functionality:
```bash
dotnet test tests/Blackpaw.Tests/Blackpaw.Tests.csproj --logger trx
```

## Dependencies

- `Microsoft.Diagnostics.NETCore.Client` - EventPipe session control
- `Microsoft.Diagnostics.Tracing.TraceEvent` - ETW event parsing
- `System.Diagnostics.PerformanceCounter` - Windows perf counters
- `Hardware.Info` - Cross-platform hardware information
- `Microsoft.EntityFrameworkCore.Sqlite` - Data persistence
- `Microsoft.Data.SqlClient` - SQL Server connections
