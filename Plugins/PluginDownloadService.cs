using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;

namespace BaChenAiLauncher;

internal sealed record PluginDownloadProgress(long BytesReceived, long? TotalBytes, double BytesPerSecond)
{
    public int Percentage => TotalBytes is > 0 ? (int)Math.Clamp(BytesReceived * 100L / TotalBytes.Value, 0, 100) : 0;
}

internal sealed record VerifiedDownloadRequest(
    string CacheKey,
    string Url,
    string[] Mirrors,
    string Sha256,
    long SizeBytes,
    string FileExtension = ".zip");

internal sealed class PluginDownloadService(HttpClient httpClient)
{
    public async Task<string> DownloadAsync(
        PluginPackageManifest manifest,
        string dataRoot,
        IProgress<PluginDownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
        => await DownloadAsync(
            new VerifiedDownloadRequest($"{manifest.Id}-{manifest.Version}", manifest.PackageUrl, manifest.PackageMirrors ?? [], manifest.PackageSha256, manifest.PackageSizeBytes),
            dataRoot,
            progress,
            cancellationToken);

    public async Task<string> DownloadAssetAsync(
        string pluginId,
        PluginAssetPackage asset,
        string dataRoot,
        IProgress<PluginDownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
        => await DownloadAsync(
            new VerifiedDownloadRequest($"{pluginId}-{asset.Id}", asset.Url, asset.Mirrors ?? [], asset.Sha256, asset.SizeBytes),
            dataRoot,
            progress,
            cancellationToken);

    public async Task<string> DownloadAsync(
        VerifiedDownloadRequest request,
        string dataRoot,
        IProgress<PluginDownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var downloadsRoot = Path.GetFullPath(Path.Combine(dataRoot, "downloads"));
        Directory.CreateDirectory(downloadsRoot);
        var extension = request.FileExtension.StartsWith('.') ? request.FileExtension : "." + request.FileExtension;
        var destination = Path.Combine(downloadsRoot, SafeFileName(request.CacheKey) + extension);
        if (File.Exists(destination) && (request.SizeBytes <= 0 || new FileInfo(destination).Length == request.SizeBytes))
        {
            if (await HasExpectedHashAsync(destination, request.Sha256, cancellationToken))
            {
                progress?.Report(new PluginDownloadProgress(new FileInfo(destination).Length, request.SizeBytes, 0));
                return destination;
            }
            File.Delete(destination);
        }

        var sources = new[] { request.Url }.Concat(request.Mirrors ?? [])
            .Where(value => Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(value => new Uri(value))
            .ToArray();
        if (sources.Length == 0)
        {
            throw new InvalidDataException("The plugin manifest does not contain an HTTPS package source.");
        }

        Exception? lastError = null;
        foreach (var source in sources)
        {
            for (var attempt = 1; attempt <= 3; attempt++)
            {
                try
                {
                    var completed = await DownloadSourceAsync(source, destination, request.SizeBytes, progress, cancellationToken);
                    if (!await HasExpectedHashAsync(completed, request.Sha256, cancellationToken))
                    {
                        File.Delete(completed);
                        throw new IOException("Downloaded package SHA-256 does not match the signed manifest.");
                    }
                    return completed;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex) when (ex is HttpRequestException or IOException)
                {
                    lastError = ex;
                    if (attempt < 3)
                    {
                        await Task.Delay(TimeSpan.FromMilliseconds(300 * attempt), cancellationToken);
                    }
                }
            }
        }
        throw new IOException("All plugin package sources failed after retrying.", lastError);
    }

    private async Task<string> DownloadSourceAsync(
        Uri source,
        string destination,
        long expectedSize,
        IProgress<PluginDownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        var partialPath = destination + ".partial";
        var existingLength = File.Exists(partialPath) ? new FileInfo(partialPath).Length : 0;
        if (expectedSize > 0 && existingLength == expectedSize)
        {
            File.Move(partialPath, destination, true);
            progress?.Report(new PluginDownloadProgress(expectedSize, expectedSize, 0));
            return destination;
        }
        if (expectedSize > 0 && existingLength > expectedSize)
        {
            File.Delete(partialPath);
            existingLength = 0;
        }
        using var request = new HttpRequestMessage(HttpMethod.Get, source);
        if (existingLength > 0)
        {
            request.Headers.Range = new RangeHeaderValue(existingLength, null);
        }
        using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (response.RequestMessage?.RequestUri is Uri finalUri && finalUri.Scheme != Uri.UriSchemeHttps)
        {
            throw new HttpRequestException("The package download was redirected to a non-HTTPS address.");
        }
        if (response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable && expectedSize > 0 && existingLength == expectedSize)
        {
            File.Move(partialPath, destination, true);
            return destination;
        }
        response.EnsureSuccessStatusCode();

        var append = existingLength > 0 && response.StatusCode == HttpStatusCode.PartialContent;
        if (!append)
        {
            existingLength = 0;
        }
        var responseLength = response.Content.Headers.ContentLength;
        var total = expectedSize > 0 ? expectedSize : responseLength is null ? null : existingLength + responseLength;
        await using var sourceStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var destinationStream = new FileStream(partialPath, append ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.Read, 1024 * 128, true);
        var buffer = new byte[1024 * 128];
        var received = existingLength;
        var stopwatch = Stopwatch.StartNew();
        var intervalBytes = received;
        var intervalStart = stopwatch.Elapsed;
        while (true)
        {
            var count = await sourceStream.ReadAsync(buffer, cancellationToken);
            if (count == 0)
            {
                break;
            }
            await destinationStream.WriteAsync(buffer.AsMemory(0, count), cancellationToken);
            received += count;
            var elapsed = stopwatch.Elapsed - intervalStart;
            if (elapsed >= TimeSpan.FromMilliseconds(200))
            {
                var speed = (received - intervalBytes) / Math.Max(0.001, elapsed.TotalSeconds);
                progress?.Report(new PluginDownloadProgress(received, total, speed));
                intervalBytes = received;
                intervalStart = stopwatch.Elapsed;
            }
        }
        await destinationStream.FlushAsync(cancellationToken);
        if (expectedSize > 0 && received != expectedSize)
        {
            throw new IOException($"Downloaded package size mismatch. Expected {expectedSize} bytes; received {received} bytes.");
        }
        progress?.Report(new PluginDownloadProgress(received, total ?? received, 0));
        await destinationStream.DisposeAsync();
        File.Move(partialPath, destination, true);
        return destination;
    }

    private static string SafeFileName(string value)
        => string.Concat(value.Select(character => Path.GetInvalidFileNameChars().Contains(character) ? '-' : character));

    private static async Task<bool> HasExpectedHashAsync(string path, string expectedHash, CancellationToken cancellationToken)
    {
        if (expectedHash.Length != 64)
        {
            return false;
        }
        await using var stream = File.OpenRead(path);
        var hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken));
        return hash.Equals(expectedHash, StringComparison.OrdinalIgnoreCase);
    }
}
