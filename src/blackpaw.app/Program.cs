using Blackpaw.Analysis;
using Blackpaw.Configuration;
using Blackpaw.Data;
using Blackpaw.Diagnostics;
using Blackpaw.Interactive;
using Blackpaw.Reporting;
using Blackpaw.Sampling;
using Spectre.Console;
using System.Reflection;

// Check for --nologo flag
var showLogo = !args.Any(a => a.Equals("--nologo", StringComparison.OrdinalIgnoreCase));
args = args.Where(a => !a.Equals("--nologo", StringComparison.OrdinalIgnoreCase)).ToArray();

var command = args.Length > 0 ? args[0].ToLowerInvariant() : string.Empty;

// Print logo unless suppressed (skip for version-only queries)
if (showLogo && command is not ("--version" or "version"))
{
    PrintLogo();
}

// Help requested explicitly
if (command is "-h" or "--help")
{
    PrintUsage();
    return 0;
}

// No arguments = interactive mode
if (string.IsNullOrWhiteSpace(command))
{
    return await RunInteractiveModeAsync();
}

return command switch
{
    "start" => await HandleStartAsync(args.Skip(1).ToArray()),
    "list-runs" => HandleListRuns(args.Skip(1).ToArray()),
    "info" => HandleInfo(args.Skip(1).ToArray()),
    "report" => HandleReport(args.Skip(1).ToArray()),
    "compare" => HandleCompare(args.Skip(1).ToArray()),
    "--version" or "version" => HandleVersion(),
    _ => HandleUnknownCommand(command)
};

static int HandleVersion()
{
    Console.WriteLine(GetVersion());
    return 0;
}

static int HandleUnknownCommand(string command)
{
    Console.WriteLine($"Error: Unknown command '{command}'.");
    Console.WriteLine();
    Console.WriteLine("Available commands: start, list-runs, info, report, compare, version");
    Console.WriteLine("Run 'blackpaw --help' for usage information.");
    return 2;
}

