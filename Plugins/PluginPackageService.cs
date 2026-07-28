using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace BaChenAiLauncher;

internal sealed class PluginPackageService(HttpClient httpClient)
{
    public async Task<PluginInstallResult> InstallAsync(PluginPackageManifest manifest, string? localPackagePath, string dataRoot, CancellationToken cancellationToken = default)
    {
        ValidateManifest(manifest);
        var pluginsRoot = Path.GetFullPath(Path.Combine(dataRoot, "plugins"));
        var downloadsRoot = Path.GetFullPath(Path.Combine(dataRoot, "downloads"));
        var backupsRoot = Path.GetFullPath(Path.Combine(dataRoot, "backups", "plugin-installs"));
        Directory.CreateDirectory(pluginsRoot);
        Directory.CreateDirectory(downloadsRoot);
        Directory.CreateDirectory(backupsRoot);

        var safeId = SanitizeId(manifest.Id);
        var packagePath = localPackagePath;
        var downloadedPackage = false;
        if (string.IsNullOrWhiteSpace(packagePath))
        {
            if (!Uri.TryCreate(manifest.PackageUrl, UriKind.Absolute, out var packageUri) || packageUri.Scheme != Uri.UriSchemeHttps)
            {
                throw new InvalidDataException("The manifest must provide an HTTPS packageUrl when no local ZIP is selected.");
            }
            packagePath = Path.Combine(downloadsRoot, $"{safeId}-{manifest.Version}-{Guid.NewGuid():N}.zip");
            await using var source = await httpClient.GetStreamAsync(packageUri, cancellationToken);
            await using var destination = File.Create(packagePath);
            await source.CopyToAsync(destination, cancellationToken);
            downloadedPackage = true;
        }

        packagePath = Path.GetFullPath(packagePath);
        if (!File.Exists(packagePath) || !Path.GetExtension(packagePath).Equals(".zip", StringComparison.OrdinalIgnoreCase))
        {
            throw new FileNotFoundException("Plugin package ZIP was not found.", packagePath);
        }
        var packageHash = await ComputeSha256Async(packagePath, cancellationToken);
        if (!packageHash.Equals(manifest.PackageSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"Package SHA-256 mismatch. Expected {manifest.PackageSha256}; actual {packageHash}.");
        }

        var stagingRoot = Path.Combine(downloadsRoot, $"install-{safeId}-{Guid.NewGuid():N}");
        var targetRoot = Path.Combine(pluginsRoot, safeId);
        string? backupPath = null;
        try
        {
            Directory.CreateDirectory(stagingRoot);
            ExtractSecurely(packagePath, stagingRoot);
            var contentRoot = ResolveContentRoot(stagingRoot);
            ValidateInstalledFiles(manifest, contentRoot);

            if (Directory.Exists(targetRoot))
            {
                backupPath = Path.Combine(backupsRoot, $"{safeId}-{DateTime.Now:yyyyMMdd-HHmmss}");
                Directory.Move(targetRoot, backupPath);
            }
            Directory.Move(contentRoot, targetRoot);
            File.WriteAllText(
                Path.Combine(targetRoot, ".bachen-plugin-manifest.json"),
                JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase }),
                Encoding.UTF8);
            if (!contentRoot.Equals(stagingRoot, StringComparison.OrdinalIgnoreCase) && Directory.Exists(stagingRoot))
            {
                Directory.Delete(stagingRoot, true);
            }

