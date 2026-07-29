using System.Diagnostics;
using System.Text.RegularExpressions;

namespace BaChenAiLauncher;

internal static class PythonEnvironmentService
{
    public static async Task EnsureRepositoryAsync(
        GitHubRepositoryAnalysis analysis,
        string pluginRoot,
        string dataRoot,
        HttpClient httpClient,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var runtime = ManagedPythonRuntimeService.SelectForConstraint(analysis.RuntimeVersion);
        progress?.Report($"Project requires Python {analysis.RuntimeVersion}; selected managed Python {runtime.Version}");
        if (analysis.EnvironmentManager.Equals("uv", StringComparison.OrdinalIgnoreCase))
        {
            var managedPython = await new ManagedPythonRuntimeService(httpClient).EnsureAsync(
                runtime.Id,
                dataRoot,
                progress,
                cancellationToken: cancellationToken);
            var uvExecutable = await EnsureExternalUvAsync(managedPython, dataRoot, progress, cancellationToken);
            var environmentPath = Path.Combine(pluginRoot, ".venv");
            var backupPath = await BackupIncompatibleEnvironmentAsync(environmentPath, analysis.RuntimeVersion, pluginRoot, progress, cancellationToken);
            try
            {
                progress?.Report("Synchronizing repository dependencies with external uv");
                await RunAsync(uvExecutable, BuildUvSyncArguments(analysis.EnvironmentArguments, managedPython), pluginRoot, cancellationToken);
                await ValidateVersionAsync(Path.Combine(environmentPath, "Scripts", "python.exe"), analysis.RuntimeVersion, pluginRoot, cancellationToken);
                TryDeleteDirectory(backupPath);
            }
            catch
            {
                RestoreEnvironmentBackup(environmentPath, backupPath);
                throw;
            }
            return;
        }

        var baseManifest = new PluginPackageManifest
        {
            Runtime = analysis.Runtime,
            RuntimeVersion = analysis.RuntimeVersion,
            CreateVirtualEnvironment = true,
            VirtualEnvironmentPath = ".venv",
            RequirementsFile = analysis.RequirementsFile,
            ManagedRuntimeId = runtime.Id,
            PythonInstallArguments = analysis.EnvironmentManager.Equals("pip", StringComparison.OrdinalIgnoreCase) && File.Exists(Path.Combine(pluginRoot, "pyproject.toml"))
                ? ["-m", "pip", "install", "--disable-pip-version-check", "-e", "."]
                : []
        };
        await EnsureAsync(baseManifest, pluginRoot, dataRoot, httpClient, progress, cancellationToken: cancellationToken);
    }

    internal static string[] BuildUvSyncArguments(IEnumerable<string> detectedArguments, string managedPython)
    {
        var arguments = detectedArguments.Where(argument => !argument.Equals("--active", StringComparison.OrdinalIgnoreCase)).ToList();
        arguments.Add("--python");
        arguments.Add(managedPython);
        return arguments.ToArray();
    }

    private static async Task<string> EnsureExternalUvAsync(string managedPython, string dataRoot, IProgress<string>? progress, CancellationToken cancellationToken)
    {
        var toolRoot = Path.Combine(Path.GetFullPath(dataRoot), "tools", "uv");
        var toolPython = Path.Combine(toolRoot, "Scripts", "python.exe");
        var uvExecutable = Path.Combine(toolRoot, "Scripts", "uv.exe");
        if (File.Exists(uvExecutable))
        {
            return uvExecutable;
        }
        Directory.CreateDirectory(Path.GetDirectoryName(toolRoot)!);
        if (!File.Exists(toolPython))
        {
            progress?.Report("Creating the external uv tool environment");
            await RunAsync(managedPython, ["-m", "venv", toolRoot], dataRoot, cancellationToken);
        }
        progress?.Report("Installing the external uv environment manager");
        await RunAsync(toolPython, ["-m", "pip", "install", "--disable-pip-version-check", "uv"], toolRoot, cancellationToken);
        return File.Exists(uvExecutable) ? uvExecutable : throw new FileNotFoundException("The external uv executable was not installed.", uvExecutable);
    }

