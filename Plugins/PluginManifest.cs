namespace BaChenAiLauncher;

internal sealed class PluginPackageManifest
{
    public int SchemaVersion { get; set; } = 3;
    public string Id { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Version { get; set; } = "0.0.0";
    public string Publisher { get; set; } = string.Empty;
    public string Category { get; set; } = "Other";
    public string Description { get; set; } = string.Empty;
    public string Executable { get; set; } = string.Empty;
    public string Arguments { get; set; } = string.Empty;
    public int Port { get; set; }
    public int RecommendedVramMiB { get; set; }
    public int RecommendedSystemMemoryMiB { get; set; } = 8192;
    public bool IsHighVram { get; set; }
    public string[] RequiredFiles { get; set; } = [];
    public string[] Dependencies { get; set; } = [];
    public string GitHubRepository { get; set; } = string.Empty;
    public string GitHubBranch { get; set; } = "main";
    public string PackageUrl { get; set; } = string.Empty;
    public string PackageSha256 { get; set; } = string.Empty;
    public string LicenseName { get; set; } = string.Empty;
    public string LicenseUrl { get; set; } = string.Empty;
    public bool RequiresLicenseAcceptance { get; set; } = true;
    public PluginManifestSignature Signature { get; set; } = new();
}

internal sealed class PluginManifestSignature
{
    public string KeyId { get; set; } = string.Empty;
    public string Algorithm { get; set; } = "RSA-SHA256";
    public string Value { get; set; } = string.Empty;
}

internal sealed class TrustedPublisherStore
{
    public int SchemaVersion { get; set; } = 1;
    public List<TrustedPublisher> Publishers { get; set; } = [];
}

internal sealed class TrustedPublisher
{
    public string KeyId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string PublicKeyPem { get; set; } = string.Empty;
    public DateTimeOffset AddedAt { get; set; } = DateTimeOffset.Now;
}

internal sealed record ManifestVerificationResult(bool IsTrusted, string Message, TrustedPublisher? Publisher = null);

internal sealed record PluginInstallResult(LauncherModelDefinition Definition, string? ReplacedPluginBackup);

internal sealed record PluginUninstallResult(bool FilesMoved, string? BackupPath);
