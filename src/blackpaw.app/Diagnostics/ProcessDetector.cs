using System.Diagnostics;

namespace Blackpaw.Diagnostics;

public static class ProcessDetector
{
    /// <summary>
    /// Detects all running .NET Core/.NET 5+ processes by checking for coreclr.dll
    /// </summary>
    public static List<ProcessInfo> DetectDotNetCoreProcesses()
    {
        var result = new List<ProcessInfo>();
        var currentPid = Environment.ProcessId;

        foreach (var proc in Process.GetProcesses())
        {
            if (proc.Id == currentPid) continue;

            try
            {
                var modules = proc.Modules;
                foreach (ProcessModule module in modules)
                {
                    var moduleName = module.ModuleName.ToLowerInvariant();
                    if (moduleName is "coreclr.dll" or "libcoreclr.so" or "libcoreclr.dylib")
                    {
                        result.Add(new ProcessInfo(proc.Id, proc.ProcessName));
                        break;
                    }
                }
            }
            catch
            {
                // Access denied or process exited - skip
            }
        }

        return result.DistinctBy(p => p.ProcessName).ToList();
    }

    /// <summary>
    /// Detects all running .NET Framework processes by checking for clr.dll (but not coreclr.dll)
    /// </summary>
    public static List<ProcessInfo> DetectDotNetFrameworkProcesses()
    {
        var result = new List<ProcessInfo>();
        var currentPid = Environment.ProcessId;

        foreach (var proc in Process.GetProcesses())
        {
            if (proc.Id == currentPid) continue;

            try
            {
                var modules = proc.Modules;
                bool hasClr = false;
                bool hasCoreclr = false;

                foreach (ProcessModule module in modules)
                {
                    var moduleName = module.ModuleName.ToLowerInvariant();
                    if (moduleName == "clr.dll") hasClr = true;
                    if (moduleName == "coreclr.dll") hasCoreclr = true;
                }

                if (hasClr && !hasCoreclr)
                {
                    result.Add(new ProcessInfo(proc.Id, proc.ProcessName));
                }
            }
            catch
            {
                // Access denied or process exited - skip
            }
        }

        return result.DistinctBy(p => p.ProcessName).ToList();
    }

    /// <summary>
    /// Detects all .NET processes (both Core and Framework)
    /// </summary>
    public static List<ProcessInfo> DetectAllDotNetProcesses()
    {
        var result = new List<ProcessInfo>();
        var currentPid = Environment.ProcessId;

        foreach (var proc in Process.GetProcesses())
        {
            if (proc.Id == currentPid) continue;

            try
            {
                var modules = proc.Modules;
                foreach (ProcessModule module in modules)
                {
                    var moduleName = module.ModuleName.ToLowerInvariant();
                    if (moduleName is "coreclr.dll" or "clr.dll" or "libcoreclr.so" or "libcoreclr.dylib")
                    {
                        result.Add(new ProcessInfo(proc.Id, proc.ProcessName));
                        break;
                    }
                }
            }
            catch
            {
                // Access denied or process exited - skip
            }
        }

        return result.DistinctBy(p => p.ProcessName).ToList();
    }
}

public record ProcessInfo(int Id, string ProcessName);
