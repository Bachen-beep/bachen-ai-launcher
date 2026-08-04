using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;

namespace BaChenAiLauncher;

internal sealed record LauncherUpdateSourceResult(
    string Name,
    Uri Uri,
    LauncherUpdateManifest? Manifest,
    string Detail)
{
    public bool IsValid => Manifest is not null;
    public Version? Version => Manifest is null ? null : Version.Parse(Manifest.Version);
}

internal sealed record LauncherUpdateCheck(
    LauncherUpdateManifest Manifest,
    Version CurrentVersion,
    Version LatestVersion,
    string SelectedSource,
    IReadOnlyList<LauncherUpdateSourceResult> Sources)
{
    public bool IsUpdateAvailable => LatestVersion > CurrentVersion;
    public bool HasSourceConflict => Sources.Where(source => source.IsValid).Select(source => source.Version).Distinct().Skip(1).Any();
}

internal sealed class LauncherUpdateUnavailableException(LauncherUpdateChannel channel, Exception? innerException = null)
    : InvalidOperationException($"No {channel.ToString().ToLowerInvariant()} launcher release is currently available.", innerException)
{
    public LauncherUpdateChannel Channel { get; } = channel;
}

internal sealed class LauncherUpdateStaleException(Version highestObservedVersion, Version remoteVersion)
    : InvalidOperationException($"The highest valid remote version {remoteVersion} is older than the previously observed version {highestObservedVersion}.")
{
    public Version HighestObservedVersion { get; } = highestObservedVersion;
    public Version RemoteVersion { get; } = remoteVersion;
}

internal sealed record LauncherUpdateSource(string Name, Uri Uri, bool IsReleaseApi = false);

internal enum LauncherUpdateProgressStage
{
    Downloading,
    Verifying
}

internal sealed record LauncherUpdateProgress(
    LauncherUpdateProgressStage Stage,
    long CompletedBytes,
    long? TotalBytes,
    double? BytesPerSecond = null);

internal sealed class LauncherSelfUpdateService
{
    public static readonly Uri DefaultManifestUri = new("https://github.com/Bachen-beep/bachen-ai-launcher/releases/latest/download/launcher-update.json");
    public static readonly Uri StableFeedUri = new("https://raw.githubusercontent.com/Bachen-beep/bachen-ai-launcher/update-feed/stable.json");
    public static readonly Uri LatestReleaseApiUri = new("https://api.github.com/repos/Bachen-beep/bachen-ai-launcher/releases/latest");
    private static readonly Uri ReleasesApiUri = new("https://api.github.com/repos/Bachen-beep/bachen-ai-launcher/releases?per_page=20");
    private readonly HttpClient client;
    private readonly Func<string> publicKeyProvider;
    private readonly IReadOnlyList<LauncherUpdateSource> stableSources;

    public LauncherSelfUpdateService(HttpClient client)
        : this(client, ReadEmbeddedPublicKey, null)
    {
    }

    internal LauncherSelfUpdateService(
        HttpClient client,
        Func<string> publicKeyProvider,
        IReadOnlyList<LauncherUpdateSource>? stableSources)
    {
        this.client = client;
        this.publicKeyProvider = publicKeyProvider;
        this.stableSources = stableSources ??
        [
            new LauncherUpdateSource("GitHub Raw", StableFeedUri),
            new LauncherUpdateSource("GitHub Latest", DefaultManifestUri),
            new LauncherUpdateSource("GitHub Release API", LatestReleaseApiUri, true)
        ];
    }