static async Task<int> HandleStartAsync(string[] args)
{
    var options = ParseOptions(args);

    var scenario = options.GetValueOrDefault("scenario");
    if (string.IsNullOrWhiteSpace(scenario))
    {
        scenario = DateTime.Now.ToString("yy-MM-dd HH:mm:ss");
        Console.WriteLine($"Using default scenario name: {scenario}");
    }

    var notes = options.GetValueOrDefault("notes");
    var configPath = options.GetValueOrDefault("config");
    var dbPathOverride = options.GetValueOrDefault("db");
    var workloadType = options.GetValueOrDefault("workload-type");
    int? workloadSize = int.TryParse(options.GetValueOrDefault("workload-size"), out var size) ? size : null;
    var workloadNotes = options.GetValueOrDefault("workload-notes");

    var config = AppConfig.Load(configPath);
    if (!string.IsNullOrWhiteSpace(dbPathOverride))
    {
        config.DatabasePath = dbPathOverride!;
    }

    if (options.TryGetValue("sample-interval", out var intervalValue) && double.TryParse(intervalValue, out var interval) && interval > 0)
    {
        config.SampleIntervalSeconds = interval;
    }

    if (options.TryGetValue("processes", out var processValue) && !string.IsNullOrWhiteSpace(processValue))
    {
        if (processValue == "*")
        {
            // Auto-detect all .NET processes
            var detected = ProcessDetector.DetectAllDotNetProcesses();
            config.ProcessNames = detected.Select(p => p.ProcessName).ToList();
            Console.WriteLine($"Auto-detected {config.ProcessNames.Count} .NET process(es): {string.Join(", ", config.ProcessNames)}");
        }
        else
        {
            config.ProcessNames = processValue.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        }
    }

    // Handle --dotnet-core-apps flag
    if (options.TryGetValue("dotnet-core-apps", out var coreAppsValue) && !string.IsNullOrWhiteSpace(coreAppsValue))
    {
        var enableHttp = options.ContainsKey("enable-http");

        if (coreAppsValue == "*")
        {
            var detected = ProcessDetector.DetectDotNetCoreProcesses();
            Console.WriteLine($"Auto-detected {detected.Count} .NET Core process(es)");
            foreach (var proc in detected)
            {
                config.DeepMonitoring.DotNetCoreApps.Add(new DotNetAppConfig
                {
                    Name = proc.ProcessName,
                    ProcessName = proc.ProcessName,
                    Enabled = true,
                    HttpMonitoring = new DotNetHttpMonitoringConfig { Enabled = enableHttp, BucketIntervalSeconds = 5 }
                });
                Console.WriteLine($"  Added .NET Core app: {proc.ProcessName}");
            }
        }
        else
        {
            var names = coreAppsValue.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var name in names)
            {
                config.DeepMonitoring.DotNetCoreApps.Add(new DotNetAppConfig
                {
                    Name = name,
                    ProcessName = name,
                    Enabled = true,
                    HttpMonitoring = new DotNetHttpMonitoringConfig { Enabled = enableHttp, BucketIntervalSeconds = 5 }
                });
            }
        }
    }

    // Handle --dotnet-fx-apps flag
    if (options.TryGetValue("dotnet-fx-apps", out var fxAppsValue) && !string.IsNullOrWhiteSpace(fxAppsValue))
    {
        if (fxAppsValue == "*")
        {
            var detected = ProcessDetector.DetectDotNetFrameworkProcesses();
            Console.WriteLine($"Auto-detected {detected.Count} .NET Framework process(es)");
            foreach (var proc in detected)
            {
                config.DeepMonitoring.DotNetFrameworkApps.Add(new DotNetAppConfig
                {
                    Name = proc.ProcessName,
                    ProcessName = proc.ProcessName,
                    Enabled = true
                });
                Console.WriteLine($"  Added .NET Framework app: {proc.ProcessName}");
            }
        }
        else
        {
            var names = fxAppsValue.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var name in names)
            {
                config.DeepMonitoring.DotNetFrameworkApps.Add(new DotNetAppConfig
                {
                    Name = name,
                    ProcessName = name,
                    Enabled = true
                });
            }
        }
    }

    var database = new DatabaseService(config.DatabasePath);
    database.Initialize();

    var run = new RunRecord
    {
        ScenarioName = scenario!,
        Notes = notes,
        StartedAtUtc = DateTime.UtcNow,
        ProbeVersion = GetVersion(),
        ConfigSnapshot = config.ToJson(),
        WorkloadType = workloadType,
        WorkloadSizeEstimate = workloadSize,
        WorkloadNotes = workloadNotes
    };

    HostInfoCollector.PopulateHostFields(run);
    var runId = database.InsertRun(run);

    Console.WriteLine($"Starting Blackpaw run {runId} for scenario '{scenario}'.");
    Console.WriteLine($"Database: {config.DatabasePath}");
    Console.WriteLine($"Sample interval: {config.SampleIntervalSeconds}s");
    Console.WriteLine($"Tracking processes: {string.Join(", ", config.ProcessNames)}");
    Console.WriteLine("Press Ctrl+C to stop.");

    DbSnapshotCollector? dbCollector = null;
    if (config.EnableDbCounters && !string.IsNullOrWhiteSpace(config.DbConnectionString))
    {
        dbCollector = new DbSnapshotCollector(config.DbConnectionString!);
        var startSnapshot = await dbCollector.CaptureAsync(runId, "start", CancellationToken.None);
        if (startSnapshot != null)
        {
            database.InsertDbSnapshot(startSnapshot);
        }
    }

    using var samplingSession = new SamplingSession(database, config);
    using var cts = new CancellationTokenSource();
    Console.CancelKeyPress += (_, eventArgs) =>
    {
        eventArgs.Cancel = true;
        cts.Cancel();
    };

    var start = DateTime.UtcNow;
    try
    {
        await samplingSession.RunAsync(runId, cts.Token);
    }
    catch (OperationCanceledException)
    {
        // expected when Ctrl+C is pressed
    }

    var end = DateTime.UtcNow;
    var duration = (end - start).TotalSeconds;
    database.UpdateRunEnd(runId, end, duration);

    if (dbCollector != null)
    {
        var endSnapshot = await dbCollector.CaptureAsync(runId, "end", CancellationToken.None);
        if (endSnapshot != null)
        {
            database.InsertDbSnapshot(endSnapshot);
        }
    }

    Console.WriteLine($"Run {runId} finished after {duration:N1}s. Data stored in {config.DatabasePath}.");
    return 0;
}

