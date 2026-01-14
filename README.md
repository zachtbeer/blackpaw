# Blackpaw

[![Build Status](https://github.com/zachtbeer/blackpaw/actions/workflows/build-and-test.yml/badge.svg)](https://github.com/zachtbeer/blackpaw/actions/workflows/build-and-test.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
[![Platform: Windows](https://img.shields.io/badge/Platform-Windows-0078d4.svg)](https://github.com/zachtbeer/blackpaw/releases)
[![.NET 10](https://img.shields.io/badge/.NET-10.0-512bd4.svg)](https://dotnet.microsoft.com/download/dotnet/10.0)

> *Named in memory of Oliver, a beloved Newfoundland.*

**Blackpaw** is a Windows-based CLI performance monitoring tool that captures detailed telemetry during scenario-based workload testing. All metrics are stored in SQLite databases for comprehensive post-run analysis and reporting.

## Features

### System-Level Metrics
- **CPU & Memory**: Total CPU usage, available/used memory
- **Disk I/O**: Read/write throughput, IOPS
- **Network**: Throughput monitoring

### Per-Process Metrics
- CPU percentage
- Working set and private bytes
- Thread and handle counts
- Process lifecycle tracking (start/stop events)

### Deep Runtime Monitoring

#### .NET Core Applications
- **EventPipe Integration**: GC activity, heap statistics, threading metrics, exceptions
- **HTTP Request Tracking**: Endpoint bucketing, latency aggregation, request counts
- No ETW required - uses modern .NET diagnostics

#### .NET Framework Applications
- Performance counter-based monitoring
- Runtime metrics collection

### SQL Server Monitoring
- **DMV Sampling**: Wait types, I/O stalls, blocking chains, connection counts
- Configurable sampling intervals

### Data & Reporting
- All data persisted to SQLite for post-run analysis
- HTML report generation with charts and statistics
- User-defined markers for annotating test runs
- Run comparison and historical tracking

## Example Output

### CLI Output

```
Starting Blackpaw run 1 for scenario 'baseline'.
Database: blackpaw.db
Sample interval: 1s
Tracking processes: myapp, sqlservr
Press Ctrl+C to stop.

[12:34:56] CPU: 45.2% | Memory: 8.2 GB / 16.0 GB | Disk R: 125 MB/s W: 45 MB/s
[12:34:57] CPU: 52.1% | Memory: 8.3 GB / 16.0 GB | Disk R: 132 MB/s W: 52 MB/s
[12:34:58] CPU: 48.7% | Memory: 8.2 GB / 16.0 GB | Disk R: 118 MB/s W: 48 MB/s
...
Run 1 finished after 120.5s. Data stored in blackpaw.db.
```

### HTML Report Features

The generated HTML reports include:
- **Summary Dashboard**: Run metadata, duration, host information
- **System Metrics Charts**: CPU, memory, disk I/O over time
- **Process Metrics**: Per-process CPU and memory usage
- **.NET Runtime Stats**: GC collections, heap sizes, thread pool utilization
- **HTTP Request Analysis**: Endpoint latencies, status code distribution
- **SQL Server Insights**: Wait types, blocking, I/O stall times

Reports are self-contained HTML files with embedded Chart.js visualizations.

## Installation

Download the latest release from the [Releases page](../../releases). Blackpaw is distributed as a self-contained Windows executable - no installation required.

```bash
# Run from command line
blackpaw.exe start --scenario baseline --notes "Initial test"
```

## Usage

### Start a Monitoring Run

```bash
# Basic usage
blackpaw start --scenario <name>

# With additional options
blackpaw start --scenario load-test \
  --notes "Testing new caching layer" \
  --processes "myapp,sqlservr" \
  --sample-interval 2 \
  --workload-type api-stress
```

**Options:**
- `--scenario <name>` - (Required) Scenario label for organizing runs
- `--notes <text>` - Free-form notes about the test
- `--config <path>` - Config file path (default: `config.json`)
- `--db <path>` - SQLite database path (default: `blackpaw.db`)
- `--processes <names>` - Comma-separated process names to monitor
- `--sample-interval <seconds>` - Override sampling interval
- `--workload-type <type>` - Workload descriptor

### List Runs

```bash
blackpaw list-runs [--db blackpaw.db]
```

### View Database Info

```bash
blackpaw info [--db blackpaw.db]
```

### Configuration

> **⚠️ SECURITY WARNING**: Configuration files may contain sensitive connection strings with credentials.
> - **DO NOT** commit `config.json` to version control
> - Use `Integrated Security=true` when possible to avoid storing credentials
> - The `.gitignore` file excludes `config.json` by default
> - See `config.example.json` for a template

Create a `config.json` file to configure monitoring behavior:

```json
{
  "DatabasePath": "blackpaw.db",
  "SampleIntervalSeconds": 1.0,
  "ProcessNames": ["myapp", "sqlservr"],
  "EnableDiskMetrics": true,
  "EnableNetworkMetrics": true,
  "DeepMonitoring": {
    "DotNetCoreApps": [
      {
        "Name": "MyApp",
        "ProcessName": "myapp",
        "Enabled": true,
        "HttpMonitoring": {
          "Enabled": true,
          "BucketIntervalSeconds": 5
        }
      }
    ],
    "SqlDmvSampling": {
      "Enabled": false,
      "SampleIntervalSeconds": 5,
      "SqlConnectionString": "Server=localhost;Database=master;Integrated Security=true;TrustServerCertificate=true;"
    },
    "EnableDbCounters": false,
    "DbConnectionString": "Server=localhost;Database=mydb;Integrated Security=true;TrustServerCertificate=true;"
  }
}
```

**Configuration Properties:**
- `DatabasePath` - SQLite database file location
- `SampleIntervalSeconds` - How often to sample system metrics
- `ProcessNames` - Array of process names to monitor
- `EnableDiskMetrics` - Collect disk I/O metrics
- `EnableNetworkMetrics` - Collect network throughput metrics
- `DeepMonitoring.DotNetCoreApps` - .NET Core applications to monitor via EventPipe
- `DeepMonitoring.DotNetFrameworkApps` - .NET Framework applications to monitor via performance counters
- `DeepMonitoring.SqlDmvSampling` - SQL Server DMV sampling configuration
- `DeepMonitoring.EnableDbCounters` - Enable SQL Server database counter collection
- `DeepMonitoring.DbConnectionString` - Connection string for database counters

## Requirements

- **Platform**: Windows only (uses Performance Counters, WMI, EventPipe)
- **Runtime**: Self-contained - no .NET installation required
- **Permissions**: Administrator recommended for full access to performance counters and WMI

## Building from Source

### Prerequisites
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- Windows 10 SDK (19041 or later)

### Build Commands

```bash
# Build
dotnet build src/blackpaw.app/Blackpaw.App.csproj

# Run tests
dotnet test tests/Blackpaw.Tests/Blackpaw.Tests.csproj

# Publish self-contained executable
dotnet publish src/blackpaw.app/Blackpaw.App.csproj \
  -c Release \
  -r win-x64 \
  --self-contained \
  /p:PublishSingleFile=true
```

## Contributing

We welcome contributions! See [CONTRIBUTING.md](CONTRIBUTING.md) for detailed guidelines.

**Quick start:**

```bash
git clone https://github.com/zachtbeer/blackpaw.git
cd blackpaw
dotnet build src/blackpaw.app/Blackpaw.App.csproj
dotnet test tests/Blackpaw.Tests/Blackpaw.Tests.csproj
```

**PR Labels for Versioning:**
- `patch` - Bug fixes (1.0.0 → 1.0.1)
- `minor` - New features (1.0.0 → 1.1.0)
- `major` - Breaking changes (1.0.0 → 2.0.0)

## Architecture

### Key Design Patterns
- **Resilience**: Try-catch wrappers with fallbacks for platform APIs
- **Event-Driven**: WMI-based process start/stop detection
- **Async Sampling**: Non-blocking periodic sampling
- **Modern .NET Diagnostics**: EventPipe for Core runtime metrics (no ETW required)

### Database Schema
- **Runs**: Central entity with machine/workload metadata
- **SystemSamples**: Time-series system metrics
- **ProcessSamples**: Per-process metrics
- **DotNetRuntimeSamples**: .NET runtime telemetry
- **DotNetHttpSamples**: HTTP aggregations
- **SqlDmvSamples**: SQL Server DMV snapshots
- **Markers**: User annotations and tool events

## FAQ

### Why Windows only?

Blackpaw relies on Windows-specific APIs for comprehensive monitoring:
- **Performance Counters**: Windows Performance Counter infrastructure for system and .NET Framework metrics
- **WMI (Windows Management Instrumentation)**: Process lifecycle detection (start/stop events)
- **EventPipe**: While EventPipe itself is cross-platform, the way Blackpaw integrates it with other Windows-specific monitoring makes the overall tool Windows-focused

Linux/macOS support is on the roadmap but would require significant refactoring to use platform-appropriate alternatives (e.g., `/proc` filesystem, `perf_events`).

### What's the performance overhead?

Blackpaw is designed for minimal impact:
- **CPU overhead**: Typically < 1% during normal sampling (1-second intervals)
- **Memory usage**: ~50-100 MB depending on number of monitored processes
- **Disk I/O**: Minimal - SQLite writes are batched and lightweight

For deep .NET monitoring via EventPipe, overhead increases slightly but remains under 2-3% in most scenarios. You can reduce overhead by:
- Increasing `SampleIntervalSeconds` (e.g., 5 seconds instead of 1)
- Monitoring fewer processes
- Disabling HTTP monitoring if not needed

### How large do database files get?

Database size depends on run duration and configuration:
- **Basic monitoring**: ~1-2 MB per hour of capture
- **With .NET deep monitoring**: ~5-10 MB per hour
- **With SQL DMV sampling**: ~10-20 MB per hour

Example: A 2-hour load test with full monitoring typically produces a 20-40 MB database file.

### Can I run Blackpaw without admin privileges?

Partially. Without admin privileges:
- ✅ Basic process monitoring (CPU, memory for processes you own)
- ✅ SQLite database operations
- ❌ Some Performance Counters may be inaccessible
- ❌ WMI process start/stop events may not fire
- ❌ EventPipe attachment to other users' processes will fail

For full functionality, running as Administrator is recommended.

### How do I monitor a remote machine?

Blackpaw currently only supports local monitoring. For remote scenarios:
1. Run Blackpaw directly on the target machine
2. Copy the generated `.db` file to your workstation for analysis
3. Generate reports locally using `blackpaw report --db <copied-file>`

Remote monitoring is being considered for future versions.

### Can I query the SQLite database directly?

Yes! The database uses a straightforward schema. Example queries:

```sql
-- Get system metrics for a run
SELECT * FROM SystemSamples WHERE RunId = 1 ORDER BY SampleTime;

-- Get process CPU usage
SELECT ps.ProcessName, AVG(ps.CpuPercent) as AvgCpu
FROM ProcessSamples ps
JOIN SystemSamples ss ON ps.SystemSampleId = ss.Id
WHERE ss.RunId = 1
GROUP BY ps.ProcessName;

-- Get .NET GC statistics
SELECT * FROM DotNetRuntimeSamples WHERE RunId = 1;
```

## Roadmap

Future enhancements under consideration:
- Linux/macOS support (where platform APIs allow)
- Real-time visualization dashboard
- Prometheus/Grafana integration
- Custom alerting on threshold violations
- Comparative analysis across multiple runs

## Security

See [SECURITY.md](SECURITY.md) for our security policy and vulnerability reporting process.

**Key points:**
- **Never commit config.json** - it may contain credentials
- Use Windows Integrated Security when possible
- Database files contain telemetry that may include sensitive information
- Review reports before sharing externally

## Documentation

- **[Quickstart Guide](docs/quickstart.md)** - Get started in 5 minutes
- **[Sample Application](samples/README.md)** - Test app for exploring Blackpaw
- **[Contributing Guide](CONTRIBUTING.md)** - How to contribute
- **[Security Policy](SECURITY.md)** - Vulnerability reporting
- **[Changelog](CHANGELOG.md)** - Version history

## Support

- **Issues**: [GitHub Issues](../../issues)
- **Discussions**: [GitHub Discussions](../../discussions)

## Acknowledgments

Built with:
- [.NET 10](https://dotnet.microsoft.com/)
- [Spectre.Console](https://spectreconsole.net/) - Beautiful CLI output
- [Entity Framework Core](https://docs.microsoft.com/ef/core/) - Data persistence
- [Microsoft.Diagnostics.NETCore.Client](https://github.com/dotnet/diagnostics) - EventPipe support
- [Hardware.Info](https://github.com/Jinjinov/Hardware.Info) - Hardware detection

---

**In memory of Oliver** 🐾
