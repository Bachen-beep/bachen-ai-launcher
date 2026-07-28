using System.Text;
using System.Text.Json;

namespace BaChenAiLauncher;

internal sealed class LauncherDiagnosticsService
{
    private const long MaxLogBytes = 2 * 1024 * 1024;
    private static readonly string LogDirectory = Path.Combine(LauncherPaths.UserConfigDirectory, "logs");

    public void Append(string message, string? serviceName, bool isError)
    {
        try
        {
            Directory.CreateDirectory(LogDirectory);
            var path = Path.Combine(LogDirectory, "launcher.log");
            if (File.Exists(path) && new FileInfo(path).Length > MaxLogBytes)
            {
                File.Move(path, Path.Combine(LogDirectory, $"launcher-{DateTime.Now:yyyyMMdd-HHmmss}.log"), true);
                foreach (var oldLog in Directory.EnumerateFiles(LogDirectory, "launcher-*.log").OrderByDescending(File.GetLastWriteTimeUtc).Skip(5)) File.Delete(oldLog);
            }
            File.AppendAllText(path, JsonSerializer.Serialize(new { timestamp = DateTimeOffset.Now, service = serviceName, error = isError, message }) + Environment.NewLine, Encoding.UTF8);
        }
        catch { }
    }

    public string Export(string outputPath, IEnumerable<string> lines)
    {
        var fullPath = Path.GetFullPath(outputPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllLines(fullPath, lines.Select(Redact), new UTF8Encoding(false));
        return fullPath;
    }

    public IEnumerable<string> ReadPersistentLogs()
    {
        foreach (var name in new[] { "launcher.log", "crash.log", "update-error.log" })
        {
            var path = Path.Combine(LogDirectory, name);
            if (!File.Exists(path)) continue;
            yield return $"--- {name} ---";
            foreach (var line in File.ReadLines(path).TakeLast(2000)) yield return line;
        }
    }

    private static string Redact(string value)
    {
        var result = value;
        foreach (var variable in new[] { "HF_TOKEN", "HUGGINGFACE_TOKEN", "BACHEN_AI_CONFIG_DIR", "BACHEN_AI_DATA_ROOT" })
        {
            var secret = Environment.GetEnvironmentVariable(variable);
            if (!string.IsNullOrWhiteSpace(secret)) result = result.Replace(secret, "[REDACTED]", StringComparison.Ordinal);
        }
        return System.Text.RegularExpressions.Regex.Replace(result, @"(?i)(token|password|secret|api[_-]?key)\s*[:=]\s*[^\s;]+", "$1=[REDACTED]");
    }
}
