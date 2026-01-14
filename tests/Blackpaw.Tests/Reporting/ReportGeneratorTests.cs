using Blackpaw.Data;
using Blackpaw.Reporting;
using Xunit;

namespace Blackpaw.Tests.Reporting;

public class ReportGeneratorTests
{
    [Fact]
    public void GenerateHtml_WithMinimalData_ProducesValidHtml()
    {
        // Arrange
        var data = CreateMinimalReportData();

        // Act
        var html = ReportGenerator.GenerateHtml(data);

        // Assert
        Assert.NotNull(html);
        Assert.Contains("<!DOCTYPE html>", html);
        Assert.Contains("<html", html);
        Assert.Contains("<head>", html);
        Assert.Contains("<body>", html);
        Assert.Contains("</html>", html);
        Assert.Contains("Blackpaw Performance Report", html);
    }

    [Fact]
    public void GenerateHtml_WithEmptyCollections_DoesNotThrow()
    {
        // Arrange
        var data = CreateMinimalReportData();
        // All collections are already empty by default

        // Act
        var exception = Record.Exception(() => ReportGenerator.GenerateHtml(data));

        // Assert
        Assert.Null(exception);
    }

    [Fact]
    public void GenerateHtml_WithSampleData_IncludesChartContainers()
    {
        // Arrange
        var data = CreateMinimalReportData();
        data.SystemSamples.Add(new SystemSample
        {
            SampleId = 1,
            RunId = 1,
            TimestampUtc = DateTime.UtcNow,
            CpuTotalPercent = 50.0,
            MemoryInUseMb = 8192
        });
        data.SystemSamples.Add(new SystemSample
        {
            SampleId = 2,
            RunId = 1,
            TimestampUtc = DateTime.UtcNow.AddSeconds(1),
            CpuTotalPercent = 55.0,
            MemoryInUseMb = 8300
        });

        // Act
        var html = ReportGenerator.GenerateHtml(data);

        // Assert
        Assert.Contains("cpuMemChart", html);
        Assert.Contains("<canvas", html);
        Assert.Contains("System Metrics", html);
    }

    [Fact]
    public void GenerateHtml_WithSpecialCharacters_EscapesCorrectly()
    {
        // Arrange
        var data = CreateMinimalReportData();
        data.Run.ScenarioName = "Test <b>bold</b> & \"quoted\"";
        data.Run.Notes = "Notes with <em>emphasis</em>";

        // Act
        var html = ReportGenerator.GenerateHtml(data);

        // Assert - verify user content is escaped (not raw HTML tags from user input)
        Assert.Contains("&lt;b&gt;bold&lt;/b&gt;", html);
        Assert.Contains("&amp;", html);
        Assert.Contains("&quot;quoted&quot;", html);
        Assert.Contains("&lt;em&gt;emphasis&lt;/em&gt;", html);
    }

    private static ReportData CreateMinimalReportData() => new ReportData
    {
        Run = new RunRecord
        {
            RunId = 1,
            ScenarioName = "Test Scenario",
            StartedAtUtc = DateTime.UtcNow,
            MachineName = "TestMachine",
            OsVersion = "Test OS 10.0",
            CpuLogicalCoreCount = 4,
            TotalPhysicalMemoryMb = 16384,
            ProbeVersion = "1.0.0"
        }
    };
}
