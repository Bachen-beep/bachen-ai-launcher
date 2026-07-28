using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace BaChenAiLauncher;

internal sealed class PluginCatalogIndex
{
    public int SchemaVersion { get; set; } = 1;
    public DateTimeOffset GeneratedAt { get; set; }
    public List<PluginPackageManifest> Plugins { get; set; } = [];
    public PluginManifestSignature Signature { get; set; } = new() { KeyId = "bachen-plugin-index-2026" };
}

internal static class PluginCatalogIndexVerifier
{
    public const string SigningKeyCredentialTarget = "BaChenAILauncher/PluginIndexSigningKey";
    public const string InstalledIndexFileName = ".bachen-plugin-index.json";
    private const string PublicKeyResourceName = "BaChenAiLauncher.PluginIndexPublicKey";
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public static void Validate(PluginCatalogIndex index)
    {
        if (index.SchemaVersion != 1 || index.Plugins.Count == 0)
        {
            throw new InvalidDataException("The plugin index schema or plugin list is invalid.");
        }
        if (!index.Signature.KeyId.Equals("bachen-plugin-index-2026", StringComparison.Ordinal) ||
            !index.Signature.Algorithm.Equals("RSA-SHA256", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(index.Signature.Value))
        {
            throw new CryptographicException("The plugin index signature metadata is invalid.");
        }
        using var stream = typeof(PluginCatalogIndexVerifier).Assembly.GetManifestResourceStream(PublicKeyResourceName)
            ?? throw new InvalidOperationException("The plugin index public key is missing.");
        using var reader = new StreamReader(stream, Encoding.ASCII);
        using var rsa = RSA.Create();
        rsa.ImportFromPem(reader.ReadToEnd());
        var valid = rsa.VerifyData(
            Encoding.UTF8.GetBytes(CreateCanonicalPayload(index)),
            Convert.FromBase64String(index.Signature.Value),
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);
        if (!valid)
        {
            throw new CryptographicException("The plugin index signature is invalid.");
        }
    }

    public static string CreateCanonicalPayload(PluginCatalogIndex index)
        => JsonSerializer.Serialize(new { index.SchemaVersion, index.GeneratedAt, Plugins = index.Plugins }, JsonOptions);

    public static void WriteInstalledCopy(PluginCatalogIndex index, string pluginRoot)
    {
        Validate(index);
        var path = Path.Combine(pluginRoot, InstalledIndexFileName);
        File.WriteAllText(path, JsonSerializer.Serialize(index, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        }), new UTF8Encoding(false));
    }

    public static async Task SignFileAsync(string indexPath, string privateKeyPath)
    {
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };
        var index = JsonSerializer.Deserialize<PluginCatalogIndex>(await File.ReadAllTextAsync(indexPath), options)
            ?? throw new InvalidDataException("The plugin index is empty.");
        using var rsa = RSA.Create();
        rsa.ImportFromPem(await File.ReadAllTextAsync(privateKeyPath));
        index.Signature.Value = Convert.ToBase64String(rsa.SignData(Encoding.UTF8.GetBytes(CreateCanonicalPayload(index)), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
        await File.WriteAllTextAsync(indexPath, JsonSerializer.Serialize(index, options), new UTF8Encoding(false));
    }

    public static async Task GenerateKeyPairAsync(string privateKeyPath, string publicKeyPath)
    {
        using var rsa = RSA.Create(2048);
        await File.WriteAllTextAsync(privateKeyPath, rsa.ExportRSAPrivateKeyPem(), new UTF8Encoding(false));
        await File.WriteAllTextAsync(publicKeyPath, rsa.ExportSubjectPublicKeyInfoPem(), new UTF8Encoding(false));
    }

    public static async Task StoreSigningKeyAsync(string privateKeyPath)
    {
        WindowsCredentialStore.Save(SigningKeyCredentialTarget, "BaChen Plugin Index", await File.ReadAllTextAsync(privateKeyPath));
        File.Delete(privateKeyPath);
    }

    public static async Task SignFileWithStoredKeyAsync(string indexPath)
    {
        var pem = WindowsCredentialStore.Read(SigningKeyCredentialTarget) ?? throw new InvalidOperationException("The plugin index signing key is not available in Windows Credential Manager.");
        var temporaryKey = Path.Combine(Path.GetTempPath(), $"bachen-plugin-index-{Guid.NewGuid():N}.pem");
        try
        {
            await File.WriteAllTextAsync(temporaryKey, pem, new UTF8Encoding(false));
            await SignFileAsync(indexPath, temporaryKey);
        }
        finally
        {
            if (File.Exists(temporaryKey))
            {
                File.Delete(temporaryKey);
            }
        }
    }
}

internal sealed class PluginCatalogService(HttpClient httpClient)
{
    public static readonly Uri DefaultIndexUri = new("https://github.com/Bachen-beep/bachen-ai-launcher/releases/latest/download/plugin-index.json");

    public async Task<PluginCatalogIndex> LoadAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var json = await httpClient.GetStringAsync(DefaultIndexUri, cancellationToken);
            return ParseAndValidate(json);
        }
        catch
        {
            using var stream = typeof(PluginCatalogService).Assembly.GetManifestResourceStream("BaChenAiLauncher.BundledPluginIndex")
                ?? throw new InvalidOperationException("The bundled plugin index is missing.");
            using var reader = new StreamReader(stream, Encoding.UTF8);
            return ParseAndValidate(await reader.ReadToEndAsync(cancellationToken));
        }
    }

    private static PluginCatalogIndex ParseAndValidate(string json)
    {
        var index = JsonSerializer.Deserialize<PluginCatalogIndex>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidDataException("The plugin index is empty.");
        PluginCatalogIndexVerifier.Validate(index);
        return index;
    }
}
