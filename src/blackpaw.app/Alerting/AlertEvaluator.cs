using Blackpaw.Configuration;
using Blackpaw.Data;

namespace Blackpaw.Alerting;

/// <summary>
/// Result of a single rule breach, ready to be turned into a console warning and a Marker.
/// </summary>
public record AlertEvent(AlertRuleConfig Rule, double Value, DateTime TimestampUtc)
{
    public string Describe()
    {
        var scope = string.IsNullOrWhiteSpace(Rule.ProcessName) ? "system" : Rule.ProcessName;
        return $"[{scope}] {Rule.Metric} {Rule.Operator} {Rule.Threshold} breached (value={Value:N2})";
    }
}

/// <summary>
/// Evaluates configured threshold rules against sampled metrics and applies a per-rule cooldown
/// so a sustained breach doesn't fire (and mark) every sample interval.
/// </summary>
public class AlertEvaluator
{
    private readonly List<AlertRuleConfig> _rules;
    private readonly Dictionary<AlertRuleConfig, DateTime> _lastFired = new();

    public AlertEvaluator(IEnumerable<AlertRuleConfig> rules)
    {
        _rules = rules.Where(IsValid).ToList();
    }

    public bool HasRules => _rules.Count > 0;

    public List<AlertEvent> Evaluate(SystemSample systemSample, IReadOnlyCollection<ProcessSample> processSamples, DateTime timestampUtc)
    {
        var events = new List<AlertEvent>();

        foreach (var rule in _rules)
        {
            double? value = string.IsNullOrWhiteSpace(rule.ProcessName)
                ? GetSystemMetricValue(systemSample, rule.Metric)
                : GetProcessMetricValue(processSamples, rule);

            if (value is null || !Breaches(rule, value.Value))
            {
                continue;
            }

            if (_lastFired.TryGetValue(rule, out var lastFired) &&
                (timestampUtc - lastFired).TotalSeconds < rule.CooldownSeconds)
            {
                continue;
            }

            _lastFired[rule] = timestampUtc;
            events.Add(new AlertEvent(rule, value.Value, timestampUtc));
        }

        return events;
    }

    private static bool IsValid(AlertRuleConfig rule) =>
        !string.IsNullOrWhiteSpace(rule.Metric) && IsKnownOperator(rule.Operator);

    private static bool IsKnownOperator(string op) => op is ">" or ">=" or "<" or "<=" or "==";

    private static double? GetSystemMetricValue(SystemSample sample, string metric) => metric switch
    {
        "CpuTotalPercent" => sample.CpuTotalPercent,
        "MemoryInUseMb" => sample.MemoryInUseMb,
        "MemoryAvailableMb" => sample.MemoryAvailableMb,
        "DiskReadBytesPerSec" => sample.DiskReadBytesPerSec,
        "DiskWriteBytesPerSec" => sample.DiskWriteBytesPerSec,
        _ => null
    };

    private static double? GetProcessMetricValue(IReadOnlyCollection<ProcessSample> processSamples, AlertRuleConfig rule)
    {
        var process = processSamples.FirstOrDefault(p => string.Equals(p.ProcessName, rule.ProcessName, StringComparison.OrdinalIgnoreCase));
        if (process is null)
        {
            return null;
        }

        return rule.Metric switch
        {
            "CpuPercent" => process.CpuPercent,
            "WorkingSetMb" => process.WorkingSetMb,
            "PrivateBytesMb" => process.PrivateBytesMb,
            "ThreadCount" => process.ThreadCount,
            "HandleCount" => process.HandleCount,
            _ => null
        };
    }

    private static bool Breaches(AlertRuleConfig rule, double value) => rule.Operator switch
    {
        ">" => value > rule.Threshold,
        ">=" => value >= rule.Threshold,
        "<" => value < rule.Threshold,
        "<=" => value <= rule.Threshold,
        "==" => Math.Abs(value - rule.Threshold) < 0.0001,
        _ => false
    };
}