static int HandleListRuns(string[] args)
{
    var options = ParseOptions(args);
    var dbPath = options.GetValueOrDefault("db") ?? GetDefaultDatabasePath();
    var database = new DatabaseService(dbPath);
    database.Initialize();

    var runs = database.GetRuns();
    if (runs.Count == 0)
    {
        Console.WriteLine("No runs found.");
        return 0;
    }

    foreach (var run in runs)
    {
        var status = run.EndedAtUtc.HasValue ? "completed" : "running";
        Console.WriteLine($"[{run.RunId}] {run.ScenarioName} | {run.StartedAtUtc:o} | {status} | Notes: {run.Notes}");
    }
    return 0;
}

static int HandleInfo(string[] args)
{
    var options = ParseOptions(args);
    var dbPath = options.GetValueOrDefault("db") ?? GetDefaultDatabasePath();
    var database = new DatabaseService(dbPath);
    database.Initialize();

    var info = database.GetRunInfo();
    var firstStart = info.firstStart?.ToString("o") ?? "n/a";
    var lastEnd = info.lastEnd?.ToString("o") ?? "n/a";

    Console.WriteLine($"Runs: {info.runCount}");
    Console.WriteLine($"First start: {firstStart}");
    Console.WriteLine($"Last end: {lastEnd}");
    return 0;
}

static int HandleReport(string[] args)
{
    var options = ParseOptions(args);
    var dbPath = options.GetValueOrDefault("db") ?? GetDefaultDatabasePath();

    if (!File.Exists(dbPath))
    {
        Console.WriteLine($"Error: Database file not found: {dbPath}");
        Console.WriteLine();
        Console.WriteLine("To fix this:");
        Console.WriteLine("  1. Check the path is correct");
        Console.WriteLine("  2. Run 'blackpaw start' first to create a database");
        Console.WriteLine("  3. Or specify a different database with --db <path>");
        return 1;
    }

    var database = new DatabaseService(dbPath);
    database.Initialize();

    // Check for --all flag
    var generateAll = options.ContainsKey("all");
    var outputDir = ValidateOutputPath(options.GetValueOrDefault("output-dir") ?? ".");

    if (generateAll)
    {
        var runs = database.GetRuns();
        if (runs.Count == 0)
        {
            Console.WriteLine("No runs found in database.");
            return 0;
        }

        Console.WriteLine($"Generating reports for {runs.Count} runs...");
        foreach (var run in runs)
        {
            try
            {
                var data = ReportGenerator.LoadReportData(database, run.RunId);
                var html = ReportGenerator.GenerateHtml(data);
                var outputPath = Path.Combine(outputDir, $"report-{run.RunId}-{SanitizeFilename(run.ScenarioName)}.html");
                File.WriteAllText(outputPath, html);
                Console.WriteLine($"  Generated: {outputPath}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  Error generating report for run {run.RunId}: {ex.Message}");
            }
        }
        Console.WriteLine("Done.");
        return 0;
    }

    // Single run report
    if (!options.TryGetValue("run", out var runIdStr) || !long.TryParse(runIdStr, out var runId))
    {
        Console.WriteLine("Error: Missing or invalid run ID.");
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine("  blackpaw report --db <path> --run <id>    Generate report for a specific run");
        Console.WriteLine("  blackpaw report --db <path> --all         Generate reports for all runs");
        Console.WriteLine();
        Console.WriteLine("To find run IDs: blackpaw list-runs --db <path>");
        return 2;
    }

    try
    {
        var data = ReportGenerator.LoadReportData(database, runId);
        var html = ReportGenerator.GenerateHtml(data);

        var outputPath = ValidateOutputPath(options.GetValueOrDefault("output") ?? $"report-{runId}-{SanitizeFilename(data.Run.ScenarioName)}.html");
        File.WriteAllText(outputPath, html);
        Console.WriteLine($"Report generated: {outputPath}");
        return 0;
    }
    catch (ArgumentException ex)
    {
        Console.WriteLine(ex.Message);
        return 1;
    }
}