    public static async Task EnsureAsync(PluginPackageManifest manifest, string pluginRoot, string dataRoot, HttpClient httpClient, IProgress<string>? progress = null, IProgress<PluginDownloadProgress>? downloadProgress = null, CancellationToken cancellationToken = default)
    {
        if (!manifest.CreateVirtualEnvironment)
        {
            return;
        }
        if (!manifest.Runtime.Equals("python", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("createVirtualEnvironment is supported only for the python runtime.");
        }
        var environmentPath = ResolveSafePath(pluginRoot, manifest.VirtualEnvironmentPath, "virtualEnvironmentPath");
        var environmentPython = Path.Combine(environmentPath, "Scripts", "python.exe");
        var backupPath = await BackupIncompatibleEnvironmentAsync(environmentPath, manifest.RuntimeVersion, pluginRoot, progress, cancellationToken);
        try
        {
            if (!File.Exists(environmentPython))
            {
                progress?.Report("Creating Python virtual environment");
                var launcher = string.IsNullOrWhiteSpace(manifest.ManagedRuntimeId)
                    ? FindPythonLauncher()
                    : await new ManagedPythonRuntimeService(httpClient).EnsureAsync(manifest.ManagedRuntimeId, dataRoot, progress, downloadProgress, cancellationToken);
                var arguments = launcher.EndsWith("py.exe", StringComparison.OrdinalIgnoreCase) && string.IsNullOrWhiteSpace(manifest.ManagedRuntimeId)
                    ? new[] { "-3", "-m", "venv", environmentPath }
                    : new[] { "-m", "venv", environmentPath };
                await RunAsync(launcher, arguments, pluginRoot, cancellationToken);
            }
            await ValidateVersionAsync(environmentPython, manifest.RuntimeVersion, pluginRoot, cancellationToken);
            if (!string.IsNullOrWhiteSpace(manifest.RequirementsFile))
            {
                var requirements = ResolveSafePath(pluginRoot, manifest.RequirementsFile, "requirementsFile");
                if (!File.Exists(requirements))
                {
                    throw new FileNotFoundException("The manifest requirements file is missing.", requirements);
                }
                progress?.Report("Installing Python dependencies");
                await RunAsync(environmentPython, ["-m", "pip", "install", "--disable-pip-version-check", "-r", requirements], pluginRoot, cancellationToken);
            }
            if ((manifest.PythonInstallArguments ?? []).Length > 0)
            {
                progress?.Report("Installing the Python plugin package");
                await RunAsync(environmentPython, manifest.PythonInstallArguments ?? [], pluginRoot, cancellationToken);
            }
            TryDeleteDirectory(backupPath);
        }
        catch
        {
            RestoreEnvironmentBackup(environmentPath, backupPath);
            throw;
        }
    }

    internal static string FindPythonLauncher()
    {
        var windows = Environment.GetEnvironmentVariable("WINDIR") ?? @"C:\Windows";
        var py = Path.Combine(windows, "py.exe");
        if (File.Exists(py))
        {
            return py;
        }
        return "python.exe";
    }

    private static async Task RunAsync(string executable, IEnumerable<string> arguments, string workingDirectory, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo(executable)
        {
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }
        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException($"Could not start {executable}.");
        var standardOutput = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var standardError = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        var output = await standardOutput;
        var error = await standardError;
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"Python environment command failed ({process.ExitCode}). {error}\n{output}".Trim());
        }
    }

    private static async Task ValidateVersionAsync(string python, string constraint, string workingDirectory, CancellationToken cancellationToken)
    {
        var actual = await ReadVersionAsync(python, workingDirectory, cancellationToken);
        if (!ManagedPythonRuntimeService.SatisfiesConstraint(actual, constraint))
        {
            throw new InvalidOperationException($"Python {actual} does not satisfy runtimeVersion '{constraint}'.");
        }
    }

    private static async Task<Version> ReadVersionAsync(string python, string workingDirectory, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo(python, "--version")
        {
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not inspect the Python virtual environment.");
        var output = await process.StandardOutput.ReadToEndAsync(cancellationToken) + await process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        var actualMatch = Regex.Match(output, "\\d+\\.\\d+(?:\\.\\d+)?");
        if (!actualMatch.Success)
        {
            throw new InvalidOperationException($"Unable to determine the Python version from '{output.Trim()}'.");
        }
        return Version.Parse(actualMatch.Value);
    }

    private static async Task<string?> BackupIncompatibleEnvironmentAsync(
        string environmentPath,
        string constraint,
        string workingDirectory,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        var environmentPython = Path.Combine(environmentPath, "Scripts", "python.exe");
        if (!File.Exists(environmentPython))
        {
            return null;
        }
        try
        {
            var version = await ReadVersionAsync(environmentPython, workingDirectory, cancellationToken);
            if (ManagedPythonRuntimeService.SatisfiesConstraint(version, constraint))
            {
                return null;
            }
            progress?.Report($"Rebuilding incompatible Python {version} environment for requirement {constraint}");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            progress?.Report("Rebuilding an unreadable Python virtual environment");
        }
        var backupPath = environmentPath + ".bachen-backup-" + Guid.NewGuid().ToString("N");
        Directory.Move(environmentPath, backupPath);
        return backupPath;
    }

    private static void RestoreEnvironmentBackup(string environmentPath, string? backupPath)
    {
        if (string.IsNullOrWhiteSpace(backupPath) || !Directory.Exists(backupPath))
        {
            return;
        }
        DeleteDirectoryIfPresent(environmentPath);
        Directory.Move(backupPath, environmentPath);
    }

    private static void DeleteDirectoryIfPresent(string? path)
    {
        if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
        {
            Directory.Delete(path, true);
        }
    }

    private static void TryDeleteDirectory(string? path)
    {
        try
        {
            DeleteDirectoryIfPresent(path);
        }
        catch
        {
            // A completed environment must remain usable even if its obsolete backup is temporarily locked.
        }
    }

    internal static bool IsEnvironmentCompatible(string pluginRoot, string constraint)
    {
        var configurationPath = Path.Combine(pluginRoot, ".venv", "pyvenv.cfg");
        if (!File.Exists(configurationPath))
        {
            return false;
        }
        try
        {
            var match = Regex.Match(File.ReadAllText(configurationPath), @"(?mi)^\s*version(?:_info)?\s*=\s*(?<version>\d+\.\d+(?:\.\d+)?)\s*$");
            return match.Success && ManagedPythonRuntimeService.SatisfiesConstraint(Version.Parse(match.Groups["version"].Value), constraint);
        }
        catch
        {
            return false;
        }
    }

    private static string ResolveSafePath(string root, string relative, string field)
    {
        if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative) || relative.Contains("..", StringComparison.Ordinal))
        {
            throw new InvalidDataException($"{field} must be a safe relative path.");
        }
        var normalizedRoot = Path.GetFullPath(root) + Path.DirectorySeparatorChar;
        var path = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
        return path.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase) ? path : throw new InvalidDataException($"{field} escapes the plugin root.");
    }
}
