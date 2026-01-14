using System.Text.Json;
using System.Text.Json.Serialization;

namespace Blackpaw.Configuration;

public class DeepMonitoringConfig
{
    public List<DotNetAppConfig> DotNetCoreApps { get; set; } = new();
    public List<DotNetAppConfig> DotNetFrameworkApps { get; set; } = new();
    public SqlDmvSamplingConfig SqlDmvSampling { get; set; } = new();
}

public class DotNetAppConfig
{
    public string Name { get; set; } = string.Empty;
    public string ProcessName { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
    public DotNetHttpMonitoringConfig HttpMonitoring { get; set; } = new();
}

public class DotNetHttpMonitoringConfig
{
    public bool Enabled { get; set; }
    public string EndpointGrouping { get; set; } = "HostOnly";
    public double BucketIntervalSeconds { get; set; } = 5;
}

public class SqlDmvSamplingConfig
{
    public bool Enabled { get; set; }
    public double SampleIntervalSeconds { get; set; } = 5;
}

public class AppConfig
{
    /// <summary>
    /// Default database path is in the executable's directory.
    /// </summary>
    private static string DefaultDatabasePath => Path.Combine(AppContext.BaseDirectory, "blackpaw.db");

    /// <summary>
    /// Default config path is in the executable's directory.
    /// </summary>
    private static string DefaultConfigPath => Path.Combine(AppContext.BaseDirectory, "config.json");

    public string DatabasePath { get; set; } = DefaultDatabasePath;
    public double SampleIntervalSeconds { get; set; } = 1;
    public List<string> ProcessNames { get; set; } = new();
    public bool EnableDbCounters { get; set; }
    public string? DbConnectionString { get; set; }
    public bool EnableDiskMetrics { get; set; } = true;
    public bool EnableNetworkMetrics { get; set; }
    public string? SqlConnectionString { get; set; }
    public DeepMonitoringConfig DeepMonitoring { get; set; } = new();

    public static AppConfig Load(string? configPath)
    {
        var config = new AppConfig();

        if (!string.IsNullOrWhiteSpace(configPath) && File.Exists(configPath))
        {
            var json = File.ReadAllText(configPath);
            var fromFile = JsonSerializer.Deserialize<AppConfig>(json, JsonSerializerOptionsFactory());
            if (fromFile != null)
            {
                config = Merge(config, fromFile);
            }
        }
        else if (!string.IsNullOrWhiteSpace(configPath))
        {
            Console.WriteLine($"Config file '{configPath}' not found. Using defaults.");
        }
        else if (File.Exists(DefaultConfigPath))
        {
            // Look for config.json in the executable's directory
            var json = File.ReadAllText(DefaultConfigPath);
            var fromFile = JsonSerializer.Deserialize<AppConfig>(json, JsonSerializerOptionsFactory());
            if (fromFile != null)
            {
                config = Merge(config, fromFile);
            }
        }

        return config;
    }

    public static AppConfig Merge(AppConfig baseline, AppConfig overrides)
    {
        var merged = new AppConfig
        {
            DatabasePath = string.IsNullOrWhiteSpace(overrides.DatabasePath) ? baseline.DatabasePath : overrides.DatabasePath,
            SampleIntervalSeconds = overrides.SampleIntervalSeconds <= 0 ? baseline.SampleIntervalSeconds : overrides.SampleIntervalSeconds,
            ProcessNames = overrides.ProcessNames.Count == 0 ? baseline.ProcessNames : overrides.ProcessNames,
            EnableDbCounters = overrides.EnableDbCounters || baseline.EnableDbCounters,
            DbConnectionString = overrides.DbConnectionString ?? baseline.DbConnectionString,
            SqlConnectionString = overrides.SqlConnectionString ?? baseline.SqlConnectionString,
            EnableDiskMetrics = overrides.EnableDiskMetrics || baseline.EnableDiskMetrics,
            EnableNetworkMetrics = overrides.EnableNetworkMetrics || baseline.EnableNetworkMetrics,
            DeepMonitoring = MergeDeepMonitoring(baseline.DeepMonitoring, overrides.DeepMonitoring)
        };

        return merged;
    }

    public string ToJson() => JsonSerializer.Serialize(this, JsonSerializerOptionsFactory());

    private static JsonSerializerOptions JsonSerializerOptionsFactory() => new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static DeepMonitoringConfig MergeDeepMonitoring(DeepMonitoringConfig baseline, DeepMonitoringConfig overrides)
    {
        return new DeepMonitoringConfig
        {
            DotNetCoreApps = overrides.DotNetCoreApps.Count == 0 ? baseline.DotNetCoreApps : overrides.DotNetCoreApps,
            DotNetFrameworkApps = overrides.DotNetFrameworkApps.Count == 0 ? baseline.DotNetFrameworkApps : overrides.DotNetFrameworkApps,
            SqlDmvSampling = new SqlDmvSamplingConfig
            {
                Enabled = overrides.SqlDmvSampling.Enabled || baseline.SqlDmvSampling.Enabled,
                SampleIntervalSeconds = overrides.SqlDmvSampling.SampleIntervalSeconds > 0 ? overrides.SqlDmvSampling.SampleIntervalSeconds : baseline.SqlDmvSampling.SampleIntervalSeconds
            }
        };
    }
}