static int HandleCompare(string[] args)
{
    var options = ParseOptions(args);
    var dbPath = options.GetValueOrDefault("db") ?? GetDefaultDatabasePath();

    if (!File.Exists(dbPath))
    {
        Console.WriteLine($"Error: Database file not found: {dbPath}");
        Console.WriteLine();
        Console.WriteLine("Run 'blackpaw start' first to create a database with run data.");
        return 1;
    }

    var database = new DatabaseService(dbPath);
    database.Initialize();

    var runs = database.GetRuns().Where(r => r.EndedAtUtc.HasValue).ToList();
    if (runs.Count < 2)
    {
        Console.WriteLine("Error: At least 2 completed runs are required for comparison.");
        Console.WriteLine($"Found {runs.Count} completed run(s).");
        return 1;
    }

    long baselineId;
    long targetId;

    // Check if baseline/target are specified via CLI
    var hasBaseline = options.TryGetValue("baseline", out var baselineStr) && long.TryParse(baselineStr, out baselineId);
    var hasTarget = options.TryGetValue("target", out var targetStr) && long.TryParse(targetStr, out targetId);

    if (!hasBaseline || !hasTarget)
    {
        // Interactive selection mode
        if (!AnsiConsole.Profile.Capabilities.Interactive)
        {
            Console.WriteLine("Error: --baseline and --target are required in non-interactive mode.");
            Console.WriteLine();
            Console.WriteLine("Usage: blackpaw compare --db <path> --baseline <id> --target <id>");
            return 2;
        }

        var selectedRuns = PromptForRunSelection(runs);
        if (selectedRuns == null)
        {
            AnsiConsole.MarkupLine("[yellow]Cancelled.[/]");
            return 0;
        }

        baselineId = selectedRuns.Value.baseline;
        targetId = selectedRuns.Value.target;
    }
    else
    {
        baselineId = long.Parse(baselineStr!);
        targetId = long.Parse(targetStr!);
    }

    // Validate run IDs
    var baselineRun = runs.FirstOrDefault(r => r.RunId == baselineId);
    var targetRun = runs.FirstOrDefault(r => r.RunId == targetId);

    if (baselineRun == null)
    {
        Console.WriteLine($"Error: Baseline run {baselineId} not found.");
        return 1;
    }
    if (targetRun == null)
    {
        Console.WriteLine($"Error: Target run {targetId} not found.");
        return 1;
    }

    // Load data and compare
    AnsiConsole.MarkupLine("[dim]Loading run data...[/]");
    var baselineData = ReportGenerator.LoadReportData(database, baselineId);
    var targetData = ReportGenerator.LoadReportData(database, targetId);

    AnsiConsole.MarkupLine("[dim]Analyzing comparison...[/]");
    var comparison = RunComparison.Compare(baselineData, targetData);

    // Display CLI summary
    PrintComparisonSummary(comparison, baselineRun, targetRun);

    // Generate HTML report
    var outputPath = options.GetValueOrDefault("output")
        ?? $"comparison-{baselineId}-vs-{targetId}.html";
    outputPath = ValidateOutputPath(outputPath);

    AnsiConsole.MarkupLine("[dim]Generating HTML report...[/]");
    var html = ComparisonReportGenerator.GenerateHtml(comparison, baselineData, targetData);
    File.WriteAllText(outputPath, html);
    AnsiConsole.MarkupLine($"[green]Report saved:[/] [link]{outputPath}[/]");
    return 0;
}

