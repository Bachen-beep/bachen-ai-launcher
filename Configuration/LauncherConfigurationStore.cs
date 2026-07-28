using System.Text;
using System.Text.Json;

namespace BaChenAiLauncher;

internal static class LauncherConfigurationStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static T LoadOrCreate<T>(string path, Func<T> createDefault) where T : class
    {
        if (!File.Exists(path))
        {
            return createDefault();
        }
        try
        {
            return JsonSerializer.Deserialize<T>(File.ReadAllText(path)) ?? createDefault();
        }
        catch
        {
            ArchiveCorruptFile(path);
            return createDefault();
        }
    }

    public static void SaveAtomic<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporaryPath = path + ".tmp";
        var backupPath = path + ".bak";
        var json = JsonSerializer.Serialize(value, JsonOptions);
        File.WriteAllText(temporaryPath, json, new UTF8Encoding(true));
        _ = JsonSerializer.Deserialize<T>(File.ReadAllText(temporaryPath))
            ?? throw new InvalidDataException($"Serialized configuration validation failed: {path}");
        if (File.Exists(path))
        {
            File.Copy(path, backupPath, true);
        }
        File.Move(temporaryPath, path, true);
    }

    private static void ArchiveCorruptFile(string path)
    {
        try
        {
            var backupRoot = Path.Combine(Path.GetDirectoryName(path)!, "backups", "corrupt-config");
            Directory.CreateDirectory(backupRoot);
            var backupPath = Path.Combine(backupRoot, $"{Path.GetFileNameWithoutExtension(path)}-{DateTime.Now:yyyyMMdd-HHmmss}{Path.GetExtension(path)}");
            File.Move(path, backupPath);
        }
        catch
        {
            // Loading must still fall back to defaults when archiving is unavailable.
        }
    }
}
