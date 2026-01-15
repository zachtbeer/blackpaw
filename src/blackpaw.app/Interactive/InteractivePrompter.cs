using Spectre.Console;
using Blackpaw.Configuration;
using Blackpaw.Diagnostics;
using System.Diagnostics;

namespace Blackpaw.Interactive;

public enum InteractiveAction
{
    StartCapture,
    CompareRuns,
    GenerateReport,
    Exit
}

public class InteractiveStartOptions
{
    public required string Scenario { get; init; }
    public string? Notes { get; init; }
    public string? ConfigPath { get; init; }
    public string DatabasePath { get; init; } = null!; // Set by prompter using DefaultDatabasePath
    public List<string> ProcessNames { get; init; } = [];
    public double SampleIntervalSeconds { get; init; } = 1.0;
    public string? WorkloadType { get; init; }
    public int? WorkloadSize { get; init; }
    public string? WorkloadNotes { get; init; }
    public DeepMonitoringConfig? DeepMonitoring { get; init; }
    public string? SqlConnectionString { get; init; }
}

public class InteractiveReportOptions
{
    public required string DatabasePath { get; init; }
    public long? RunId { get; init; }
    public bool GenerateAll { get; init; }
    public string? OutputPath { get; init; }
}

public static class InteractivePrompter
{
    /// <summary>
    /// Default database path is in the executable's directory.
    /// </summary>
    private static string DefaultDatabasePath => Path.Combine(AppContext.BaseDirectory, "blackpaw.db");

    public static InteractiveAction PromptMainMenu()
    {
        // Check if we're in an interactive terminal
        if (!AnsiConsole.Profile.Capabilities.Interactive)
        {
            AnsiConsole.MarkupLine("[red]Interactive mode requires a terminal. Use --help for CLI usage.[/]");
            return InteractiveAction.Exit;
        }

        // Welcome banner
        AnsiConsole.Write(new FigletText("Blackpaw").Color(Color.Blue));
        AnsiConsole.MarkupLine("[dim]Scenario-based performance capture[/]");
        AnsiConsole.WriteLine();

        var choice = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("[green]What would you like to do?[/]")
                .AddChoices(
                    "Start a new capture",
                    "Compare two captures",
                    "Export a capture report",
                    "Exit"));