static (long baseline, long target)? PromptForRunSelection(List<RunRecord> runs)
{
    AnsiConsole.Write(new Rule("[yellow]Compare Runs[/]").RuleStyle("grey"));
    AnsiConsole.WriteLine();

    // Show runs table
    var table = new Table();
    table.AddColumn("ID");
    table.AddColumn("Scenario");
    table.AddColumn("Started");
    table.AddColumn("Duration");
    table.AddColumn("Notes");
    table.Border(TableBorder.Rounded);

    foreach (var run in runs.OrderBy(r => r.StartedAtUtc))
    {
        var duration = run.DurationSeconds.HasValue
            ? FormatDuration(run.DurationSeconds.Value)
            : "—";
        table.AddRow(
            run.RunId.ToString(),
            run.ScenarioName,
            run.StartedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm"),
            duration,
            run.Notes ?? "[dim](none)[/]");
    }

    AnsiConsole.Write(table);
    AnsiConsole.WriteLine();

    // Build selection choices
    var choices = runs
        .OrderBy(r => r.StartedAtUtc)
        .Select(r => $"{r.RunId} - {r.ScenarioName} ({r.StartedAtUtc.ToLocalTime():MM/dd HH:mm})")
        .ToList();

    // Select baseline
    var baselineChoice = AnsiConsole.Prompt(
        new SelectionPrompt<string>()
            .Title("[green]Select BASELINE run[/] (the \"before\" run):")
            .AddChoices(choices.Append("Cancel")));

    if (baselineChoice == "Cancel") return null;
    var baselineId = long.Parse(baselineChoice.Split(' ')[0]);

    // Select target (exclude baseline)
    var targetChoices = choices.Where(c => !c.StartsWith($"{baselineId} ")).ToList();
    var targetChoice = AnsiConsole.Prompt(
        new SelectionPrompt<string>()
            .Title("[green]Select TARGET run[/] (the \"after\" run to compare against baseline):")
            .AddChoices(targetChoices.Append("Cancel")));

    if (targetChoice == "Cancel") return null;
    var targetId = long.Parse(targetChoice.Split(' ')[0]);

    // Confirm
    AnsiConsole.WriteLine();
    var baselineRun = runs.First(r => r.RunId == baselineId);
    var targetRun = runs.First(r => r.RunId == targetId);

    AnsiConsole.MarkupLine($"[dim]Baseline:[/] #{baselineId} \"{baselineRun.ScenarioName}\"");
    AnsiConsole.MarkupLine($"[dim]Target:[/]   #{targetId} \"{targetRun.ScenarioName}\"");
    AnsiConsole.WriteLine();

    if (!AnsiConsole.Confirm("Generate comparison?", defaultValue: true))
    {
        return null;
    }

    return (baselineId, targetId);
}