            return new PluginInstallResult(new LauncherModelDefinition
            {
                Id = safeId,
                DisplayName = manifest.DisplayName.Trim(),
                Description = manifest.Description.Trim(),
                Category = manifest.Category.Trim(),
                RootDirectory = targetRoot,
                Executable = manifest.Executable.Trim(),
                Arguments = manifest.Arguments,
                Port = manifest.Port,
                RecommendedVramMiB = manifest.RecommendedVramMiB,
                RecommendedSystemMemoryMiB = manifest.RecommendedSystemMemoryMiB,
                RequiredFiles = manifest.RequiredFiles ?? [],
                Dependencies = manifest.Dependencies ?? [],
                GitHubRepository = manifest.GitHubRepository.Trim(),
                GitHubBranch = string.IsNullOrWhiteSpace(manifest.GitHubBranch) ? "main" : manifest.GitHubBranch.Trim(),
                InstalledVersion = manifest.Version.Trim(),
                Publisher = manifest.Publisher.Trim(),
                SigningKeyId = manifest.Signature.KeyId.Trim(),
                PackageSha256 = packageHash,
                IsManifestTrusted = true,
                TrustSource = "SignedManifest",
                IsHighVram = manifest.IsHighVram
            }, backupPath);
        }
        catch
        {
            if (!Directory.Exists(targetRoot) && backupPath is not null && Directory.Exists(backupPath))
            {
                Directory.Move(backupPath, targetRoot);
            }
            throw;
        }
        finally
        {
            if (Directory.Exists(stagingRoot))
            {
                Directory.Delete(stagingRoot, true);
            }
            if (downloadedPackage && File.Exists(packagePath))
            {
                File.Delete(packagePath);
            }
        }
    }

    public PluginUninstallResult Uninstall(LauncherModelDefinition definition, string dataRoot)
    {
        var root = Path.GetFullPath(definition.RootDirectory);
        var managedRoot = Path.GetFullPath(Path.Combine(dataRoot, "plugins")) + Path.DirectorySeparatorChar;
        if (!root.StartsWith(managedRoot, StringComparison.OrdinalIgnoreCase) || !Directory.Exists(root))
        {
            return new PluginUninstallResult(false, null);
        }
        var backupRoot = Path.Combine(dataRoot, "backups", "uninstalled-plugins");
        Directory.CreateDirectory(backupRoot);
        var backupPath = Path.Combine(backupRoot, $"{SanitizeId(definition.Id)}-{DateTime.Now:yyyyMMdd-HHmmss}");
        Directory.Move(root, backupPath);
        return new PluginUninstallResult(true, backupPath);
    }

    private static void ValidateManifest(PluginPackageManifest manifest)
    {
        if (manifest.SchemaVersion is not (2 or 3))
        {
            throw new InvalidDataException($"Unsupported plugin manifest schema: {manifest.SchemaVersion}.");
        }
        if (string.IsNullOrWhiteSpace(manifest.Id) || string.IsNullOrWhiteSpace(manifest.DisplayName) || string.IsNullOrWhiteSpace(manifest.Version))
        {
            throw new InvalidDataException("Plugin id, displayName, and version are required.");
        }
        if (!manifest.Id.Equals(SanitizeId(manifest.Id), StringComparison.Ordinal))
        {
            throw new InvalidDataException("Plugin id must already be lowercase and may contain only letters, numbers, '-' or '_'.");
        }
        if (string.IsNullOrWhiteSpace(manifest.Executable) || Path.IsPathRooted(manifest.Executable) || manifest.Executable.Contains("..", StringComparison.Ordinal))
        {
            throw new InvalidDataException("Plugin executable must be a safe path relative to the plugin directory.");
        }
        if (manifest.Port is < 1024 or > 65535)
        {
            throw new InvalidDataException("Plugin port must be between 1024 and 65535.");
        }
        if (manifest.PackageSha256.Length != 64 || manifest.PackageSha256.Any(character => !Uri.IsHexDigit(character)))
        {
            throw new InvalidDataException("packageSha256 must contain 64 hexadecimal characters.");
        }
        if (manifest.SchemaVersion >= 3 &&
            (string.IsNullOrWhiteSpace(manifest.LicenseName) ||
             !Uri.TryCreate(manifest.LicenseUrl, UriKind.Absolute, out var licenseUri) ||
             licenseUri.Scheme != Uri.UriSchemeHttps ||
             !manifest.RequiresLicenseAcceptance))
        {
            throw new InvalidDataException("Schema v3 plugins must provide a license name, an HTTPS license URL, and require explicit acceptance.");
        }
    }

    private static void ExtractSecurely(string packagePath, string stagingRoot)
    {
        var normalizedRoot = Path.GetFullPath(stagingRoot) + Path.DirectorySeparatorChar;
        using var archive = ZipFile.OpenRead(packagePath);
        foreach (var entry in archive.Entries)
        {
            var destination = Path.GetFullPath(Path.Combine(stagingRoot, entry.FullName.Replace('/', Path.DirectorySeparatorChar)));
            if (!destination.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException($"Unsafe package path: {entry.FullName}");
            }
            if (string.IsNullOrEmpty(entry.Name))
            {
                Directory.CreateDirectory(destination);
                continue;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            entry.ExtractToFile(destination, true);
        }
    }

    private static string ResolveContentRoot(string stagingRoot)
    {
        var files = Directory.EnumerateFiles(stagingRoot).ToArray();
        var directories = Directory.EnumerateDirectories(stagingRoot).ToArray();
        return files.Length == 0 && directories.Length == 1 ? directories[0] : stagingRoot;
    }

    private static void ValidateInstalledFiles(PluginPackageManifest manifest, string contentRoot)
    {
        var required = (manifest.RequiredFiles ?? []).Append(manifest.Executable);
        foreach (var relative in required)
        {
            var destination = Path.GetFullPath(Path.Combine(contentRoot, relative.Replace('/', Path.DirectorySeparatorChar)));
            var normalizedRoot = Path.GetFullPath(contentRoot) + Path.DirectorySeparatorChar;
            if (!destination.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase) || (!File.Exists(destination) && !Directory.Exists(destination)))
            {
                throw new InvalidDataException($"Required package file is missing or unsafe: {relative}");
            }
        }
    }

    private static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash);
    }

    private static string SanitizeId(string id)
    {
        var value = new string(id.Trim().ToLowerInvariant().Select(character => char.IsLetterOrDigit(character) || character is '-' or '_' ? character : '-').ToArray()).Trim('-');
        return string.IsNullOrWhiteSpace(value) ? throw new InvalidDataException("Plugin id is invalid.") : value;
    }
}

