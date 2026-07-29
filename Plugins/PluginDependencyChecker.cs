using System.Diagnostics;

namespace BaChenAiLauncher;

internal sealed record DependencyCheckResult(string Requirement, bool IsSatisfied, bool IsEnforced, string Details);

internal static class PluginDependencyChecker
{
    public static IReadOnlyList<DependencyCheckResult> Check(IEnumerable<string>? requirements, string pluginRoot, Func<bool>? cudaProbe = null)
    {
        return (requirements ?? []).Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => CheckOne(value.Trim(), pluginRoot, cudaProbe)).ToArray();
    }

    private static DependencyCheckResult CheckOne(string requirement, string pluginRoot, Func<bool>? cudaProbe)
    {
        if (requirement.StartsWith("command:", StringComparison.OrdinalIgnoreCase))
        {
            var command = requirement[8..].Trim();
            var path = FindCommand(command);
            return new DependencyCheckResult(requirement, path is not null, true, path ?? $"Command '{command}' was not found in PATH.");
        }
        if (requirement.StartsWith("env:", StringComparison.OrdinalIgnoreCase))
        {
            var variable = requirement[4..].Trim();
            var value = Environment.GetEnvironmentVariable(variable);
            return new DependencyCheckResult(requirement, !string.IsNullOrWhiteSpace(value), true, string.IsNullOrWhiteSpace(value) ? $"Environment variable '{variable}' is not set." : "Available");
        }
        if (requirement.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
        {
            var relative = requirement[5..].Trim();
            var fullPath = Path.GetFullPath(Path.Combine(pluginRoot, relative.Replace('/', Path.DirectorySeparatorChar)));
            var normalizedRoot = Path.GetFullPath(pluginRoot) + Path.DirectorySeparatorChar;
            var exists = fullPath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase) && (File.Exists(fullPath) || Directory.Exists(fullPath));
            return new DependencyCheckResult(requirement, exists, true, exists ? fullPath : $"Required path is missing: {relative}");
        }
        if (requirement.Equals("cuda", StringComparison.OrdinalIgnoreCase))
        {
            var available = cudaProbe?.Invoke() ?? SystemResourceProbe.ReadGpuMemory() is not null;
            return new DependencyCheckResult(requirement, available, true, available ? "NVIDIA CUDA-capable GPU detected." : "nvidia-smi is unavailable.");
        }
        if (requirement.StartsWith("python", StringComparison.OrdinalIgnoreCase))
        {
            var localPython = Path.Combine(pluginRoot, ".venv", "Scripts", "python.exe");
            if (File.Exists(localPython))
            {
                var constraint = requirement["python".Length..].Trim();
                var compatible = string.IsNullOrWhiteSpace(constraint) || PythonEnvironmentService.IsEnvironmentCompatible(pluginRoot, constraint);
                return new DependencyCheckResult(
                    requirement,
                    compatible,
                    true,
                    compatible ? localPython : $"The local virtual environment does not satisfy Python {constraint}.");
            }
            var path = FindCommand("python.exe") ?? FindCommand("python");
            return new DependencyCheckResult(requirement, path is not null, true, path ?? "Python was not found in PATH.");
        }
        return new DependencyCheckResult(requirement, true, false, "Declared by the publisher; no automatic probe is defined.");
    }

    private static string? FindCommand(string command)
    {
        try
        {
            var startInfo = new ProcessStartInfo("where.exe", command)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using var process = Process.Start(startInfo);
            if (process is null || !process.WaitForExit(3000) || process.ExitCode != 0)
            {
                return null;
            }
            return process.StandardOutput.ReadLine();
        }
        catch
        {
            return null;
        }
    }
}
