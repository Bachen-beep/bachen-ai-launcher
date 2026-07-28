using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;

namespace BaChenAiLauncher;

internal sealed record LauncherUpdateCheck(LauncherUpdateManifest Manifest, Version CurrentVersion, Version LatestVersion)
{
    public bool IsUpdateAvailable => LatestVersion > CurrentVersion;
}

internal sealed class LauncherSelfUpdateService(HttpClient client)
{
    public static readonly Uri DefaultManifestUri = new("https://github.com/Bachen-beep/bachen-ai-launcher/releases/latest/download/launcher-update.json");
    private static readonly Uri ReleasesApiUri = new("https://api.github.com/repos/Bachen-beep/bachen-ai-launcher/releases?per_page=20");

    public async Task<LauncherUpdateCheck> CheckAsync(LauncherUpdateChannel channel = LauncherUpdateChannel.Stable, Uri? manifestUri = null)
    {
        var resolvedManifestUri = manifestUri ?? await ResolveManifestUriAsync(channel);
        var json = await client.GetStringAsync(resolvedManifestUri);
        var manifest = JsonSerializer.Deserialize<LauncherUpdateManifest>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidDataException("The launcher update manifest is empty.");
        LauncherUpdateManifestVerifier.Validate(manifest, ReadEmbeddedPublicKey());
        var current = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0);
        var latest = Version.Parse(manifest.Version);
        var minimum = Version.Parse(manifest.MinimumCompatibleVersion);
        if (current < minimum)
        {
            throw new InvalidOperationException($"This update requires launcher {minimum} or newer. Install the latest setup package manually.");
        }
        return new LauncherUpdateCheck(manifest, current, latest);
    }

    internal async Task<Uri> ResolveManifestUriAsync(LauncherUpdateChannel channel)
    {
        if (channel == LauncherUpdateChannel.Stable)
        {
            return DefaultManifestUri;
        }

        await using var stream = await client.GetStreamAsync(ReleasesApiUri);
        using var document = await JsonDocument.ParseAsync(stream);
        foreach (var release in document.RootElement.EnumerateArray())
        {
            if (release.GetProperty("draft").GetBoolean() || !release.GetProperty("prerelease").GetBoolean())
            {
                continue;
            }
            foreach (var asset in release.GetProperty("assets").EnumerateArray())
            {
                if (!asset.GetProperty("name").GetString()!.Equals("launcher-update.json", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                var downloadUrl = asset.GetProperty("browser_download_url").GetString();
                if (Uri.TryCreate(downloadUrl, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps)
                {
                    return uri;
                }
            }
        }
        throw new InvalidOperationException("No preview launcher release with an update manifest is available.");
    }

    public async Task<string> DownloadVerifiedAsync(LauncherUpdateManifest manifest)
    {
        var updateRoot = Path.Combine(Path.GetTempPath(), "bachen-launcher-update-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(updateRoot);
        var packagePath = Path.Combine(updateRoot, "BaChen AI Launcher.exe");
        using var response = await client.GetAsync(manifest.DownloadUrl, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();
        var requiredBytes = response.Content.Headers.ContentLength ?? 150L * 1024 * 1024;
        var drive = new DriveInfo(Path.GetPathRoot(updateRoot)!);
        if (drive.AvailableFreeSpace < requiredBytes + 100L * 1024 * 1024)
        {
            Directory.Delete(updateRoot, true);
            throw new IOException($"Insufficient disk space. At least {(requiredBytes + 100L * 1024 * 1024) / 1024 / 1024} MiB must be available.");
        }
        await using (var input = await response.Content.ReadAsStreamAsync())
        await using (var output = File.Create(packagePath))
        {
            await input.CopyToAsync(output);
        }
        await using var package = File.OpenRead(packagePath);
        var actual = Convert.ToHexString(await SHA256.HashDataAsync(package));
        if (!actual.Equals(manifest.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            Directory.Delete(updateRoot, true);
            throw new InvalidDataException("The downloaded launcher hash does not match the signed update manifest.");
        }
        return packagePath;
    }

    public static void BeginApply(string packagePath, LauncherUpdateManifest manifest)
    {
        var targetPath = Environment.ProcessPath ?? throw new InvalidOperationException("The launcher executable path is unavailable.");
        if (!targetPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Self-update is available only from the published launcher EXE.");
        }
        var helperPath = Path.Combine(Path.GetDirectoryName(packagePath)!, "BaChen AI Launcher Updater.exe");
        File.Copy(targetPath, helperPath, true);
        _ = Process.Start(new ProcessStartInfo
        {
            FileName = helperPath,
            UseShellExecute = false,
            ArgumentList =
            {
                "--apply-update", targetPath, packagePath, Environment.ProcessId.ToString(),
                manifest.Sha256, manifest.Version
            }
        }) ?? throw new InvalidOperationException("Unable to start the launcher update helper.");
    }

    public static string? GetRollbackPath()
    {
        var path = Path.Combine(LauncherPaths.UserConfigDirectory, "updates", "backup", "BaChen AI Launcher.previous.exe");
        return File.Exists(path) ? path : null;
    }

    public static async Task BeginRollbackAsync()
    {
        var backupPath = GetRollbackPath() ?? throw new FileNotFoundException("No previous launcher backup is available.");
        await using var stream = File.OpenRead(backupPath);
        var sha256 = Convert.ToHexString(await SHA256.HashDataAsync(stream));
        BeginApply(backupPath, new LauncherUpdateManifest { Version = "rollback", Sha256 = sha256 });
    }

    public static async Task<int> ApplyUpdateAsync(string targetPath, string packagePath, int processId, string expectedSha256, string version)
    {
        try
        {
            if (!Path.GetFileName(targetPath).Equals("BaChen AI Launcher.exe", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("The update target is not a BaChen AI Launcher executable.");
            }
            try
            {
                using var process = Process.GetProcessById(processId);
                await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30));
            }
            catch (ArgumentException)
            {
                // The launcher already exited.
            }
            await using (var stream = File.OpenRead(packagePath))
            {
                var actual = Convert.ToHexString(await SHA256.HashDataAsync(stream));
                if (!actual.Equals(expectedSha256, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException("The staged update hash changed before installation.");
                }
            }
            var backupDirectory = Path.Combine(LauncherPaths.UserConfigDirectory, "updates", "backup");
            Directory.CreateDirectory(backupDirectory);
            var backupPath = Path.Combine(backupDirectory, "BaChen AI Launcher.previous.exe");
            var replacementPath = targetPath + ".new";
            File.Copy(packagePath, replacementPath, true);
            File.Copy(targetPath, backupPath, true);
            try
            {
                File.Move(replacementPath, targetPath, true);
            }
            catch
            {
                File.Copy(backupPath, targetPath, true);
                throw;
            }
            Process.Start(new ProcessStartInfo(targetPath, $"--update-complete {version}") { UseShellExecute = true });
            return 0;
        }
        catch (Exception ex)
        {
            try
            {
                Directory.CreateDirectory(Path.Combine(LauncherPaths.UserConfigDirectory, "logs"));
                await File.AppendAllTextAsync(Path.Combine(LauncherPaths.UserConfigDirectory, "logs", "update-error.log"), $"[{DateTimeOffset.Now:O}] {ex}\n");
            }
            catch { }
            return 1;
        }
    }

    public static string ReadEmbeddedPublicKey()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("BaChenAiLauncher.UpdatePublicKey")
            ?? throw new InvalidOperationException("The launcher update public key is missing.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