static void PrintComparisonSummary(RunComparison comparison, RunRecord baselineRun, RunRecord targetRun)
{
    AnsiConsole.WriteLine();

    // Header
    var headerPanel = new Panel(
        $"[dim]Baseline:[/] #{comparison.Baseline.RunId} \"{baselineRun.ScenarioName}\" ({baselineRun.StartedAtUtc.ToLocalTime():yyyy-MM-dd HH:mm})\n" +
        $"[dim]Target:[/]   #{comparison.Target.RunId} \"{targetRun.ScenarioName}\" ({targetRun.StartedAtUtc.ToLocalTime():yyyy-MM-dd HH:mm})")
        .Header("[bold]Run Comparison[/]")
        .BorderColor(Color.Grey);
    AnsiConsole.Write(headerPanel);
    AnsiConsole.WriteLine();

    // Verdict
    var verdictColor = comparison.Result.Verdict switch
    {
        ComparisonVerdict.Improved => "green",
        ComparisonVerdict.MostlyImproved => "green",
        ComparisonVerdict.Mixed => "yellow",
        ComparisonVerdict.MostlyRegressed => "red",
        ComparisonVerdict.Regressed => "red",
        _ => "white"
    };
    var verdictText = comparison.Result.Verdict switch
    {
        ComparisonVerdict.Improved => "IMPROVED",
        ComparisonVerdict.MostlyImproved => "MOSTLY IMPROVED",
        ComparisonVerdict.Mixed => "MIXED RESULTS",
        ComparisonVerdict.MostlyRegressed => "MOSTLY REGRESSED",
        ComparisonVerdict.Regressed => "REGRESSED",
        _ => "UNKNOWN"
    };

    AnsiConsole.MarkupLine($"[{verdictColor} bold]{verdictText}[/]");
    AnsiConsole.MarkupLine($"[dim]{comparison.Result.ImprovedCount} improved, {comparison.Result.RegressedCount} regressed, {comparison.Result.NeutralCount} unchanged[/]");
    AnsiConsole.WriteLine();

    // System metrics table
    var sysTable = new Table();
    sysTable.Title("[bold]System Metrics[/]");
    sysTable.AddColumn("Metric");
    sysTable.AddColumn(new TableColumn("Baseline").RightAligned());
    sysTable.AddColumn(new TableColumn("Target").RightAligned());
    sysTable.AddColumn("Change");
    sysTable.Border(TableBorder.Simple);

    AddComparisonRow(sysTable, comparison.Result.System.CpuAvg, "%");
    AddComparisonRow(sysTable, comparison.Result.System.CpuP95, "%");
    AddComparisonRow(sysTable, comparison.Result.System.MemoryAvgMb, "MB");
    AddComparisonRow(sysTable, comparison.Result.System.MemoryMaxMb, "MB");

    AnsiConsole.Write(sysTable);
    AnsiConsole.WriteLine();

    // Process metrics (if any)
    var processesWithData = comparison.Result.Processes.Where(p => p.Status == ComparisonStatus.Present).ToList();
    if (processesWithData.Count > 0)
    {
        var procTable = new Table();
        procTable.Title("[bold]Process Metrics[/]");
        procTable.AddColumn("Process");
        procTable.AddColumn("Metric");
        procTable.AddColumn(new TableColumn("Baseline").RightAligned());
        procTable.AddColumn(new TableColumn("Target").RightAligned());
        procTable.AddColumn("Change");
        procTable.Border(TableBorder.Simple);

        foreach (var proc in processesWithData.Take(5)) // Limit to top 5
        {
            if (proc.CpuAvg != null)
            {
                procTable.AddRow(
                    proc.ProcessName,
                    "CPU (avg)",
                    $"{proc.CpuAvg.BaselineValue:N1}%",
                    $"{proc.CpuAvg.TargetValue:N1}%",
                    FormatChange(proc.CpuAvg));
            }
        }

        AnsiConsole.Write(procTable);
        AnsiConsole.WriteLine();
    }

    // HTTP endpoints (if any)
    var endpointsWithData = comparison.Result.HttpEndpoints.Where(e => e.Status == ComparisonStatus.Present).ToList();
    if (endpointsWithData.Count > 0)
    {
        var httpTable = new Table();
        httpTable.Title("[bold]HTTP Endpoints (p95 Latency)[/]");
        httpTable.AddColumn("Endpoint");
        httpTable.AddColumn(new TableColumn("Baseline").RightAligned());
        httpTable.AddColumn(new TableColumn("Target").RightAligned());
        httpTable.AddColumn("Change");
        httpTable.Border(TableBorder.Simple);

        foreach (var ep in endpointsWithData.Take(10)) // Limit to top 10
        {
            if (ep.LatencyP95 != null)
            {
                httpTable.AddRow(
                    TruncateEndpoint(ep.Endpoint, 30),
                    $"{ep.BaselineLatencyP95:N1}ms",
                    $"{ep.TargetLatencyP95:N1}ms",
                    FormatChange(ep.LatencyP95));
            }
        }

        AnsiConsole.Write(httpTable);
        AnsiConsole.WriteLine();
    }

    // Key changes
    if (comparison.Result.KeyImprovements.Count > 0)
    {
        AnsiConsole.MarkupLine("[green bold]Key Improvements:[/]");
        foreach (var improvement in comparison.Result.KeyImprovements.Take(5))
        {
            AnsiConsole.MarkupLine($"  [green]•[/] {improvement}");
        }
        AnsiConsole.WriteLine();
    }

    if (comparison.Result.KeyRegressions.Count > 0)
    {
        AnsiConsole.MarkupLine("[red bold]Watch:[/]");
        foreach (var regression in comparison.Result.KeyRegressions.Take(5))
        {
            AnsiConsole.MarkupLine($"  [red]•[/] {regression}");
        }
        AnsiConsole.WriteLine();
    }
}

static void AddComparisonRow(Table table, MetricComparison metric, string unit)
{
    table.AddRow(
        metric.Name,
        $"{metric.BaselineValue:N1}{unit}",
        $"{metric.TargetValue:N1}{unit}",
        FormatChange(metric));
}

static string FormatChange(MetricComparison metric)
{
    var changeText = metric.FormatChange();
    var icon = metric.GetIcon();
    var color = metric.Direction switch
    {
        ChangeDirection.Improved => "green",
        ChangeDirection.Regressed => "red",
        _ => "dim"
    };
    return $"[{color}]{changeText} {icon}[/]";
}

static string FormatDuration(double seconds)
{
    if (seconds < 60) return $"{seconds:N0}s";
    if (seconds < 3600) return $"{seconds / 60:N0}m {seconds % 60:N0}s";
    return $"{seconds / 3600:N0}h {(seconds % 3600) / 60:N0}m";
}

