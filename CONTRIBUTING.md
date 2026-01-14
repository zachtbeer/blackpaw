# Contributing to Blackpaw

Thank you for your interest in contributing to Blackpaw! This document provides guidelines and instructions for contributing.

## Code of Conduct

Please be respectful and constructive in all interactions. We want Blackpaw to be a welcoming project for everyone.

## Getting Started

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- Windows 10 SDK (19041 or later)
- Git

### Setting Up Your Development Environment

1. **Fork the Repository**

   Click the "Fork" button on GitHub to create your own copy.

2. **Clone Your Fork**
   ```bash
   git clone https://github.com/zachtbeer/blackpaw.git
   cd blackpaw
   ```

3. **Build the Project**
   ```bash
   dotnet build src/blackpaw.app/Blackpaw.App.csproj
   ```

4. **Run Tests**
   ```bash
   dotnet test tests/Blackpaw.Tests/Blackpaw.Tests.csproj
   ```

5. **Create a Configuration File** (optional, for manual testing)
   ```bash
   cp config.example.json config.json
   # Edit config.json with your settings
   ```

## Making Changes

### Development Workflow

1. **Create a Feature Branch**
   ```bash
   git checkout -b feature/your-feature-name
   # or
   git checkout -b fix/your-bug-fix
   ```

2. **Make Your Changes**
   - Write code following existing patterns
   - Add tests for new functionality
   - Update documentation if needed

3. **Test Your Changes**
   ```bash
   dotnet test tests/Blackpaw.Tests/Blackpaw.Tests.csproj
   ```

4. **Commit Your Changes**
   ```bash
   git add .
   git commit -m "feat: add your feature description"
   ```

   We use [Conventional Commits](https://www.conventionalcommits.org/):
   - `feat:` - New features
   - `fix:` - Bug fixes
   - `docs:` - Documentation changes
   - `test:` - Test additions or fixes
   - `refactor:` - Code refactoring
   - `chore:` - Maintenance tasks

5. **Push and Create a Pull Request**
   ```bash
   git push origin feature/your-feature-name
   ```
   Then open a Pull Request on GitHub.

### PR Labels for Versioning

When creating a PR, add **one** of these labels to control versioning:

| Label | When to Use | Example |
|-------|-------------|---------|
| `patch` | Bug fixes, minor changes | 1.0.0 → 1.0.1 |
| `minor` | New features, backward-compatible | 1.0.0 → 1.1.0 |
| `major` | Breaking changes | 1.0.0 → 2.0.0 |

**No label?** Your PR will still be merged and built, but won't trigger a version increment or release.

## Code Guidelines

### Error Handling

Use try-catch for operations that may fail, with best-effort fallbacks:

```csharp
try
{
    var value = performanceCounter.NextValue();
    // Use value
}
catch (Exception ex)
{
    _logger.LogWarning(ex, "Failed to read performance counter");
    // Continue with fallback or skip
}
```

This is especially important for:
- Performance counter reads
- WMI queries
- Process access
- File operations

### Async Operations

Use `PeriodicTimer` for non-blocking sampling intervals:

```csharp
using var timer = new PeriodicTimer(TimeSpan.FromSeconds(interval));
while (await timer.WaitForNextTickAsync(cancellationToken))
{
    await CollectSampleAsync();
}
```

### Thread Safety

Use thread-safe collections for shared state:

```csharp
private readonly ConcurrentDictionary<int, MonitorSession> _activeSessions = new();
```

### Logging

Use structured logging with appropriate levels:

```csharp
_logger.LogInformation("Started monitoring process {ProcessName} (PID: {ProcessId})", name, pid);
_logger.LogWarning("Failed to attach to process {ProcessId}: {Error}", pid, ex.Message);
_logger.LogError(ex, "Critical error in sampling loop");
```

## Project Structure

```
src/
├── blackpaw.app/           # Main CLI application
│   ├── Program.cs          # Entry point with command definitions
│   ├── Configuration/      # JSON config loading with merge support
│   ├── Diagnostics/        # Host info, DB snapshots
│   ├── Monitoring/         # Deep .NET and SQL monitoring
│   │   ├── DotNetCoreMonitor.cs
│   │   ├── DotNetFrameworkMonitor.cs
│   │   └── SqlDmvSampler.cs
│   ├── Sampling/           # Core sampling loop, process tracking
│   └── Reporting/          # HTML report generation
└── blackpaw.data/          # EF Core + SQLite data layer
    ├── Data/               # Models, DbContext, DatabaseService
    └── Migrations/         # EF Core migrations

tests/
└── Blackpaw.Tests/         # XUnit test suite

samples/
└── SampleWebClient/        # Test application for monitoring
```

## Adding New Features

### Adding a New Metric

1. **Add the property to the appropriate model** in `blackpaw.data/Data/Models/`
2. **Create a migration** if modifying the database schema:
   ```bash
   dotnet ef migrations add YourMigrationName --project src/blackpaw.data
   ```
3. **Update the collector** in `blackpaw.app/Sampling/` or `blackpaw.app/Monitoring/`
4. **Add tests** in `tests/Blackpaw.Tests/`

### Adding a New Command

1. **Define the command** in `Program.cs` using System.CommandLine
2. **Implement the handler** following existing patterns
3. **Update the README** with usage documentation

## Testing

### Running Tests

```bash
# Run all tests
dotnet test tests/Blackpaw.Tests/Blackpaw.Tests.csproj

# Run with detailed output
dotnet test tests/Blackpaw.Tests/Blackpaw.Tests.csproj --logger "console;verbosity=detailed"

# Run specific test
dotnet test tests/Blackpaw.Tests/Blackpaw.Tests.csproj --filter "FullyQualifiedName~YourTestName"
```

### Writing Tests

- Tests use temporary SQLite databases that are cleaned up automatically
- Mock external dependencies (WMI, performance counters) when possible
- Test both success and failure scenarios

```csharp
[Fact]
public async Task YourFeature_WhenCondition_ShouldBehavior()
{
    // Arrange
    using var db = await CreateTestDatabaseAsync();

    // Act
    var result = await YourMethodAsync();

    // Assert
    Assert.NotNull(result);
}
```

## Questions?

- **Issues**: Open a [GitHub Issue](../../issues) for bugs or feature requests
- **Discussions**: Use [GitHub Discussions](../../discussions) for questions

Thank you for contributing!
