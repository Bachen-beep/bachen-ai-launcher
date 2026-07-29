using System.Diagnostics;
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
        "https://www.python.org/ftp/python/3.11.9/python-3.11.9-amd64.exe",
        "5EE42C4EEE1E6B4464BB23722F90B45303F79442DF63083F05322F1785F5FDDE",
        26216840);

    public static readonly ManagedPythonRuntimeDefinition Python312 = new(
        "python-3.12.10-x64",
        "3.12.10",
        "https://www.python.org/ftp/python/3.12.10/python-3.12.10-amd64.exe",
        "67B5635E80EA51072B87941312D00EC8927C4DB9BA18938F7AD2D27B328B95FB",
        26964224);

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
        if (File.Exists(python))
        {
            return python;
        }
        status?.Report($"Downloading managed Python {definition.Version}");
        var installer = await new PluginDownloadService(httpClient).DownloadAsync(
            new VerifiedDownloadRequest(definition.Id, definition.Url, [], definition.Sha256, definition.SizeBytes, ".exe"),
            dataRoot,
            downloadProgress,
            cancellationToken);
        Directory.CreateDirectory(runtimeRoot);
        status?.Report($"Installing managed Python {definition.Version}");
        var startInfo = new ProcessStartInfo(installer)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var argument in new[]
        {
            "/quiet",
            "InstallAllUsers=0",
            $"TargetDir={runtimeRoot}",
            "Include_pip=1",
            "Include_launcher=0",
            "Include_test=0",
            "AssociateFiles=0",
            "Shortcuts=0",
            "PrependPath=0"
        })
        {
            startInfo.ArgumentList.Add(argument);
        }
        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start the managed Python installer.");
        var output = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var error = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        if (process.ExitCode != 0 || !File.Exists(python))
        {
            throw new InvalidOperationException($"Managed Python installation failed ({process.ExitCode}). {await error}\n{await output}".Trim());
        }
        return python;
    }
}
