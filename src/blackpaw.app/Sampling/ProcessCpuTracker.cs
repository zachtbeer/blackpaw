using System.Diagnostics;

namespace Blackpaw.Sampling;

public class ProcessCpuTracker
{
    private readonly Dictionary<int, TimeSpan> _lastCpuTimes = new();

    public double CalculateCpuPercent(Process process, double intervalSeconds, int logicalCoreCount)
    {
        var totalTime = process.TotalProcessorTime;
        _lastCpuTimes.TryGetValue(process.Id, out var lastTime);
        _lastCpuTimes[process.Id] = totalTime;

        if (lastTime == default)
        {
            return 0;
        }

        var delta = totalTime - lastTime;
        var percent = delta.TotalSeconds / (intervalSeconds * logicalCoreCount) * 100;
        return Math.Max(0, percent);
    }

    public void TrimToActive(IEnumerable<Process> activeProcesses)
    {
        var activeIds = activeProcesses.Select(p => p.Id).ToHashSet();
        var stale = _lastCpuTimes.Keys.Where(id => !activeIds.Contains(id)).ToList();
        foreach (var id in stale)
        {
            _lastCpuTimes.Remove(id);
        }
    }
}
