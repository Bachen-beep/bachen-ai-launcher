using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace BaChenAiLauncher;

internal sealed class PluginPackageService(HttpClient httpClient)
{
    public async Task<PluginInstallResult> InstallAsync(PluginPackageManifest manifest, string? localPackagePath, string dataRoot, IProgress<string>? setupProgress = null, IProgress<PluginDownloadProgress>? downloadProgress = null, CancellationToken cancellationToken = default)
    {
        ValidateManifest(manifest);
        var preflight = PluginInstallPreflightService.Assess(manifest, dataRoot);
        if (!preflight.CanInstall)
        {
            throw new IOException(string.Join(Environment.NewLine, preflight.Issues.Where(issue => issue.Severity == PluginPreflightSeverity.Blocking).Select(issue => issue.Message)));
        }
        var pluginsRoot = Path.GetFullPath(Path.Combine(dataRoot, "plugins"));
        var downloadsRoot = Path.GetFullPath(Path.Combine(dataRoot, "downloads"));
        var backupsRoot = Path.GetFullPath(Path.Combine(dataRoot, "backups", "plugin-installs"));
        Directory.CreateDirectory(pluginsRoot);
        Directory.CreateDirectory(downloadsRoot);
        Directory.CreateDirectory(backupsRoot);

        var safeId = SanitizeId(manifest.Id);
        var packagePath = localPackagePath;
        if (string.IsNullOrWhiteSpace(packagePath))
        {
            packagePath = await new PluginDownloadService(httpClient).DownloadAsync(manifest, dataRoot, downloadProgress, cancellationToken);
        }

        packagePath = Path.GetFullPath(packagePath);
        if (!File.Exists(packagePath) || !Path.GetExtension(packagePath).Equals(".zip", StringComparison.OrdinalIgnoreCase))
        {
            throw new FileNotFoundException("Plugin package ZIP was not found.", packagePath);
        }
        if (manifest.PackageSizeBytes > 0 && new FileInfo(packagePath).Length != manifest.PackageSizeBytes)
        {
            throw new InvalidDataException($"Package size mismatch. Expected {manifest.PackageSizeBytes} bytes; actual {new FileInfo(packagePath).Length} bytes.");
        }
        var packageHash = await ComputeSha256Async(packagePath, cancellationToken);
        if (!packageHash.Equals(manifest.PackageSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"Package SHA-256 mismatch. Expected {manifest.PackageSha256}; actual {packageHash}.");
        }

        var stagingRoot = Path.Combine(downloadsRoot, $"install-{safeId}-{Guid.NewGuid():N}");
        var targetRoot = Path.Combine(pluginsRoot, safeId);
        string? backupPath = null;
        var targetActivated = false;
        try
        {
            Directory.CreateDirectory(stagingRoot);
            ExtractSecurely(packagePath, stagingRoot);
            var contentRoot = ResolveContentRoot(stagingRoot);
            foreach (var asset in manifest.AssetPackages ?? [])
            {
                setupProgress?.Report($"Downloading model asset {asset.Id}");
                var assetPath = await new PluginDownloadService(httpClient).DownloadAssetAsync(manifest.Id, asset, dataRoot, downloadProgress, cancellationToken);
                var assetRoot = ResolveSafeContentPath(contentRoot, asset.DestinationPath, "asset destination");
                Directory.CreateDirectory(assetRoot);
                ExtractSecurely(assetPath, assetRoot);
            }
            await PythonEnvironmentService.EnsureAsync(manifest, contentRoot, dataRoot, httpClient, setupProgress, downloadProgress, cancellationToken);
            ValidateInstalledFiles(manifest, contentRoot);

            if (Directory.Exists(targetRoot))
            {
                backupPath = Path.Combine(backupsRoot, $"{safeId}-{DateTime.Now:yyyyMMdd-HHmmss}");
                Directory.Move(targetRoot, backupPath);
            }
            Directory.Move(contentRoot, targetRoot);
            targetActivated = true;
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
                Runtime = manifest.Runtime.Trim(),
                RuntimeVersion = manifest.RuntimeVersion.Trim(),
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
                PackageSizeBytes = new FileInfo(packagePath).Length,
                PreservedPaths = manifest.PreservedPaths ?? [],
                IsManifestTrusted = true,
                TrustSource = "SignedManifest",
                IsHighVram = manifest.IsHighVram
            }, backupPath);
        }
        catch
        {
            if (targetActivated && Directory.Exists(targetRoot))
            {
                Directory.Delete(targetRoot, true);
            }
            if (backupPath is not null && Directory.Exists(backupPath))
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
        if (manifest.SchemaVersion is not (2 or 3 or 4 or 5 or 6))
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
        if (manifest.SchemaVersion >= 4)
        {
            if (!System.Text.RegularExpressions.Regex.IsMatch(manifest.Version, "^\\d+\\.\\d+\\.\\d+(?:-[0-9A-Za-z.-]+)?(?:\\+[0-9A-Za-z.-]+)?$"))
            {
                throw new InvalidDataException("Schema v4 plugin version must use SemVer (for example 1.2.3 or 1.2.3-preview.1).");
            }
            if (string.IsNullOrWhiteSpace(manifest.Runtime) || string.IsNullOrWhiteSpace(manifest.RuntimeVersion))
            {
                throw new InvalidDataException("Schema v4 plugins must provide runtime and runtimeVersion.");
            }
            if (manifest.PackageSizeBytes <= 0)
            {
                throw new InvalidDataException("Schema v4 plugins must provide a positive packageSizeBytes value.");
            }
            if (!Uri.TryCreate(manifest.PackageUrl, UriKind.Absolute, out var packageUri) || packageUri.Scheme != Uri.UriSchemeHttps)
            {
                throw new InvalidDataException("Schema v4 plugins must provide an HTTPS packageUrl.");
            }
            if (!System.Text.RegularExpressions.Regex.IsMatch(manifest.GitHubRepository, "^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$"))
            {
                throw new InvalidDataException("Schema v4 gitHubRepository must use owner/repository format.");
            }
            foreach (var path in manifest.PreservedPaths ?? [])
            {
                if (string.IsNullOrWhiteSpace(path) || Path.IsPathRooted(path) || path.Contains("..", StringComparison.Ordinal))
                {
                    throw new InvalidDataException("preservedPaths must contain safe paths relative to the plugin directory.");
                }
            }
        }
        if (manifest.SchemaVersion >= 5)
        {
            foreach (var mirror in manifest.PackageMirrors ?? [])
            {
                if (!Uri.TryCreate(mirror, UriKind.Absolute, out var mirrorUri) || mirrorUri.Scheme != Uri.UriSchemeHttps)
                {
                    throw new InvalidDataException("packageMirrors must contain only HTTPS URLs.");
                }
            }
            if (manifest.CreateVirtualEnvironment &&
                (!manifest.Runtime.Equals("python", StringComparison.OrdinalIgnoreCase) ||
                 !IsSafeRelativePath(manifest.VirtualEnvironmentPath) ||
                 (!string.IsNullOrWhiteSpace(manifest.RequirementsFile) && !IsSafeRelativePath(manifest.RequirementsFile))))
            {
                throw new InvalidDataException("Python virtual environment fields are invalid or unsafe.");
            }
            if (manifest.MinimumFreeDiskBytes < 0)
            {
                throw new InvalidDataException("minimumFreeDiskBytes cannot be negative.");
            }
            if (manifest.RequiresExternalAuthorization)
            {
                if (!manifest.ModelProvider.Equals("huggingface", StringComparison.OrdinalIgnoreCase) ||
                    !System.Text.RegularExpressions.Regex.IsMatch(manifest.ModelId, "^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$") ||
                    !Uri.TryCreate(manifest.AuthorizationUrl, UriKind.Absolute, out var authorizationUri) ||
                    authorizationUri.Scheme != Uri.UriSchemeHttps ||
                    !authorizationUri.Host.Equals("huggingface.co", StringComparison.OrdinalIgnoreCase) ||
                    !IsSafeRelativePath(manifest.AuthorizationProbePath))
                {
                    throw new InvalidDataException("External model authorization metadata is incomplete or unsafe.");
                }
            }
        }
        if (manifest.SchemaVersion >= 6)
        {
            if (manifest.CreateVirtualEnvironment && string.IsNullOrWhiteSpace(manifest.ManagedRuntimeId))
            {
                throw new InvalidDataException("Schema v6 Python plugins must declare managedRuntimeId.");
            }
            if (manifest.CreateVirtualEnvironment &&
                !ManagedPythonRuntimeService.Supported.Any(runtime => runtime.Id.Equals(manifest.ManagedRuntimeId, StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidDataException($"Unsupported managedRuntimeId: {manifest.ManagedRuntimeId}");
            }
            foreach (var asset in manifest.AssetPackages ?? [])
            {
                if (string.IsNullOrWhiteSpace(asset.Id) ||
                    !Uri.TryCreate(asset.Url, UriKind.Absolute, out var assetUri) || assetUri.Scheme != Uri.UriSchemeHttps ||
                    asset.Sha256.Length != 64 || asset.Sha256.Any(character => !Uri.IsHexDigit(character)) ||
                    asset.SizeBytes <= 0 || !IsSafeRelativePath(asset.DestinationPath) ||
                    (asset.Mirrors ?? []).Any(mirror => !Uri.TryCreate(mirror, UriKind.Absolute, out var mirrorUri) || mirrorUri.Scheme != Uri.UriSchemeHttps))
                {
                    throw new InvalidDataException($"Asset package '{asset.Id}' is incomplete or unsafe.");
                }
            }
        }
    }

    private static bool IsSafeRelativePath(string value)
        => !string.IsNullOrWhiteSpace(value) && !Path.IsPathRooted(value) && !value.Contains("..", StringComparison.Ordinal);

    private static string ResolveSafeContentPath(string root, string relative, string field)
    {
        if (!IsSafeRelativePath(relative))
        {
            throw new InvalidDataException($"The {field} must be a safe relative path.");
        }
        var normalizedRoot = Path.GetFullPath(root) + Path.DirectorySeparatorChar;
        var path = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
        return path.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase) ? path : throw new InvalidDataException($"The {field} escapes the plugin root.");
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
        if (definition.TrustSource.Equals("SignedCatalog", StringComparison.OrdinalIgnoreCase))
        {
            return VerifySignedCatalogEntry(definition);
        }
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
            var matchesCatalog = MatchesDefinition(definition, manifest);
            return matchesCatalog
                ? signature
                : new ManifestVerificationResult(false, "The executable, arguments, port, or package hash differs from the signed manifest.", signature.Publisher);
        }
        catch (Exception ex)
        {
            return new ManifestVerificationResult(false, $"Installed manifest validation failed: {ex.Message}");
        }
    }

    private static ManifestVerificationResult VerifySignedCatalogEntry(LauncherModelDefinition definition)
    {
        try
        {
            var manifestPath = Path.Combine(definition.RootDirectory, ".bachen-plugin-manifest.json");
            var catalogPath = Path.Combine(definition.RootDirectory, PluginCatalogIndexVerifier.InstalledIndexFileName);
            if (!File.Exists(manifestPath) || !File.Exists(catalogPath))
            {
                return new ManifestVerificationResult(false, "The installed catalog trust evidence is missing.");
            }
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var manifest = JsonSerializer.Deserialize<PluginPackageManifest>(File.ReadAllText(manifestPath), options)
                ?? throw new InvalidDataException("The installed manifest could not be parsed.");
            var catalog = JsonSerializer.Deserialize<PluginCatalogIndex>(File.ReadAllText(catalogPath), options)
                ?? throw new InvalidDataException("The installed plugin index could not be parsed.");
            PluginCatalogIndexVerifier.Validate(catalog);
            var signedManifest = catalog.Plugins.SingleOrDefault(plugin => plugin.Id.Equals(definition.Id, StringComparison.OrdinalIgnoreCase));
            if (signedManifest is null ||
                !PluginManifestSignatureVerifier.CreateCanonicalPayload(manifest).Equals(
                    PluginManifestSignatureVerifier.CreateCanonicalPayload(signedManifest),
                    StringComparison.Ordinal) ||
                !MatchesDefinition(definition, signedManifest))
            {
                return new ManifestVerificationResult(false, "The installed command or manifest differs from the signed plugin index.");
            }
            return new ManifestVerificationResult(true, "Verified by the signed BaChen plugin index.");
        }
        catch (Exception ex)
        {
            return new ManifestVerificationResult(false, $"Installed plugin index validation failed: {ex.Message}");
        }
    }

    private static bool MatchesDefinition(LauncherModelDefinition definition, PluginPackageManifest manifest)
    {
        var matches = definition.Id.Equals(manifest.Id, StringComparison.OrdinalIgnoreCase) &&
            definition.Executable.Equals(manifest.Executable, StringComparison.Ordinal) &&
            definition.Arguments.Equals(manifest.Arguments, StringComparison.Ordinal) &&
            definition.Port == manifest.Port &&
            definition.PackageSha256.Equals(manifest.PackageSha256, StringComparison.OrdinalIgnoreCase) &&
            definition.RequiredFiles.SequenceEqual(manifest.RequiredFiles ?? [], StringComparer.OrdinalIgnoreCase) &&
            definition.Dependencies.SequenceEqual(manifest.Dependencies ?? [], StringComparer.OrdinalIgnoreCase);
        if (matches && manifest.SchemaVersion >= 4)
        {
            matches = definition.Runtime.Equals(manifest.Runtime, StringComparison.OrdinalIgnoreCase) &&
                definition.RuntimeVersion.Equals(manifest.RuntimeVersion, StringComparison.Ordinal) &&
                definition.PackageSizeBytes == manifest.PackageSizeBytes &&
                definition.PreservedPaths.SequenceEqual(manifest.PreservedPaths ?? [], StringComparer.OrdinalIgnoreCase);
        }
        return matches;
    }
}
