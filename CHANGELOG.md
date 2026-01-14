# Changelog

All notable changes to Blackpaw will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added
- Open source release preparation
- SECURITY.md with vulnerability reporting guidelines
- CONTRIBUTING.md with development workflow
- GitHub issue and PR templates
- Quickstart tutorial documentation
- Sample application documentation

## [1.0.0] - 2025-01-13

### Added
- **Core Monitoring**
  - System-level metrics: CPU, memory, disk I/O, network throughput
  - Per-process metrics: CPU%, working set, private bytes, threads, handles
  - Process lifecycle tracking with automatic detection

- **Deep .NET Monitoring**
  - .NET Core runtime monitoring via EventPipe (GC, heap, threading, exceptions)
  - .NET Framework runtime monitoring via Performance Counters
  - HTTP request tracking with endpoint bucketing and latency aggregation

- **SQL Server Monitoring**
  - DMV sampling for wait types, I/O stalls, blocking, connections
  - Database counter collection

- **Data & Reporting**
  - SQLite database storage for all telemetry
  - HTML report generation with charts and statistics
  - Run comparison with regression/improvement analysis
  - User-defined markers for annotating test runs
  - Schema versioning for database compatibility

- **CLI Commands**
  - `start` - Begin a performance capture run
  - `list-runs` - List all runs in the database
  - `info` - Show database statistics
  - `report` - Generate HTML performance reports
  - `compare` - Compare two runs with interactive selection
  - `version` - Display version information

- **Configuration**
  - JSON-based configuration with CLI override support
  - Configurable sampling intervals
  - Per-application deep monitoring settings

### Security
- Configuration files excluded from version control by default
- Security warnings for connection string handling
- Path validation to prevent directory traversal

---

## Version History Summary

| Version | Date | Highlights |
|---------|------|------------|
| 1.0.0 | 2025-01-13 | Initial public release |

[Unreleased]: https://github.com/zachtbeer/blackpaw/compare/v1.0.0...HEAD
[1.0.0]: https://github.com/zachtbeer/blackpaw/releases/tag/v1.0.0
