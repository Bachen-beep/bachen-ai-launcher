using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace BaChenAiLauncher;

internal static class PluginManifestSignatureVerifier
{
    private static readonly JsonSerializerOptions CanonicalJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public static ManifestVerificationResult Verify(PluginPackageManifest manifest, TrustedPublisherStore store)
    {
        if (manifest.Signature is null || string.IsNullOrWhiteSpace(manifest.Signature.KeyId) || string.IsNullOrWhiteSpace(manifest.Signature.Value))
        {
            return new ManifestVerificationResult(false, "The manifest is unsigned.");
        }
        if (!manifest.Signature.Algorithm.Equals("RSA-SHA256", StringComparison.OrdinalIgnoreCase))
        {
            return new ManifestVerificationResult(false, $"Unsupported signature algorithm: {manifest.Signature.Algorithm}");
        }
        var publisher = store.Publishers.FirstOrDefault(item => item.KeyId.Equals(manifest.Signature.KeyId, StringComparison.OrdinalIgnoreCase));
        if (publisher is null)
        {
            return new ManifestVerificationResult(false, $"Publisher key '{manifest.Signature.KeyId}' is not trusted.");
        }
        try
        {
            var signature = Convert.FromBase64String(manifest.Signature.Value);
            var payload = Encoding.UTF8.GetBytes(CreateCanonicalPayload(manifest));
            using var rsa = RSA.Create();
            rsa.ImportFromPem(publisher.PublicKeyPem);
            var valid = rsa.VerifyData(payload, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            return valid
                ? new ManifestVerificationResult(true, $"Valid signature from {publisher.DisplayName} ({publisher.KeyId}).", publisher)
                : new ManifestVerificationResult(false, "The manifest signature is invalid.", publisher);
        }
        catch (Exception ex)
        {
            return new ManifestVerificationResult(false, $"Signature verification failed: {ex.Message}", publisher);
        }
    }

    public static string CreateCanonicalPayload(PluginPackageManifest manifest)
    {
        if (manifest.SchemaVersion >= 6)
        {
            var versionSixPayload = new
            {
                manifest.SchemaVersion,
                manifest.Id,
                manifest.DisplayName,
                manifest.Version,
                manifest.Publisher,
                manifest.Category,
                manifest.Description,
                manifest.Executable,
                manifest.Arguments,
                manifest.Runtime,
                manifest.RuntimeVersion,
                manifest.Port,
                manifest.RecommendedVramMiB,
                manifest.RecommendedSystemMemoryMiB,
                manifest.IsHighVram,
                RequiredFiles = manifest.RequiredFiles ?? [],
                Dependencies = manifest.Dependencies ?? [],
                manifest.GitHubRepository,
                manifest.GitHubBranch,
                manifest.PackageUrl,
                PackageMirrors = manifest.PackageMirrors ?? [],
                PackageSha256 = manifest.PackageSha256.ToUpperInvariant(),
                manifest.PackageSizeBytes,
                PreservedPaths = manifest.PreservedPaths ?? [],
                manifest.LicenseName,
                manifest.LicenseUrl,
                manifest.RequiresLicenseAcceptance,
                manifest.CreateVirtualEnvironment,
                manifest.VirtualEnvironmentPath,
                manifest.RequirementsFile,
                manifest.MinimumFreeDiskBytes,
                manifest.RequiresExternalAuthorization,
                manifest.ModelProvider,
                manifest.ModelId,
                manifest.AuthorizationUrl,
                manifest.AuthorizationProbePath,
                manifest.ManagedRuntimeId,
                PythonInstallArguments = manifest.PythonInstallArguments ?? [],
                AssetPackages = (manifest.AssetPackages ?? []).Select(asset => new
                {
                    asset.Id,
                    asset.Url,
                    Mirrors = asset.Mirrors ?? [],
                    Sha256 = asset.Sha256.ToUpperInvariant(),
                    asset.SizeBytes,
                    asset.DestinationPath
                }).ToArray()
            };
            return JsonSerializer.Serialize(versionSixPayload, CanonicalJsonOptions);
        }
        if (manifest.SchemaVersion >= 5)
        {
            var versionFivePayload = new
            {
                manifest.SchemaVersion,
                manifest.Id,
                manifest.DisplayName,
                manifest.Version,
                manifest.Publisher,
                manifest.Category,
                manifest.Description,
                manifest.Executable,
                manifest.Arguments,
                manifest.Runtime,
                manifest.RuntimeVersion,
                manifest.Port,
                manifest.RecommendedVramMiB,
                manifest.RecommendedSystemMemoryMiB,
                manifest.IsHighVram,
                RequiredFiles = manifest.RequiredFiles ?? [],
                Dependencies = manifest.Dependencies ?? [],
                manifest.GitHubRepository,
                manifest.GitHubBranch,
                manifest.PackageUrl,
                PackageMirrors = manifest.PackageMirrors ?? [],
                PackageSha256 = manifest.PackageSha256.ToUpperInvariant(),
                manifest.PackageSizeBytes,
                PreservedPaths = manifest.PreservedPaths ?? [],
                manifest.LicenseName,
                manifest.LicenseUrl,
                manifest.RequiresLicenseAcceptance,
                manifest.CreateVirtualEnvironment,
                manifest.VirtualEnvironmentPath,
                manifest.RequirementsFile,
                manifest.MinimumFreeDiskBytes,
                manifest.RequiresExternalAuthorization,
                manifest.ModelProvider,
                manifest.ModelId,
                manifest.AuthorizationUrl,
                manifest.AuthorizationProbePath
            };
            return JsonSerializer.Serialize(versionFivePayload, CanonicalJsonOptions);
        }
        if (manifest.SchemaVersion >= 4)
        {
            var versionFourPayload = new
            {
                manifest.SchemaVersion,
                manifest.Id,
                manifest.DisplayName,
                manifest.Version,
                manifest.Publisher,
                manifest.Category,
                manifest.Description,
                manifest.Executable,
                manifest.Arguments,
                manifest.Runtime,
                manifest.RuntimeVersion,
                manifest.Port,
                manifest.RecommendedVramMiB,
                manifest.RecommendedSystemMemoryMiB,
                manifest.IsHighVram,
                RequiredFiles = manifest.RequiredFiles ?? [],
                Dependencies = manifest.Dependencies ?? [],
                manifest.GitHubRepository,
                manifest.GitHubBranch,
                manifest.PackageUrl,
                PackageSha256 = manifest.PackageSha256.ToUpperInvariant(),
                manifest.PackageSizeBytes,
                PreservedPaths = manifest.PreservedPaths ?? [],
                manifest.LicenseName,
                manifest.LicenseUrl,
                manifest.RequiresLicenseAcceptance
            };
            return JsonSerializer.Serialize(versionFourPayload, CanonicalJsonOptions);
        }
        if (manifest.SchemaVersion >= 3)
        {
            var versionThreePayload = new
            {
                manifest.SchemaVersion,
                manifest.Id,
                manifest.DisplayName,
                manifest.Version,
                manifest.Publisher,
                manifest.Category,
                manifest.Description,
                manifest.Executable,
                manifest.Arguments,
                manifest.Port,
                manifest.RecommendedVramMiB,
                manifest.RecommendedSystemMemoryMiB,
                manifest.IsHighVram,
                RequiredFiles = manifest.RequiredFiles ?? [],
                Dependencies = manifest.Dependencies ?? [],
                manifest.GitHubRepository,
                manifest.GitHubBranch,
                manifest.PackageUrl,
                PackageSha256 = manifest.PackageSha256.ToUpperInvariant(),
                manifest.LicenseName,
                manifest.LicenseUrl,
                manifest.RequiresLicenseAcceptance
            };
            return JsonSerializer.Serialize(versionThreePayload, CanonicalJsonOptions);
        }
        var payload = new
        {
            manifest.SchemaVersion,
            manifest.Id,
            manifest.DisplayName,
            manifest.Version,
            manifest.Publisher,
            manifest.Category,
            manifest.Description,
            manifest.Executable,
            manifest.Arguments,
            manifest.Port,
            manifest.RecommendedVramMiB,
            manifest.RecommendedSystemMemoryMiB,
            manifest.IsHighVram,
            RequiredFiles = manifest.RequiredFiles ?? [],
            Dependencies = manifest.Dependencies ?? [],
            manifest.GitHubRepository,
            manifest.GitHubBranch,
            manifest.PackageUrl,
            PackageSha256 = manifest.PackageSha256.ToUpperInvariant()
        };
        return JsonSerializer.Serialize(payload, CanonicalJsonOptions);
    }
}

internal static class TrustedPublisherStoreService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static TrustedPublisherStore Load(string path)
    {
        try
        {
            return File.Exists(path)
                ? JsonSerializer.Deserialize<TrustedPublisherStore>(File.ReadAllText(path)) ?? new TrustedPublisherStore()
                : new TrustedPublisherStore();
        }
        catch
        {
            return new TrustedPublisherStore();
        }
    }

    public static void Save(string path, TrustedPublisherStore store)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporaryPath = path + ".tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(store, JsonOptions), Encoding.UTF8);
        File.Move(temporaryPath, path, true);
    }
}
