using System.Diagnostics;
using System.Text;

namespace BaChenAiLauncher;

internal sealed record ProcessStopFailure(int ProcessId, string Message);
internal sealed record ProcessStopResult(IReadOnlyList<int> StoppedProcessIds, IReadOnlyList<ProcessStopFailure> Failures);

internal static class PluginProcessService
{
    public static List<int> FindProcessesByPluginRoots(IEnumerable<string> pluginRoots)
    {
        var roots = pluginRoots
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(path => "'" + path.Replace("'", "''") + "'");
        var script = $@"
$roots = @({string.Join(",", roots)})
Get-CimInstance Win32_Process | Where-Object {{
    $processCommandLine = $_.CommandLine
    $processExecutable = $_.ExecutablePath
    $roots | Where-Object {{
        ($processCommandLine -and $processCommandLine.IndexOf($_, [System.StringComparison]::OrdinalIgnoreCase) -ge 0) -or
        ($processExecutable -and $processExecutable.IndexOf($_, [System.StringComparison]::OrdinalIgnoreCase) -eq 0)
    }}
}} | Select-Object -ExpandProperty ProcessId
";
        return RunPowerShellForPids(script);
    }

    public static List<int> GetListeningProcessIds(int port)
    {
        try
        {
            var startInfo = new ProcessStartInfo("netstat.exe", "-ano -p tcp")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true
            };
            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return [];
            }
            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(3000);
            var suffix = $":{port}";
            return output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
                .Where(line => line.Contains("LISTENING", StringComparison.OrdinalIgnoreCase))
                .Where(line => line.Split(' ', StringSplitOptions.RemoveEmptyEntries).Skip(1).FirstOrDefault()?.EndsWith(suffix, StringComparison.Ordinal) == true)
                .Select(line => line.Split(' ', StringSplitOptions.RemoveEmptyEntries).LastOrDefault())
                .Select(value => int.TryParse(value, out var pid) ? pid : 0)
                .Where(pid => pid > 0)
                .Distinct()
                .ToList();
        }
        catch
        {
            return [];
        }
    }

    public static ProcessStopResult Stop(IReadOnlyCollection<int> processIds)
    {
        var stopped = new List<int>();
        var failures = new List<ProcessStopFailure>();
        foreach (var processId in processIds.Distinct())
        {
            try
            {
                using var process = Process.GetProcessById(processId);
                process.Kill(true);
                if (!process.WaitForExit(5000))
                {
                    throw new TimeoutException($"Process {processId} did not exit within 5 seconds.");
                }
                stopped.Add(processId);
            }
            catch (ArgumentException)
            {
                // The process already stopped.
            }
            catch (Exception ex)
            {
                failures.Add(new ProcessStopFailure(processId, ex.Message));
            }
        }
        return new ProcessStopResult(stopped, failures);
    }

    private static List<int> RunPowerShellForPids(string script)
    {
        try
        {
            var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
            var startInfo = new ProcessStartInfo("powershell.exe", $"-NoProfile -NonInteractive -EncodedCommand {encoded}")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return [];
            }
            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(4000))
            {
                process.Kill(true);
                return [];
            }
            var output = outputTask.GetAwaiter().GetResult();
            _ = errorTask.GetAwaiter().GetResult();
            return output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
                .Select(line => int.TryParse(line.Trim(), out var pid) ? pid : 0)
                .Where(pid => pid > 0 && pid != Environment.ProcessId)
                .Distinct()
                .ToList();
        }
        catch
        {
            return [];
        }
    }
}
