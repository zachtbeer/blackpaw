using Blackpaw.Alerting;
using Blackpaw.Configuration;
using Blackpaw.Data;
using Xunit;

namespace Blackpaw.Tests.Alerting;

public class AlertEvaluatorTests
{
    private static SystemSample SystemSample(double cpu = 0, double memInUse = 0) => new()
    {
        RunId = 1,
        TimestampUtc = DateTime.UtcNow,
        CpuTotalPercent = cpu,
        MemoryInUseMb = memInUse
    };

    [Fact]
    public void Evaluate_NoRules_ReturnsNoEvents()
    {
        var evaluator = new AlertEvaluator(new List<AlertRuleConfig>());

        var events = evaluator.Evaluate(SystemSample(cpu: 99), new List<ProcessSample>(), DateTime.UtcNow);

        Assert.Empty(events);
        Assert.False(evaluator.HasRules);
    }

    [Fact]
    public void Evaluate_SystemMetricBreachesThreshold_FiresAlert()
    {
        var rule = new AlertRuleConfig { Metric = "CpuTotalPercent", Operator = ">", Threshold = 90 };
        var evaluator = new AlertEvaluator(new[] { rule });

        var events = evaluator.Evaluate(SystemSample(cpu: 95), new List<ProcessSample>(), DateTime.UtcNow);

        Assert.Single(events);
        Assert.Equal(95, events[0].Value);
        Assert.Contains("CpuTotalPercent", events[0].Describe());
    }

    [Fact]
    public void Evaluate_ValueBelowThreshold_DoesNotFire()
    {
        var rule = new AlertRuleConfig { Metric = "CpuTotalPercent", Operator = ">", Threshold = 90 };
        var evaluator = new AlertEvaluator(new[] { rule });

        var events = evaluator.Evaluate(SystemSample(cpu: 50), new List<ProcessSample>(), DateTime.UtcNow);

        Assert.Empty(events);
    }

    [Fact]
    public void Evaluate_ProcessMetric_MatchesByProcessName()
    {
        var rule = new AlertRuleConfig { Metric = "WorkingSetMb", Operator = ">=", Threshold = 4096, ProcessName = "myapp" };
        var evaluator = new AlertEvaluator(new[] { rule });
        var processSamples = new List<ProcessSample>
        {
            new() { ProcessName = "myapp", WorkingSetMb = 5000 },
            new() { ProcessName = "otherapp", WorkingSetMb = 8000 }
        };

        var events = evaluator.Evaluate(SystemSample(), processSamples, DateTime.UtcNow);

        Assert.Single(events);
        Assert.Equal(5000, events[0].Value);
    }

    [Fact]
    public void Evaluate_ProcessMetric_NoMatchingProcess_DoesNotFire()
    {
        var rule = new AlertRuleConfig { Metric = "WorkingSetMb", Operator = ">", Threshold = 100, ProcessName = "missing" };
        var evaluator = new AlertEvaluator(new[] { rule });

        var events = evaluator.Evaluate(SystemSample(), new List<ProcessSample> { new() { ProcessName = "myapp", WorkingSetMb = 5000 } }, DateTime.UtcNow);

        Assert.Empty(events);
    }

    [Fact]
    public void Evaluate_RepeatedBreachWithinCooldown_FiresOnlyOnce()
    {
        var rule = new AlertRuleConfig { Metric = "CpuTotalPercent", Operator = ">", Threshold = 90, CooldownSeconds = 60 };
        var evaluator = new AlertEvaluator(new[] { rule });
        var t0 = DateTime.UtcNow;

        var first = evaluator.Evaluate(SystemSample(cpu: 95), new List<ProcessSample>(), t0);
        var second = evaluator.Evaluate(SystemSample(cpu: 96), new List<ProcessSample>(), t0.AddSeconds(10));

        Assert.Single(first);
        Assert.Empty(second);
    }

    [Fact]
    public void Evaluate_BreachAfterCooldownExpires_FiresAgain()
    {
        var rule = new AlertRuleConfig { Metric = "CpuTotalPercent", Operator = ">", Threshold = 90, CooldownSeconds = 30 };
        var evaluator = new AlertEvaluator(new[] { rule });
        var t0 = DateTime.UtcNow;

        var first = evaluator.Evaluate(SystemSample(cpu: 95), new List<ProcessSample>(), t0);
        var second = evaluator.Evaluate(SystemSample(cpu: 96), new List<ProcessSample>(), t0.AddSeconds(31));

        Assert.Single(first);
        Assert.Single(second);
    }

    [Fact]
    public void Evaluate_UnknownMetric_IsIgnored()
    {
        var rule = new AlertRuleConfig { Metric = "NotARealMetric", Operator = ">", Threshold = 1 };
        var evaluator = new AlertEvaluator(new[] { rule });

        var events = evaluator.Evaluate(SystemSample(cpu: 1000), new List<ProcessSample>(), DateTime.UtcNow);

        Assert.Empty(events);
    }

    [Fact]
    public void Evaluate_UnknownOperator_RuleIsInvalidAndSkipped()
    {
        var rule = new AlertRuleConfig { Metric = "CpuTotalPercent", Operator = "!=", Threshold = 1 };
        var evaluator = new AlertEvaluator(new[] { rule });

        Assert.False(evaluator.HasRules);
    }

    [Theory]
    [InlineData("<", 50, 40, true)]
    [InlineData("<", 50, 60, false)]
    [InlineData("<=", 50, 50, true)]
    [InlineData(">=", 50, 50, true)]
    [InlineData("==", 50, 50, true)]
    [InlineData("==", 50, 51, false)]
    public void Evaluate_Operators_EvaluateCorrectly(string op, double threshold, double cpu, bool expectFire)
    {
        var rule = new AlertRuleConfig { Metric = "CpuTotalPercent", Operator = op, Threshold = threshold };
        var evaluator = new AlertEvaluator(new[] { rule });

        var events = evaluator.Evaluate(SystemSample(cpu: cpu), new List<ProcessSample>(), DateTime.UtcNow);

        Assert.Equal(expectFire, events.Count == 1);
    }
}