static string TruncateEndpoint(string endpoint, int maxLength)
{
    return endpoint.Length <= maxLength ? endpoint : endpoint[..(maxLength - 3)] + "...";
}

static string SanitizeFilename(string name)
{
    var invalid = Path.GetInvalidFileNameChars();
    return string.Join("_", name.Split(invalid, StringSplitOptions.RemoveEmptyEntries)).Trim();
}

static string ValidateOutputPath(string path)
{
    // Validate and normalize path to prevent directory traversal
    if (string.IsNullOrWhiteSpace(path))
    {
        throw new ArgumentException("Output path cannot be empty");
    }

    // Get full path to resolve any relative path components
    var fullPath = Path.GetFullPath(path);

    // Validate the path doesn't contain invalid characters
    if (fullPath.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
    {
        throw new ArgumentException("Output path contains invalid characters");
    }

    return fullPath;
}

static Dictionary<string, string?> ParseOptions(string[] args)
{
    var options = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
    for (var i = 0; i < args.Length; i++)
    {
        var arg = args[i];
        if (!arg.StartsWith("--"))
        {
            continue;
        }

        var key = arg[2..];
        string? value = null;
        if (i + 1 < args.Length && !args[i + 1].StartsWith("--"))
        {
            value = args[i + 1];
            i++;
        }

        options[key] = value;
    }

    return options;
}

static string GetVersion()
{
    return Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "1.0.0";
}

/// <summary>
/// Gets the default database path (blackpaw.db in the executable's directory).
/// Uses AppContext.BaseDirectory which works correctly for single-file publish.
/// </summary>
static string GetDefaultDatabasePath()
{
    return Path.Combine(AppContext.BaseDirectory, "blackpaw.db");
}

static async Task<int> RunInteractiveModeAsync()
{
    var action = InteractivePrompter.PromptMainMenu();

    return action switch
    {
        InteractiveAction.StartCapture => await RunInteractiveCaptureAsync(),
        InteractiveAction.GenerateReport => RunInteractiveReport(),
        _ => 0
    };
}

static async Task<int> RunInteractiveCaptureAsync()
{
    try
    {
        var options = InteractivePrompter.PromptForStartOptions();
        if (options == null) return 0;

        // Load config file if specified
        var config = AppConfig.Load(options.ConfigPath);

        // Apply interactive options
        config.DatabasePath = options.DatabasePath;
        config.SampleIntervalSeconds = options.SampleIntervalSeconds;
        if (options.ProcessNames.Count > 0)
        {
            config.ProcessNames = options.ProcessNames;
        }

        // Apply deep monitoring config if specified
        if (options.DeepMonitoring != null)
        {
            config.DeepMonitoring = options.DeepMonitoring;
        }

        var database = new DatabaseService(config.DatabasePath);
        database.Initialize();

        var run = new RunRecord
        {
            ScenarioName = options.Scenario,
            Notes = options.Notes,
            StartedAtUtc = DateTime.UtcNow,
            ProbeVersion = GetVersion(),
            ConfigSnapshot = config.ToJson(),
            WorkloadType = options.WorkloadType,
            WorkloadSizeEstimate = options.WorkloadSize,
            WorkloadNotes = options.WorkloadNotes
        };

        HostInfoCollector.PopulateHostFields(run);
        var runId = database.InsertRun(run);

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine($"[green]Starting Blackpaw run {runId}[/] for scenario '[blue]{options.Scenario}[/]'");
        AnsiConsole.MarkupLine($"[dim]Database:[/] {config.DatabasePath}");
        AnsiConsole.MarkupLine($"[dim]Sample interval:[/] {config.SampleIntervalSeconds}s");
        if (config.ProcessNames.Count > 0)
        {
            AnsiConsole.MarkupLine($"[dim]Tracking processes:[/] {string.Join(", ", config.ProcessNames)}");
        }
        AnsiConsole.MarkupLine("[yellow]Press Ctrl+C to stop.[/]");
        AnsiConsole.WriteLine();

        DbSnapshotCollector? dbCollector = null;
        if (config.EnableDbCounters && !string.IsNullOrWhiteSpace(config.DbConnectionString))
        {
            dbCollector = new DbSnapshotCollector(config.DbConnectionString!);
            var startSnapshot = await dbCollector.CaptureAsync(runId, "start", CancellationToken.None);
            if (startSnapshot != null)
            {
                database.InsertDbSnapshot(startSnapshot);
            }
        }

        using var samplingSession = new SamplingSession(database, config);
        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cts.Cancel();
        };

        var start = DateTime.UtcNow;
        try
        {
            await samplingSession.RunAsync(runId, cts.Token);
        }
        catch (OperationCanceledException)
        {
            // expected when Ctrl+C is pressed
        }

        var end = DateTime.UtcNow;
        var duration = (end - start).TotalSeconds;
        database.UpdateRunEnd(runId, end, duration);

        if (dbCollector != null)
        {
            var endSnapshot = await dbCollector.CaptureAsync(runId, "end", CancellationToken.None);
            if (endSnapshot != null)
            {
                database.InsertDbSnapshot(endSnapshot);
            }
        }

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine($"[green]Run {runId} finished[/] after [blue]{duration:N1}s[/]. Data stored in [dim]{config.DatabasePath}[/].");

        // Auto-generate HTML report
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[dim]Generating HTML report...[/]");
        try
        {
            var reportData = ReportGenerator.LoadReportData(database, runId);
            var html = ReportGenerator.GenerateHtml(reportData);
            var reportPath = $"report-{runId}-{SanitizeFilename(options.Scenario)}.html";
            File.WriteAllText(reportPath, html);
            AnsiConsole.MarkupLine($"[green]Report generated:[/] [link]{reportPath}[/]");
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[yellow]Could not generate report: {ex.Message}[/]");
        }
        return 0;
    }
    catch (OperationCanceledException)
    {
        AnsiConsole.MarkupLine("\n[yellow]Cancelled.[/]");
        return 0;
    }
}

