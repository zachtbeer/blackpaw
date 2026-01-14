using Blackpaw.Configuration;
using Xunit;

namespace Blackpaw.Tests.Configuration;

public class AppConfigTests
{
    [Fact]
    public void Load_NoConfigFile_ReturnsDefaults()
    {
        // Arrange
        var nonExistentPath = Path.Combine(Path.GetTempPath(), $"nonexistent-{Guid.NewGuid()}.json");

        // Act
        var config = AppConfig.Load(nonExistentPath);

        // Assert - should have sensible defaults
        Assert.Equal(1, config.SampleIntervalSeconds);
        Assert.Empty(config.ProcessNames);
        Assert.True(config.EnableDiskMetrics);
        Assert.False(config.EnableNetworkMetrics);
    }

    [Fact]
    public void Load_ValidConfigFile_DeserializesCorrectly()
    {
        // Arrange
        var configPath = Path.Combine(Path.GetTempPath(), $"test-config-{Guid.NewGuid()}.json");
        var configJson = """
            {
                "SampleIntervalSeconds": 5,
                "ProcessNames": ["app1", "app2"],
                "EnableNetworkMetrics": true
            }
            """;
        File.WriteAllText(configPath, configJson);

        try
        {
            // Act
            var config = AppConfig.Load(configPath);

            // Assert
            Assert.Equal(5, config.SampleIntervalSeconds);
            Assert.Equal(2, config.ProcessNames.Count);
            Assert.Contains("app1", config.ProcessNames);
            Assert.Contains("app2", config.ProcessNames);
            // EnableDiskMetrics defaults to true and merge uses OR
            Assert.True(config.EnableDiskMetrics);
            Assert.True(config.EnableNetworkMetrics);
        }
        finally
        {
            File.Delete(configPath);
        }
    }

    [Fact]
    public void Merge_OverridesEmpty_UsesBaseline()
    {
        // Arrange
        var baseline = new AppConfig
        {
            SampleIntervalSeconds = 2,
            ProcessNames = new List<string> { "process1" },
            EnableDiskMetrics = true
        };
        var overrides = new AppConfig
        {
            SampleIntervalSeconds = 0, // Empty/default
            ProcessNames = new List<string>() // Empty
        };

        // Act
        var merged = AppConfig.Merge(baseline, overrides);

        // Assert - baseline values should be used when overrides are empty
        Assert.Equal(2, merged.SampleIntervalSeconds);
        Assert.Single(merged.ProcessNames);
        Assert.Contains("process1", merged.ProcessNames);
    }

    [Fact]
    public void Merge_OverridesPresent_UsesOverrides()
    {
        // Arrange
        var baseline = new AppConfig
        {
            SampleIntervalSeconds = 1,
            ProcessNames = new List<string> { "old" },
            DbConnectionString = "old-connection"
        };
        var overrides = new AppConfig
        {
            SampleIntervalSeconds = 5,
            ProcessNames = new List<string> { "new1", "new2" },
            DbConnectionString = "new-connection"
        };

        // Act
        var merged = AppConfig.Merge(baseline, overrides);

        // Assert - override values should be used
        Assert.Equal(5, merged.SampleIntervalSeconds);
        Assert.Equal(2, merged.ProcessNames.Count);
        Assert.Contains("new1", merged.ProcessNames);
        Assert.Equal("new-connection", merged.DbConnectionString);
    }

    [Fact]
    public void Merge_DeepMonitoring_MergesCorrectly()
    {
        // Arrange
        var baseline = new AppConfig
        {
            DeepMonitoring = new DeepMonitoringConfig
            {
                DotNetCoreApps = new List<DotNetAppConfig>
                {
                    new() { Name = "BaseApp", ProcessName = "baseapp" }
                },
                SqlDmvSampling = new SqlDmvSamplingConfig
                {
                    Enabled = false,
                    SampleIntervalSeconds = 10
                }
            }
        };
        var overrides = new AppConfig
        {
            DeepMonitoring = new DeepMonitoringConfig
            {
                DotNetCoreApps = new List<DotNetAppConfig>
                {
                    new() { Name = "OverrideApp", ProcessName = "overrideapp" }
                },
                SqlDmvSampling = new SqlDmvSamplingConfig
                {
                    Enabled = true,
                    SampleIntervalSeconds = 5
                }
            }
        };

        // Act
        var merged = AppConfig.Merge(baseline, overrides);

        // Assert
        Assert.Single(merged.DeepMonitoring.DotNetCoreApps);
        Assert.Equal("OverrideApp", merged.DeepMonitoring.DotNetCoreApps[0].Name);
        Assert.True(merged.DeepMonitoring.SqlDmvSampling.Enabled);
        Assert.Equal(5, merged.DeepMonitoring.SqlDmvSampling.SampleIntervalSeconds);
    }

    [Fact]
    public void ToJson_ProducesValidJson()
    {
        // Arrange
        var config = new AppConfig
        {
            SampleIntervalSeconds = 2,
            ProcessNames = new List<string> { "test" }
        };

        // Act
        var json = config.ToJson();

        // Assert
        Assert.Contains("\"SampleIntervalSeconds\": 2", json);
        Assert.Contains("\"test\"", json);
    }
}
