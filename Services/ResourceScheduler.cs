using System.Diagnostics;
using System.Runtime.InteropServices;

namespace BaChenAiLauncher;

internal enum ResourceConflictSeverity
{
    Information,
    Warning,
    Blocking
}

internal sealed record SystemResourceSnapshot(
    string? GpuName,
    int? GpuUsedMiB,
    int? GpuTotalMiB,
    long AvailableMemoryMiB,
    long TotalMemoryMiB);

internal sealed record ResourceConflict(ResourceConflictSeverity Severity, string Message);

internal sealed record ResourceAssessment(
    SystemResourceSnapshot Snapshot,
    IReadOnlyList<ResourceConflict> Conflicts,
    IReadOnlyList<int> ManagedProcessIds,
    IReadOnlyList<int> UnknownPortProcessIds)
{
    public bool BlocksLaunch => Conflicts.Any(conflict => conflict.Severity == ResourceConflictSeverity.Blocking);
    public bool RequiresConfirmation => Conflicts.Any(conflict => conflict.Severity == ResourceConflictSeverity.Warning);
}

internal static class ResourceScheduler
{
    public static ResourceAssessment Assess(ServiceProfile profile, IReadOnlyCollection<int> managedProcessIds, IReadOnlyCollection<int> portProcessIds)
    {
        var snapshot = SystemResourceProbe.Capture();
        var conflicts = new List<ResourceConflict>();
        var managed = managedProcessIds.Distinct().Order().ToArray();
        var unknownPortProcesses = portProcessIds.Except(managed).Distinct().Order().ToArray();

        if (unknownPortProcesses.Length > 0)
        {
            conflicts.Add(new ResourceConflict(
                ResourceConflictSeverity.Blocking,
                $"Port {profile.Port} is occupied by an unmanaged process (PID: {string.Join(", ", unknownPortProcesses)})."));
        }
        if (managed.Length > 0)
        {
            conflicts.Add(new ResourceConflict(
                ResourceConflictSeverity.Warning,
                $"{managed.Length} launcher-managed AI process(es) must be stopped first (PID: {string.Join(", ", managed)})."));
        }

        if (profile.RecommendedVramMiB > 0)
        {
            if (snapshot.GpuTotalMiB is null || snapshot.GpuUsedMiB is null)
            {
                conflicts.Add(new ResourceConflict(ResourceConflictSeverity.Warning, "GPU memory could not be read with nvidia-smi."));
            }
            else
            {
                var available = Math.Max(0, snapshot.GpuTotalMiB.Value - snapshot.GpuUsedMiB.Value);
                if (available < profile.RecommendedVramMiB)
                {
                    conflicts.Add(new ResourceConflict(
                        ResourceConflictSeverity.Warning,
                        $"Available GPU memory is {available:N0} MiB; this plugin recommends {profile.RecommendedVramMiB:N0} MiB."));
                }
            }
        }

        if (profile.RecommendedSystemMemoryMiB > 0 && snapshot.AvailableMemoryMiB < profile.RecommendedSystemMemoryMiB)
        {
            conflicts.Add(new ResourceConflict(
                ResourceConflictSeverity.Warning,
                $"Available system memory is {snapshot.AvailableMemoryMiB:N0} MiB; this plugin recommends {profile.RecommendedSystemMemoryMiB:N0} MiB."));
        }

        if (profile.IsMedium)
        {
            conflicts.Add(new ResourceConflict(ResourceConflictSeverity.Warning, "This profile is marked as high resource usage."));
        }

        return new ResourceAssessment(snapshot, conflicts, managed, unknownPortProcesses);
    }
}

internal static class SystemResourceProbe
{
    internal static string FormatGpuUsageGiB(int usedMiB, int totalMiB)
        => $"{usedMiB / 1024D:0.00} / {totalMiB / 1024D:0.00} GiB";

    public static SystemResourceSnapshot Capture()
    {
        var gpu = ReadPrimaryGpu();
        var memory = ReadSystemMemory();
        return new SystemResourceSnapshot(gpu?.Name, gpu?.UsedMiB, gpu?.TotalMiB, memory.AvailableMiB, memory.TotalMiB);
    }

    public static (int UsedMiB, int TotalMiB)? ReadGpuMemory()
    {
        var gpu = ReadPrimaryGpu();
        return gpu is null ? null : (gpu.UsedMiB, gpu.TotalMiB);
    }

    public static GpuResourceInfo? ReadPrimaryGpu()
    {
        try
        {
            var startInfo = new ProcessStartInfo(FindNvidiaSmi(), "--query-gpu=index,name,memory.used,memory.total --format=csv,noheader,nounits")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return null;
            }
            var output = process.StandardOutput.ReadToEnd();
            if (!process.WaitForExit(3000) || process.ExitCode != 0)
            {
                return null;
            }
            return ParseGpuOutput(output)
                .OrderByDescending(gpu => gpu.TotalMiB)
                .ThenBy(gpu => gpu.Index)
                .FirstOrDefault();
        }
        catch
        {
            return null;
        }
    }

    internal static IReadOnlyList<GpuResourceInfo> ParseGpuOutput(string output)
    {
        var devices = new List<GpuResourceInfo>();
        foreach (var line in output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var values = line.Split(',', StringSplitOptions.TrimEntries);
            if (values.Length >= 4 &&
                int.TryParse(values[0], out var index) &&
                int.TryParse(values[^2], out var used) &&
                int.TryParse(values[^1], out var total) &&
                !string.IsNullOrWhiteSpace(values[1]))
            {
                devices.Add(new GpuResourceInfo(index, values[1].Trim(), used, total));
            }
        }
        return devices;
    }

    private static string FindNvidiaSmi()
    {
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var installedPath = Path.Combine(programFiles, "NVIDIA Corporation", "NVSMI", "nvidia-smi.exe");
        return File.Exists(installedPath) ? installedPath : "nvidia-smi.exe";
    }

    private static (long AvailableMiB, long TotalMiB) ReadSystemMemory()
    {
        var status = new MemoryStatusEx();
        return GlobalMemoryStatusEx(status)
            ? ((long)(status.AvailablePhysical / 1024 / 1024), (long)(status.TotalPhysical / 1024 / 1024))
            : (0, 0);
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx([In, Out] MemoryStatusEx buffer);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private sealed class MemoryStatusEx
    {
        public uint Length = (uint)Marshal.SizeOf<MemoryStatusEx>();
        public uint MemoryLoad;
        public ulong TotalPhysical;
        public ulong AvailablePhysical;
        public ulong TotalPageFile;
        public ulong AvailablePageFile;
        public ulong TotalVirtual;
        public ulong AvailableVirtual;
        public ulong AvailableExtendedVirtual;
    }
}

internal sealed record GpuResourceInfo(int Index, string Name, int UsedMiB, int TotalMiB);