internal static class InstalledPluginTrustValidator
{
    public static ManifestVerificationResult Verify(LauncherModelDefinition definition, TrustedPublisherStore store)
    {
        if (!definition.TrustSource.Equals("SignedManifest", StringComparison.OrdinalIgnoreCase))
        {
            return new ManifestVerificationResult(true, definition.IsBuiltIn ? "Built-in plugin." : "Locally configured by the user.");
        }
        try
        {
            var manifestPath = Path.Combine(definition.RootDirectory, ".bachen-plugin-manifest.json");
            if (!File.Exists(manifestPath))
            {
                return new ManifestVerificationResult(false, "The installed signed manifest is missing.");
            }
            var manifest = JsonSerializer.Deserialize<PluginPackageManifest>(File.ReadAllText(manifestPath), new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (manifest is null)
            {
                return new ManifestVerificationResult(false, "The installed manifest could not be parsed.");
            }
            var signature = PluginManifestSignatureVerifier.Verify(manifest, store);
            if (!signature.IsTrusted)
            {
                return signature;
            }
            var matchesCatalog = definition.Id.Equals(manifest.Id, StringComparison.OrdinalIgnoreCase) &&
                definition.Executable.Equals(manifest.Executable, StringComparison.Ordinal) &&
                definition.Arguments.Equals(manifest.Arguments, StringComparison.Ordinal) &&
                definition.Port == manifest.Port &&
                definition.PackageSha256.Equals(manifest.PackageSha256, StringComparison.OrdinalIgnoreCase);
            return matchesCatalog
                ? signature
                : new ManifestVerificationResult(false, "The executable, arguments, port, or package hash differs from the signed manifest.", signature.Publisher);
        }
        catch (Exception ex)
        {
            return new ManifestVerificationResult(false, $"Installed manifest validation failed: {ex.Message}");
        }
    }
}
