using System.Diagnostics;
using System.IO.Compression;
using System.Text.RegularExpressions;

namespace BaChenAiLauncher;

internal sealed record ManagedPythonRuntimeDefinition(
    string Id,
    string Version,
    string Url,
    string Sha256,
    long SizeBytes);

internal sealed class ManagedPythonRuntimeService(HttpClient httpClient)
{
    public static readonly ManagedPythonRuntimeDefinition Python311 = new(
        "python-3.11.9-x64",
        "3.11.9",
        "https://api.nuget.org/v3-flatcontainer/python/3.11.9/python.3.11.9.nupkg",
        "9283876D58C017E0E846F95B490DA3BCA0FC0A6EE1134B2870677CFB7EEC3C67",
        17478009);

    public static readonly ManagedPythonRuntimeDefinition Python312 = new(
        "python-3.12.10-x64",
        "3.12.10",
        "https://api.nuget.org/v3-flatcontainer/python/3.12.10/python.3.12.10.nupkg",
        "0EB85C2DFCCCCF1B17352DE4C397F69194035B7D37149EACC16F1147D93DE3B8",
        14515433);

    public static IReadOnlyList<ManagedPythonRuntimeDefinition> Supported { get; } = [Python311, Python312];

    public static ManagedPythonRuntimeDefinition SelectForConstraint(string? constraint)
    {
        var normalized = string.IsNullOrWhiteSpace(constraint) ? ">=3.10" : constraint.Trim();
        return Supported
            .Where(runtime => SatisfiesConstraint(Version.Parse(runtime.Version), normalized))
            .OrderByDescending(runtime => Version.Parse(runtime.Version))
            .FirstOrDefault()
            ?? throw new InvalidOperationException($"No managed Python runtime satisfies '{normalized}'. Supported versions: {string.Join(", ", Supported.Select(runtime => runtime.Version))}.");
    }

    internal static bool SatisfiesConstraint(Version version, string? constraint)
    {
        if (string.IsNullOrWhiteSpace(constraint))
        {
            return true;
        }
        foreach (var rawClause in constraint.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var match = Regex.Match(rawClause, @"^(?<operator>===|==|!=|<=|>=|~=|<|>)?\s*(?<version>\d+(?:\.\d+){0,2})(?<wildcard>\.\*)?$");
            if (!match.Success)
            {
                throw new InvalidDataException($"Unsupported Python version constraint: {constraint}");
            }
            var expected = Version.Parse(match.Groups["version"].Value);
            var comparison = version.CompareTo(expected);
            var operation = match.Groups["operator"].Value;
            var wildcard = match.Groups["wildcard"].Success;
            var satisfied = operation switch
            {
                ">=" => comparison >= 0,
                ">" => comparison > 0,
                "<=" => comparison <= 0,
                "<" => comparison < 0,
                "!=" when wildcard => !MatchesPrefix(version, expected),
                "!=" => comparison != 0,
                "==" or "===" when wildcard => MatchesPrefix(version, expected),
                "==" or "===" or "" => MatchesPrefix(version, expected),
                "~=" => comparison >= 0 && version < CompatibleUpperBound(expected),
                _ => false
            };
            if (!satisfied)
            {
                return false;
            }
        }
        return true;
    }

    private static bool MatchesPrefix(Version actual, Version expected)
        => actual.Major == expected.Major &&
           (expected.Minor < 0 || actual.Minor == expected.Minor) &&
           (expected.Build < 0 || actual.Build == expected.Build);

    private static Version CompatibleUpperBound(Version expected)
        => expected.Build >= 0
            ? new Version(expected.Major, expected.Minor + 1)
            : new Version(expected.Major + 1, 0);