    public async Task<LauncherUpdateCheck> CheckAsync(
        LauncherUpdateChannel channel = LauncherUpdateChannel.Stable,
        Uri? manifestUri = null,
        Version? highestObservedVersion = null)
    {
        IReadOnlyList<LauncherUpdateSourceResult> results;
        if (manifestUri is not null)
        {
            results = [await FetchManifestSourceAsync(new LauncherUpdateSource("Custom", manifestUri))];
        }
        else if (channel == LauncherUpdateChannel.Stable)
        {
            results = await FetchStableSourcesAsync(highestObservedVersion);
        }
        else
        {
            try
            {
                var previewUri = await ResolveManifestUriAsync(LauncherUpdateChannel.Preview);
                results = [await FetchManifestSourceAsync(new LauncherUpdateSource("GitHub Preview", previewUri))];
            }
            catch (HttpRequestException ex)
            {
                var stableResults = await FetchStableSourcesAsync(highestObservedVersion);
                results =
                [
                    new LauncherUpdateSourceResult("GitHub Preview API", ReleasesApiUri, null, $"fallback: {ex.Message}"),
                    .. stableResults
                ];
            }
        }

        var selected = results
            .Where(result => result.Manifest is not null)
            .OrderByDescending(result => result.Version)
            .ThenByDescending(result => result.Manifest!.PublishedAt)
            .FirstOrDefault();
        if (selected?.Manifest is null)
        {
            var details = string.Join(" | ", results.Select(result => $"{result.Name}: {result.Detail}"));
            throw new LauncherUpdateUnavailableException(channel, new HttpRequestException(details));
        }

        var manifest = selected.Manifest;
        var current = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0);
        var latest = Version.Parse(manifest.Version);
        if (channel == LauncherUpdateChannel.Stable && highestObservedVersion is not null && latest < highestObservedVersion)
        {
            throw new LauncherUpdateStaleException(highestObservedVersion, latest);
        }
        var minimum = Version.Parse(manifest.MinimumCompatibleVersion);
        if (current < minimum)
        {
            throw new InvalidOperationException($"This update requires launcher {minimum} or newer. Install the latest setup package manually.");
        }
        return new LauncherUpdateCheck(manifest, current, latest, selected.Name, results);
    }

    private async Task<IReadOnlyList<LauncherUpdateSourceResult>> FetchStableSourcesAsync(Version? highestObservedVersion)
    {
        var directSources = stableSources.Where(source => !source.IsReleaseApi).ToArray();
        var apiSources = stableSources.Where(source => source.IsReleaseApi).ToArray();
        var results = (await Task.WhenAll(directSources.Select(FetchManifestSourceAsync))).ToList();
        var latestDirectVersion = results
            .Where(result => result.Version is not null)
            .Select(result => result.Version!)
            .DefaultIfEmpty()
            .Max();
        var needsApiFallback = latestDirectVersion is null ||
            (highestObservedVersion is not null && latestDirectVersion < highestObservedVersion);

        if (needsApiFallback)
        {
            results.AddRange(await Task.WhenAll(apiSources.Select(FetchManifestSourceAsync)));
        }
        else
        {
            results.AddRange(apiSources.Select(source => new LauncherUpdateSourceResult(
                source.Name,
                source.Uri,
                null,
                "skipped: signed direct source available")));
        }
        return results;
    }

    private async Task<LauncherUpdateSourceResult> FetchManifestSourceAsync(LauncherUpdateSource source)
    {
        try
        {
            var manifestUri = source.IsReleaseApi ? await ResolveStableReleaseManifestUriAsync(source.Uri) : source.Uri;
            using var request = CreateFreshRequest(manifestUri);
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync();
            var manifest = JsonSerializer.Deserialize<LauncherUpdateManifest>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? throw new InvalidDataException("The launcher update manifest is empty.");
            LauncherUpdateManifestVerifier.Validate(manifest, publicKeyProvider());
            return new LauncherUpdateSourceResult(source.Name, manifestUri, manifest, "valid");
        }
        catch (Exception ex)
        {
            return new LauncherUpdateSourceResult(source.Name, source.Uri, null, ex.Message);
        }
    }

    private async Task<Uri> ResolveStableReleaseManifestUriAsync(Uri apiUri)
    {
        using var request = CreateFreshRequest(apiUri);
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync();
        using var document = await JsonDocument.ParseAsync(stream);
        foreach (var asset in document.RootElement.GetProperty("assets").EnumerateArray())
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
        throw new LauncherUpdateUnavailableException(LauncherUpdateChannel.Stable);
    }

    private static HttpRequestMessage CreateFreshRequest(Uri uri)
    {
        var builder = new UriBuilder(uri);
        var separator = string.IsNullOrEmpty(builder.Query) ? string.Empty : builder.Query.TrimStart('?') + "&";
        builder.Query = separator + "bachen_request=" + Guid.NewGuid().ToString("N");
        var request = new HttpRequestMessage(HttpMethod.Get, builder.Uri);
        request.Headers.CacheControl = new System.Net.Http.Headers.CacheControlHeaderValue
        {
            NoCache = true,
            NoStore = true
        };
        request.Headers.Pragma.ParseAdd("no-cache");
        return request;
    }

    internal async Task<Uri> ResolveManifestUriWithFallbackAsync(LauncherUpdateChannel channel)
    {
        try
        {
            return await ResolveManifestUriAsync(channel);
        }
        catch (HttpRequestException) when (channel == LauncherUpdateChannel.Preview)
        {
            return DefaultManifestUri;
        }
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
        throw new LauncherUpdateUnavailableException(LauncherUpdateChannel.Preview);
    }

    public Task<string> DownloadVerifiedAsync(LauncherUpdateManifest manifest, IProgress<LauncherUpdateProgress>? progress = null)
        => DownloadVerifiedAsync(manifest, null, progress);

    internal async Task<string> DownloadVerifiedAsync(
        LauncherUpdateManifest manifest,
        string? updateRootOverride,
        IProgress<LauncherUpdateProgress>? progress = null)
    {
        var updateRoot = updateRootOverride ?? Path.Combine(Path.GetTempPath(), "bachen-launcher-update-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(updateRoot);
        var packagePath = Path.Combine(updateRoot, "BaChen AI Launcher.exe");
        try
        {
            using var response = await client.GetAsync(manifest.DownloadUrl, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();
            var contentLength = response.Content.Headers.ContentLength;
            var requiredBytes = contentLength ?? 150L * 1024 * 1024;
            var drive = new DriveInfo(Path.GetPathRoot(updateRoot)!);
            if (drive.AvailableFreeSpace < requiredBytes + 100L * 1024 * 1024)
            {
                throw new IOException($"Insufficient disk space. At least {(requiredBytes + 100L * 1024 * 1024) / 1024 / 1024} MiB must be available.");
            }
            progress?.Report(new LauncherUpdateProgress(LauncherUpdateProgressStage.Downloading, 0, contentLength));
            await using (var input = await response.Content.ReadAsStreamAsync())
            await using (var output = File.Create(packagePath))
            {
                var buffer = new byte[128 * 1024];
                long downloadedBytes = 0;
                var lastReportedBytes = 0L;
                var downloadTimer = Stopwatch.StartNew();
                var lastReportedAt = TimeSpan.Zero;
                while (true)
                {
                    var read = await input.ReadAsync(buffer);
                    if (read == 0)
                    {
                        break;
                    }
                    await output.WriteAsync(buffer.AsMemory(0, read));
                    downloadedBytes += read;
                    if (downloadedBytes - lastReportedBytes >= 256 * 1024 || contentLength == downloadedBytes)
                    {
                        var reportedAt = downloadTimer.Elapsed;
                        var elapsedSeconds = (reportedAt - lastReportedAt).TotalSeconds;
                        var bytesPerSecond = elapsedSeconds > 0
                            ? (downloadedBytes - lastReportedBytes) / elapsedSeconds
                            : (double?)null;
                        progress?.Report(new LauncherUpdateProgress(LauncherUpdateProgressStage.Downloading, downloadedBytes, contentLength, bytesPerSecond));
                        lastReportedBytes = downloadedBytes;
                        lastReportedAt = reportedAt;
                    }
                }
            }
            await using var package = File.OpenRead(packagePath);
            var packageLength = package.Length;
            progress?.Report(new LauncherUpdateProgress(LauncherUpdateProgressStage.Verifying, 0, packageLength));
            using var sha256 = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var hashBuffer = new byte[128 * 1024];
            long hashedBytes = 0;
            var lastHashedReport = 0L;
            while (true)
            {
                var read = await package.ReadAsync(hashBuffer);
                if (read == 0)
                {
                    break;
                }
                sha256.AppendData(hashBuffer, 0, read);
                hashedBytes += read;
                if (hashedBytes - lastHashedReport >= 256 * 1024 || hashedBytes == packageLength)
                {
                    progress?.Report(new LauncherUpdateProgress(LauncherUpdateProgressStage.Verifying, hashedBytes, packageLength));
                    lastHashedReport = hashedBytes;
                }
            }
            var actual = Convert.ToHexString(sha256.GetHashAndReset());
            if (!actual.Equals(manifest.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("The downloaded launcher hash does not match the signed update manifest.");
            }
            return packagePath;
        }
        catch
        {
            TryDeleteDirectory(updateRoot);
            throw;
        }
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
            var backupDirectory = Path.Combine(LauncherPaths.UserConfigDirectory, "updates", "backup");
            Directory.CreateDirectory(backupDirectory);
            var backupPath = Path.Combine(backupDirectory, "BaChen AI Launcher.previous.exe");
            await ApplyUpdateFilesAsync(targetPath, packagePath, expectedSha256, backupPath);
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

    internal static async Task ApplyUpdateFilesAsync(
        string targetPath,
        string packagePath,
        string expectedSha256,
        string backupPath,
        Action<string, string, bool>? moveFile = null)
    {
        await using (var stream = File.OpenRead(packagePath))
        {
            var actual = Convert.ToHexString(await SHA256.HashDataAsync(stream));
            if (!actual.Equals(expectedSha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("The staged update hash changed before installation.");
            }
        }

        Directory.CreateDirectory(Path.GetDirectoryName(backupPath)!);
        var replacementPath = targetPath + ".new";
        File.Copy(packagePath, replacementPath, true);
        File.Copy(targetPath, backupPath, true);
        try
        {
            (moveFile ?? File.Move)(replacementPath, targetPath, true);
        }
        catch
        {
            File.Copy(backupPath, targetPath, true);
            throw;
        }
        finally
        {
            if (File.Exists(replacementPath))
            {
                File.Delete(replacementPath);
            }
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
            // Cleanup must not hide the original update failure.
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
