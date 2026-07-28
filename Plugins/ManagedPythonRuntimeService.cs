using System.Diagnostics;

namespace BaChenAiLauncher;

internal sealed record ManagedPythonRuntimeDefinition(
    string Id,
    string Version,
    string Url,
    string Sha256,
    long SizeBytes);

internal sealed class ManagedPythonRuntimeService(HttpClient httpClient)
{
    public static readonly ManagedPythonRuntimeDefinition Python312 = new(
        "python-3.12.10-x64",
        "3.12.10",
        "https://www.python.org/ftp/python/3.12.10/python-3.12.10-amd64.exe",
        "67B5635E80EA51072B87941312D00EC8927C4DB9BA18938F7AD2D27B328B95FB",
        26964224);

    public async Task<string> EnsureAsync(
        string runtimeId,
        string dataRoot,
        IProgress<string>? status = null,
        IProgress<PluginDownloadProgress>? downloadProgress = null,
        CancellationToken cancellationToken = default)
    {
        var definition = runtimeId.Equals(Python312.Id, StringComparison.OrdinalIgnoreCase)
            ? Python312
            : throw new InvalidDataException($"Unsupported managed runtime: {runtimeId}");
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