        return choice switch
        {
            "Start a new capture" => InteractiveAction.StartCapture,
            "Compare two captures" => InteractiveAction.CompareRuns,
            "Export a capture report" => InteractiveAction.GenerateReport,
            _ => InteractiveAction.Exit
        };
    }

    public static InteractiveReportOptions? PromptForReportOptions()
    {
        AnsiConsole.Write(new Rule("[yellow]Generate Report[/]").RuleStyle("grey"));

        var dbPath = AnsiConsole.Prompt(
            new TextPrompt<string>("[green]Database path[/]:")
                .DefaultValue(DefaultDatabasePath)
                .Validate(path => File.Exists(path)
                    ? ValidationResult.Success()
                    : ValidationResult.Error($"[red]Database not found: {path}[/]")));

        // Load runs from database to show options
        var database = new Blackpaw.Data.DatabaseService(dbPath);
        database.Initialize();
        var runs = database.GetRuns();

        if (runs.Count == 0)
        {
            AnsiConsole.MarkupLine("[yellow]No runs found in database.[/]");
            return null;
        }

        // Show available runs
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine($"[dim]Found {runs.Count} run(s) in database:[/]");

        var runTable = new Table();
        runTable.AddColumn("ID");
        runTable.AddColumn("Scenario");
        runTable.AddColumn("Started");
        runTable.AddColumn("Status");
        runTable.Border(TableBorder.Rounded);

        foreach (var run in runs.Take(10))
        {
            var status = run.EndedAtUtc.HasValue ? "[green]completed[/]" : "[yellow]running[/]";
            runTable.AddRow(
                run.RunId.ToString(),
                run.ScenarioName,
                run.StartedAtUtc.ToString("yyyy-MM-dd HH:mm"),
                status);
        }
        if (runs.Count > 10)
        {
            runTable.AddRow("[dim]...[/]", $"[dim]and {runs.Count - 10} more[/]", "", "");
        }
        AnsiConsole.Write(runTable);
        AnsiConsole.WriteLine();

        var reportChoice = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("[green]Which runs to report?[/]")
                .AddChoices(
                    "Generate report for a specific run",
                    "Generate reports for all runs",
                    "Cancel"));

        if (reportChoice == "Cancel")
        {
            return null;
        }

        if (reportChoice == "Generate reports for all runs")
        {
            var outputDir = AnsiConsole.Prompt(
                new TextPrompt<string>("[green]Output directory[/]:")
                    .DefaultValue(".")
                    .AllowEmpty());

            return new InteractiveReportOptions
            {
                DatabasePath = dbPath,
                GenerateAll = true,
                OutputPath = string.IsNullOrWhiteSpace(outputDir) ? "." : outputDir
            };
        }

        // Specific run
        var runIdStr = AnsiConsole.Prompt(
            new TextPrompt<string>("[green]Run ID[/]:")
                .Validate(input =>
                {
                    if (!long.TryParse(input, out var id))
                        return ValidationResult.Error("[red]Please enter a valid run ID[/]");
                    if (!runs.Any(r => r.RunId == id))
                        return ValidationResult.Error($"[red]Run {id} not found[/]");
                    return ValidationResult.Success();
                }));

        var runId = long.Parse(runIdStr);
        var selectedRun = runs.First(r => r.RunId == runId);

        var outputPath = AnsiConsole.Prompt(
            new TextPrompt<string>("[green]Output file path[/]:")
                .DefaultValue($"report-{runId}-{SanitizeFilename(selectedRun.ScenarioName)}.html")
                .AllowEmpty());

        return new InteractiveReportOptions
        {
            DatabasePath = dbPath,
            RunId = runId,
            GenerateAll = false,
            OutputPath = string.IsNullOrWhiteSpace(outputPath) ? null : outputPath
        };
    }

    private static string SanitizeFilename(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return string.Join("_", name.Split(invalid, StringSplitOptions.RemoveEmptyEntries)).Trim();
    }

    private static string MaskConnectionString(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            return "[dim](none)[/]";

        try
        {
            var builder = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(connectionString);
            var server = builder.DataSource;
            if (!string.IsNullOrWhiteSpace(server))
            {
                return $"{server} [dim](credentials masked)[/]";
            }
        }
        catch
        {
            // Invalid connection string format
        }

        return "[dim](configured)[/]";
    }

    public static InteractiveStartOptions? PromptForStartOptions()
    {

        // Phase 1: Essential settings
        AnsiConsole.Write(new Rule("[yellow]Basic Settings[/]").RuleStyle("grey"));

        var defaultScenario = DateTime.Now.ToString("yy-MM-dd HH:mm:ss");
        var scenario = AnsiConsole.Prompt(
            new TextPrompt<string>("[green]Scenario name[/]:")
                .PromptStyle("blue")
                .DefaultValue(defaultScenario));

        var dbPath = AnsiConsole.Prompt(
            new TextPrompt<string>("[green]Database path[/]:")
                .DefaultValue(DefaultDatabasePath)
                .AllowEmpty());
        if (string.IsNullOrWhiteSpace(dbPath)) dbPath = DefaultDatabasePath;

        var configPath = AnsiConsole.Prompt(
            new TextPrompt<string>("[green]Config file path[/] (optional, press Enter to skip):")
                .AllowEmpty());

        var processInput = AnsiConsole.Prompt(
            new TextPrompt<string>("[green]Processes to monitor[/] (comma-separated, or Enter to skip):")
                .AllowEmpty());

        var processNames = string.IsNullOrWhiteSpace(processInput)
            ? new List<string>()
            : processInput.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

        // Validate process names against running processes
        if (processNames.Count > 0)
        {
            var runningProcesses = Process.GetProcesses().Select(p => p.ProcessName).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var name in processNames)
            {
                if (!runningProcesses.Contains(name))
                {
                    AnsiConsole.MarkupLine($"[yellow]Warning: Process '{name}' is not currently running[/]");
                }
            }
        }

        var sampleInterval = AnsiConsole.Prompt(
            new TextPrompt<double>("[green]Sample interval[/] (seconds):")
                .DefaultValue(1.0)
                .Validate(val => val > 0
                    ? ValidationResult.Success()
                    : ValidationResult.Error("[red]Must be greater than 0[/]")));

        var notes = AnsiConsole.Prompt(
            new TextPrompt<string>("[green]Run notes[/] (optional):")
                .AllowEmpty());

        // Phase 2: Workload settings
        AnsiConsole.WriteLine();
        string? workloadType = null;
        int? workloadSize = null;
        string? workloadNotes = null;

        if (AnsiConsole.Confirm("Configure workload settings?", defaultValue: false))
        {
            AnsiConsole.Write(new Rule("[yellow]Workload Settings[/]").RuleStyle("grey"));

            workloadType = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("[green]Workload type[/]:")
                    .AddChoices("manual", "scripted", "prod_capture", "other", "(skip)"));

            if (workloadType == "(skip)") workloadType = null;

            var sizeInput = AnsiConsole.Prompt(
                new TextPrompt<string>("[green]Workload size estimate[/] (number, or Enter to skip):")
                    .AllowEmpty());

            if (!string.IsNullOrWhiteSpace(sizeInput) && int.TryParse(sizeInput, out var size))
            {
                workloadSize = size;
            }

            workloadNotes = AnsiConsole.Prompt(
                new TextPrompt<string>("[green]Workload notes[/] (optional):")
                    .AllowEmpty());
        }

        // Phase 3: Deep monitoring
        AnsiConsole.WriteLine();
        DeepMonitoringConfig? deepMonitoring = null;
        string? sqlConnectionString = null;

        if (AnsiConsole.Confirm("Configure deep monitoring? (.NET runtime, HTTP, SQL DMV)", defaultValue: false))
        {
            (deepMonitoring, sqlConnectionString) = PromptDeepMonitoring();
        }

        // Build options
        var options = new InteractiveStartOptions
        {
            Scenario = scenario,
            Notes = string.IsNullOrWhiteSpace(notes) ? null : notes,
            ConfigPath = string.IsNullOrWhiteSpace(configPath) ? null : configPath,
            DatabasePath = dbPath,
            ProcessNames = processNames,
            SampleIntervalSeconds = sampleInterval,
            WorkloadType = workloadType,
            WorkloadSize = workloadSize,
            WorkloadNotes = string.IsNullOrWhiteSpace(workloadNotes) ? null : workloadNotes,
            DeepMonitoring = deepMonitoring,
            SqlConnectionString = sqlConnectionString
        };

        // Summary
        AnsiConsole.WriteLine();
        AnsiConsole.Write(new Rule("[yellow]Summary[/]").RuleStyle("grey"));

        var table = new Table();
        table.AddColumn("Setting");
        table.AddColumn("Value");
        table.Border(TableBorder.Rounded);

        table.AddRow("Scenario", options.Scenario);
        table.AddRow("Database", options.DatabasePath);
        table.AddRow("Config file", options.ConfigPath ?? "[dim](none)[/]");
        table.AddRow("Processes", options.ProcessNames.Count > 0 ? string.Join(", ", options.ProcessNames) : "[dim](none)[/]");
        table.AddRow("Sample interval", $"{options.SampleIntervalSeconds}s");
        table.AddRow("Notes", options.Notes ?? "[dim](none)[/]");
        table.AddRow("Workload type", options.WorkloadType ?? "[dim](none)[/]");
        table.AddRow("Workload size", options.WorkloadSize?.ToString() ?? "[dim](none)[/]");

        if (options.DeepMonitoring != null)
        {
            var dmConfig = options.DeepMonitoring;
            if (dmConfig.DotNetCoreApps.Count > 0)
            {
                table.AddRow(".NET Core apps", string.Join(", ", dmConfig.DotNetCoreApps.Where(a => a.Enabled).Select(a => a.Name)));
            }
            if (dmConfig.DotNetFrameworkApps.Count > 0)
            {
                table.AddRow(".NET Framework apps", string.Join(", ", dmConfig.DotNetFrameworkApps.Where(a => a.Enabled).Select(a => a.Name)));
            }
            if (dmConfig.SqlDmvSampling.Enabled)
            {
                table.AddRow("SQL DMV sampling", $"every {dmConfig.SqlDmvSampling.SampleIntervalSeconds}s");
                table.AddRow("SQL connection", MaskConnectionString(options.SqlConnectionString));
            }
        }

        AnsiConsole.Write(table);
        AnsiConsole.WriteLine();

        if (!AnsiConsole.Confirm("Start capture with these settings?", defaultValue: true))
        {
            AnsiConsole.MarkupLine("[yellow]Cancelled.[/]");
            return null;
        }

        return options;
    }

    private static (DeepMonitoringConfig config, string? sqlConnectionString) PromptDeepMonitoring()
    {
        var config = new DeepMonitoringConfig();
        string? sqlConnectionString = null;

        AnsiConsole.Write(new Rule("[yellow]Deep Monitoring[/]").RuleStyle("grey"));

        // .NET Core apps
        if (AnsiConsole.Confirm("Monitor .NET Core applications?", defaultValue: false))
        {
            AnsiConsole.MarkupLine("[dim]Enter app names one at a time, or * to auto-detect all .NET Core processes[/]");

            var appName = AnsiConsole.Prompt(
                new TextPrompt<string>("[green].NET Core app name[/] (or * for auto-detect, Enter to skip):")
                    .AllowEmpty());

            if (appName == "*")
            {
                // Auto-detect .NET Core processes
                var coreProcesses = ProcessDetector.DetectDotNetCoreProcesses(out var accessDenied);
                if (coreProcesses.Count == 0)
                {
                    AnsiConsole.MarkupLine("[yellow]No .NET Core processes detected.[/]");
                    if (accessDenied > 0)
                    {
                        AnsiConsole.MarkupLine($"[dim]({accessDenied} processes could not be inspected - run as Administrator to detect more)[/]");
                    }
                }
                else
                {
                    AnsiConsole.MarkupLine($"[green]Found {coreProcesses.Count} .NET Core process(es):[/]");
                    if (accessDenied > 0)
                    {
                        AnsiConsole.MarkupLine($"[dim]({accessDenied} processes could not be inspected - run as Administrator to detect more)[/]");
                    }
                    var enableHttpAll = AnsiConsole.Confirm("Enable HTTP monitoring for all?", defaultValue: false);

                    foreach (var proc in coreProcesses)
                    {
                        var appConfig = new DotNetAppConfig
                        {
                            Name = proc.ProcessName,
                            ProcessName = proc.ProcessName,
                            Enabled = true,
                            HttpMonitoring = new DotNetHttpMonitoringConfig
                            {
                                Enabled = enableHttpAll,
                                BucketIntervalSeconds = 5
                            }
                        };
                        config.DotNetCoreApps.Add(appConfig);
                        AnsiConsole.MarkupLine($"  [green]Added:[/] {proc.ProcessName} (PID {proc.Id})");
                    }
                }
            }
            else if (!string.IsNullOrWhiteSpace(appName))
            {
                // Manual entry mode
                while (true)
                {
                    var processName = AnsiConsole.Prompt(
                        new TextPrompt<string>("[green]Process name[/]:")
                            .DefaultValue(appName.ToLowerInvariant()));

                    var enableHttp = AnsiConsole.Confirm("Enable HTTP request monitoring?", defaultValue: false);

                    var appConfig = new DotNetAppConfig
                    {
                        Name = appName,
                        ProcessName = processName,
                        Enabled = true,
                        HttpMonitoring = new DotNetHttpMonitoringConfig
                        {
                            Enabled = enableHttp,
                            BucketIntervalSeconds = 5
                        }
                    };

                    config.DotNetCoreApps.Add(appConfig);
                    AnsiConsole.MarkupLine($"[green]Added:[/] {appName} ({processName})");

                    appName = AnsiConsole.Prompt(
                        new TextPrompt<string>("[green].NET Core app name[/] (or Enter to finish):")
                            .AllowEmpty());

                    if (string.IsNullOrWhiteSpace(appName)) break;
                }
            }
        }

        // .NET Framework apps
        if (AnsiConsole.Confirm("Monitor .NET Framework applications?", defaultValue: false))
        {
            AnsiConsole.MarkupLine("[dim]Enter app names one at a time, or * to auto-detect all .NET Framework processes[/]");

            var appName = AnsiConsole.Prompt(
                new TextPrompt<string>("[green].NET Framework app name[/] (or * for auto-detect, Enter to skip):")
                    .AllowEmpty());

            if (appName == "*")
            {
                // Auto-detect .NET Framework processes
                var fxProcesses = ProcessDetector.DetectDotNetFrameworkProcesses(out var accessDenied);
                if (fxProcesses.Count == 0)
                {
                    AnsiConsole.MarkupLine("[yellow]No .NET Framework processes detected.[/]");
                    if (accessDenied > 0)
                    {
                        AnsiConsole.MarkupLine($"[dim]({accessDenied} processes could not be inspected - run as Administrator to detect more)[/]");
                    }
                }
                else
                {
                    AnsiConsole.MarkupLine($"[green]Found {fxProcesses.Count} .NET Framework process(es):[/]");
                    if (accessDenied > 0)
                    {
                        AnsiConsole.MarkupLine($"[dim]({accessDenied} processes could not be inspected - run as Administrator to detect more)[/]");
                    }

                    foreach (var proc in fxProcesses)
                    {
                        var appConfig = new DotNetAppConfig
                        {
                            Name = proc.ProcessName,
                            ProcessName = proc.ProcessName,
                            Enabled = true
                        };
                        config.DotNetFrameworkApps.Add(appConfig);
                        AnsiConsole.MarkupLine($"  [green]Added:[/] {proc.ProcessName} (PID {proc.Id})");
                    }
                }
            }
            else if (!string.IsNullOrWhiteSpace(appName))
            {
                // Manual entry mode
                while (true)
                {
                    var processName = AnsiConsole.Prompt(
                        new TextPrompt<string>("[green]Process name[/]:")
                            .DefaultValue(appName.ToLowerInvariant()));

                    var appConfig = new DotNetAppConfig
                    {
                        Name = appName,
                        ProcessName = processName,
                        Enabled = true
                    };

                    config.DotNetFrameworkApps.Add(appConfig);
                    AnsiConsole.MarkupLine($"[green]Added:[/] {appName} ({processName})");

                    appName = AnsiConsole.Prompt(
                        new TextPrompt<string>("[green].NET Framework app name[/] (or Enter to finish):")
                            .AllowEmpty());

                    if (string.IsNullOrWhiteSpace(appName)) break;
                }
            }
        }

        // SQL DMV sampling
        if (AnsiConsole.Confirm("Enable SQL Server DMV sampling?", defaultValue: false))
        {
            config.SqlDmvSampling.Enabled = true;

            sqlConnectionString = AnsiConsole.Prompt(
                new TextPrompt<string>("[green]SQL Server connection string[/]:")
                    .PromptStyle("blue")
                    .Validate(connStr =>
                    {
                        if (string.IsNullOrWhiteSpace(connStr))
                            return ValidationResult.Error("[red]Connection string is required for SQL DMV sampling[/]");
                        return ValidationResult.Success();
                    }));

            config.SqlDmvSampling.SampleIntervalSeconds = AnsiConsole.Prompt(
                new TextPrompt<double>("[green]SQL DMV sample interval[/] (seconds):")
                    .DefaultValue(5.0)
                    .Validate(val => val > 0
                        ? ValidationResult.Success()
                        : ValidationResult.Error("[red]Must be greater than 0[/]")));
        }

        return (config, sqlConnectionString);
    }

}
