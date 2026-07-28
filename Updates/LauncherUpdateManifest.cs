using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace BaChenAiLauncher;

internal sealed class LauncherUpdateManifest
{
    public string Version { get; set; } = string.Empty;
    public string MinimumCompatibleVersion { get; set; } = "0.11.0";
    public string DownloadUrl { get; set; } = string.Empty;
    public string Sha256 { get; set; } = string.Empty;
    public string ReleaseNotesUrl { get; set; } = string.Empty;
    public DateTimeOffset PublishedAt { get; set; }
    public LauncherUpdateSignature Signature { get; set; } = new();
}

internal sealed class LauncherUpdateSignature
{
    public string KeyId { get; set; } = "bachen-launcher-release-2026";
    public string Algorithm { get; set; } = "RSA-SHA256";
    public string Value { get; set; } = string.Empty;
}

internal static class LauncherUpdateManifestVerifier
{
    public const string KeyId = "bachen-launcher-release-2026";

    public static string CreateCanonicalPayload(LauncherUpdateManifest manifest)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = false }))
        {
            writer.WriteStartObject();
            writer.WriteString("version", manifest.Version);
            writer.WriteString("minimumCompatibleVersion", manifest.MinimumCompatibleVersion);
            writer.WriteString("downloadUrl", manifest.DownloadUrl);
            writer.WriteString("sha256", manifest.Sha256.ToUpperInvariant());
            writer.WriteString("releaseNotesUrl", manifest.ReleaseNotesUrl);
            writer.WriteString("publishedAt", manifest.PublishedAt.ToUniversalTime());
            writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    public static void Validate(LauncherUpdateManifest manifest, string publicKeyPem)
    {
        if (manifest.Signature.KeyId != KeyId ||
            !manifest.Signature.Algorithm.Equals("RSA-SHA256", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("The launcher update uses an untrusted signing key or algorithm.");
        }
        if (!Version.TryParse(manifest.Version, out _) || !Version.TryParse(manifest.MinimumCompatibleVersion, out _))
        {
            throw new InvalidDataException("The launcher update contains an invalid version.");
        }
        if (!Uri.TryCreate(manifest.DownloadUrl, UriKind.Absolute, out var downloadUri) || downloadUri.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidDataException("The launcher update download URL must use HTTPS.");
        }
        if (manifest.Sha256.Length != 64 || !manifest.Sha256.All(Uri.IsHexDigit))
        {
            throw new InvalidDataException("The launcher update SHA-256 value is invalid.");
        }
        byte[] signature;
        try
        {
            signature = Convert.FromBase64String(manifest.Signature.Value);
        }
        catch (FormatException ex)
        {
            throw new InvalidDataException("The launcher update signature is invalid.", ex);
        }
        using var rsa = RSA.Create();
        rsa.ImportFromPem(publicKeyPem);
        var payload = Encoding.UTF8.GetBytes(CreateCanonicalPayload(manifest));
        if (!rsa.VerifyData(payload, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1))
        {
            throw new InvalidDataException("The launcher update signature verification failed.");
        }
    }

    public static async Task SignFileAsync(string manifestPath, string privateKeyPath)
    {
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true, WriteIndented = true };
        var manifest = JsonSerializer.Deserialize<LauncherUpdateManifest>(await File.ReadAllTextAsync(manifestPath), options)
            ?? throw new InvalidDataException("The launcher update manifest is empty.");
        manifest.Signature.KeyId = KeyId;
        manifest.Signature.Algorithm = "RSA-SHA256";
        using var rsa = RSA.Create();
        rsa.ImportFromPem(await File.ReadAllTextAsync(privateKeyPath));
        var payload = Encoding.UTF8.GetBytes(CreateCanonicalPayload(manifest));
        manifest.Signature.Value = Convert.ToBase64String(rsa.SignData(payload, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
        await File.WriteAllTextAsync(manifestPath, JsonSerializer.Serialize(manifest, options), new UTF8Encoding(false));
    }
}
