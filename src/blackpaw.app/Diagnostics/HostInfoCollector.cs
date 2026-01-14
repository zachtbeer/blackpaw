using Hardware.Info;
using Blackpaw.Data;

namespace Blackpaw.Diagnostics;

public static class HostInfoCollector
{
    private static readonly Lazy<HardwareInfo> HardwareInfoInstance = new(() =>
    {
        var info = new HardwareInfo();
        info.RefreshCPUList();
        info.RefreshMemoryStatus();
        return info;
    });

    public static void PopulateHostFields(RunRecord run)
    {
        run.MachineName = Environment.MachineName;
        run.OsVersion = Environment.OSVersion.VersionString;
        run.CpuLogicalCoreCount = Environment.ProcessorCount;

        var hw = HardwareInfoInstance.Value;
        run.CpuModel = hw.CpuList.FirstOrDefault()?.Name?.Trim();
        run.TotalPhysicalMemoryMb = Math.Round(hw.MemoryStatus.TotalPhysical / (1024d * 1024d), 2);

        var systemDrive = GetSystemDrive();
        if (systemDrive != null)
        {
            run.SystemDriveType = systemDrive.DriveType.ToString();
            run.SystemDriveFreeSpaceMbAtStart = systemDrive.AvailableFreeSpace / (1024d * 1024d);
        }

        run.SystemUptimeSecondsAtStart = TimeSpan.FromMilliseconds(Environment.TickCount64).TotalSeconds;
    }

    private static DriveInfo? GetSystemDrive()
    {
        try
        {
            var root = Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\";
            return new DriveInfo(root);
        }
        catch (Exception ex)
        {
            Logger.Debug($"Failed to get system drive info: {ex.Message}");
            return null;
        }
    }
}