    public async Task<string> EnsureAsync(
        string runtimeId,
        string dataRoot,
        IProgress<string>? status = null,
        IProgress<PluginDownloadProgress>? downloadProgress = null,
        CancellationToken cancellationToken = default)
    {
        var definition = Supported.FirstOrDefault(runtime => runtime.Id.Equals(runtimeId, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidDataException($"Unsupported managed runtime: {runtimeId}");
        var runtimeRoot = Path.Combine(Path.GetFullPath(dataRoot), "runtimes", definition.Id);
        var python = Path.Combine(runtimeRoot, "python.exe");
        if (File.Exists(python) && await IsRuntimeUsableAsync(python, definition.Version, cancellationToken))
        {
            return python;
        }
        status?.Report(File.Exists(python)
            ? $"Repairing managed Python {definition.Version}"
            : $"Downloading managed Python {definition.Version}");
        var package = await new PluginDownloadService(httpClient).DownloadAsync(
            new VerifiedDownloadRequest(definition.Id, definition.Url, [], definition.Sha256, definition.SizeBytes, ".nupkg"),
            dataRoot,
            downloadProgress,
            cancellationToken);
        status?.Report($"Installing portable managed Python {definition.Version}");
        return await InstallPortableRuntimeAsync(definition, package, runtimeRoot, cancellationToken);
    }

    private static async Task<string> InstallPortableRuntimeAsync(
        ManagedPythonRuntimeDefinition definition,
        string package,
        string runtimeRoot,
        CancellationToken cancellationToken)
    {
        var parent = Path.GetDirectoryName(runtimeRoot) ?? throw new InvalidDataException("Managed Python runtime path has no parent directory.");
        Directory.CreateDirectory(parent);
        var suffix = Guid.NewGuid().ToString("N");
        var stagingRoot = Path.Combine(parent, $".{definition.Id}.staging-{suffix}");
        var backupRoot = Path.Combine(parent, $".{definition.Id}.backup-{suffix}");
        var movedExistingRuntime = false;
        try
        {
            Directory.CreateDirectory(stagingRoot);
            var portableRoot = ExtractPortablePackage(package, stagingRoot);
            var stagedPython = Path.Combine(portableRoot, "python.exe");
            await ValidateRuntimeAsync(stagedPython, definition.Version, cancellationToken);

            if (Directory.Exists(runtimeRoot))
            {
                Directory.Move(runtimeRoot, backupRoot);
                movedExistingRuntime = true;
            }
            Directory.Move(portableRoot, runtimeRoot);
            var installedPython = Path.Combine(runtimeRoot, "python.exe");
            await ValidateRuntimeAsync(installedPython, definition.Version, cancellationToken);
            TryDeleteDirectory(backupRoot);
            return installedPython;
        }
        catch
        {
            if (movedExistingRuntime && Directory.Exists(backupRoot))
            {
                TryDeleteDirectory(runtimeRoot);
                Directory.Move(backupRoot, runtimeRoot);
            }
            throw;
        }
        finally
        {
            TryDeleteDirectory(stagingRoot);
        }
    }

    internal static string ExtractPortablePackage(string package, string stagingRoot)
    {
        ZipFile.ExtractToDirectory(package, stagingRoot);
        var portableRoot = Path.Combine(stagingRoot, "tools");
        if (!File.Exists(Path.Combine(portableRoot, "python.exe")))
        {
            throw new InvalidDataException("The managed Python package does not contain tools/python.exe.");
        }
        return portableRoot;
    }

    private static async Task<bool> IsRuntimeUsableAsync(string python, string expectedVersion, CancellationToken cancellationToken)
    {
        try
        {
            await ValidateRuntimeAsync(python, expectedVersion, cancellationToken);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return false;
        }
    }

    private static async Task ValidateRuntimeAsync(string python, string expectedVersion, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo(python)
        {
            WorkingDirectory = Path.GetDirectoryName(python)!,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        startInfo.ArgumentList.Add("-c");
        startInfo.ArgumentList.Add("import pip, sys, venv; print('.'.join(map(str, sys.version_info[:3])))");
        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start the managed Python runtime validation.");
        var output = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var error = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        var versionText = (await output).Trim();
        var errorText = (await error).Trim();
        if (process.ExitCode != 0 || !Version.TryParse(versionText, out var actualVersion) ||
            actualVersion != Version.Parse(expectedVersion))
        {
            throw new InvalidOperationException(
                $"Managed Python validation failed. Expected {expectedVersion}; exit code {process.ExitCode}; output '{versionText}'; error '{errorText}'.");
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, true);
            }
        }
        catch
        {
            // A valid runtime must remain usable even if obsolete staging files are temporarily locked.
        }
    }
}
