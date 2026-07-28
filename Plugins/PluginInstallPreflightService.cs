namespace BaChenAiLauncher;

internal enum PluginPreflightSeverity
{
    Information,
    Warning,
    Blocking
}

internal sealed record PluginPreflightIssue(PluginPreflightSeverity Severity, string Message);

internal sealed record PluginInstallPreflight(long AvailableDiskBytes, long RequiredDiskBytes, IReadOnlyList<PluginPreflightIssue> Issues)
{
    public bool CanInstall => Issues.All(issue => issue.Severity != PluginPreflightSeverity.Blocking);
}

internal static class PluginInstallPreflightService
{
    public static PluginInstallPreflight Assess(PluginPackageManifest manifest, string dataRoot, Func<(int UsedMiB, int TotalMiB)?>? gpuProbe = null)
    {
        var fullRoot = Path.GetFullPath(dataRoot);
        var pathRoot = Path.GetPathRoot(fullRoot) ?? throw new InvalidDataException("The plugin data root has no drive.");
        var drive = new DriveInfo(pathRoot);
        var totalDownload = manifest.PackageSizeBytes;
        foreach (var asset in manifest.AssetPackages ?? [])
        {
            totalDownload = asset.SizeBytes > long.MaxValue - totalDownload ? long.MaxValue : totalDownload + asset.SizeBytes;
        }
        var calculatedDisk = totalDownload > (long.MaxValue - 512L * 1024 * 1024) / 2
            ? long.MaxValue
            : totalDownload * 2 + 512L * 1024 * 1024;
        var requiredDisk = Math.Max(manifest.MinimumFreeDiskBytes, calculatedDisk);
        var issues = new List<PluginPreflightIssue>();
        if (drive.AvailableFreeSpace < requiredDisk)
        {
            issues.Add(new PluginPreflightIssue(PluginPreflightSeverity.Blocking, $"Available disk space is {drive.AvailableFreeSpace:N0} bytes; installation requires at least {requiredDisk:N0} bytes."));
        }
        if ((manifest.Dependencies ?? []).Contains("cuda", StringComparer.OrdinalIgnoreCase))
        {
            var gpu = (gpuProbe ?? SystemResourceProbe.ReadGpuMemory)();
            if (gpu is null)
            {
                issues.Add(new PluginPreflightIssue(PluginPreflightSeverity.Warning, "An NVIDIA GPU could not be detected with nvidia-smi."));
            }
            else if (manifest.RecommendedVramMiB > 0 && gpu.Value.TotalMiB < manifest.RecommendedVramMiB)
            {
                issues.Add(new PluginPreflightIssue(PluginPreflightSeverity.Warning, $"GPU memory is {gpu.Value.TotalMiB:N0} MiB; this plugin recommends {manifest.RecommendedVramMiB:N0} MiB."));
            }
        }
        return new PluginInstallPreflight(drive.AvailableFreeSpace, requiredDisk, issues);
    }
}