static int RunInteractiveReport()
{
    var options = InteractivePrompter.PromptForReportOptions();
    if (options == null) return 0;

    var database = new DatabaseService(options.DatabasePath);
    database.Initialize();

    if (options.GenerateAll)
    {
        var runs = database.GetRuns();
        var outputDir = options.OutputPath ?? ".";

        AnsiConsole.MarkupLine($"[dim]Generating reports for {runs.Count} runs...[/]");

        foreach (var run in runs)
        {
            try
            {
                var data = ReportGenerator.LoadReportData(database, run.RunId);
                var html = ReportGenerator.GenerateHtml(data);
                var outputPath = Path.Combine(outputDir, $"report-{run.RunId}-{SanitizeFilename(run.ScenarioName)}.html");
                File.WriteAllText(outputPath, html);
                AnsiConsole.MarkupLine($"  [green]Generated:[/] {outputPath}");
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"  [red]Error for run {run.RunId}:[/] {ex.Message}");
            }
        }

        AnsiConsole.MarkupLine("[green]Done.[/]");
    }
    else if (options.RunId.HasValue)
    {
        try
        {
            var data = ReportGenerator.LoadReportData(database, options.RunId.Value);
            var html = ReportGenerator.GenerateHtml(data);
            var outputPath = options.OutputPath ?? $"report-{options.RunId}-{SanitizeFilename(data.Run.ScenarioName)}.html";
            File.WriteAllText(outputPath, html);
            AnsiConsole.MarkupLine($"[green]Report generated:[/] [link]{outputPath}[/]");
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Error:[/] {ex.Message}");
        }
    }
    return 0;
}

static void PrintLogo()
{
    var logo = LoadEmbeddedResource("Blackpaw.App.Resources.Logo.txt");
    Console.WriteLine(logo);
}

static void PrintUsage()
{
    var helpText = LoadEmbeddedResource("Blackpaw.App.Resources.HelpMessage.txt")
        .Replace("{{VERSION}}", GetVersion());
    Console.WriteLine(helpText);
}

static string LoadEmbeddedResource(string resourceName)
{
    var assembly = Assembly.GetExecutingAssembly();
    using var stream = assembly.GetManifestResourceStream(resourceName);
    if (stream == null)
    {
        return $"Help text not available (resource '{resourceName}' not found).";
    }
    using var reader = new StreamReader(stream);
    return reader.ReadToEnd();
}
