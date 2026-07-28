using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace BaChenAiLauncher;

internal static class LauncherSelfTests
{
    public static async Task<int> WriteCanonicalManifestPayloadAsync(string manifestPath, string outputPath)
    {
        try
        {
            var manifest = System.Text.Json.JsonSerializer.Deserialize<PluginPackageManifest>(
                await File.ReadAllTextAsync(manifestPath),
                new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? throw new InvalidDataException("The plugin manifest is empty.");
            await File.WriteAllTextAsync(outputPath, PluginManifestSignatureVerifier.CreateCanonicalPayload(manifest), new UTF8Encoding(false));
            return 0;
        }
        catch
        {
            return 1;
        }
    }

    public static async Task<int> RunAsync(string reportPath)
    {
        var lines = new List<string>();
        var testRoot = Path.Combine(Path.GetTempPath(), "bachen-launcher-self-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(testRoot);
            var packageSource = Path.Combine(testRoot, "package-source");
            Directory.CreateDirectory(packageSource);
            await File.WriteAllTextAsync(Path.Combine(packageSource, "start.cmd"), "@echo off\r\nexit /b 0\r\n", Encoding.ASCII);
            await File.WriteAllTextAsync(Path.Combine(packageSource, "model.dat"), "self-test", Encoding.ASCII);
            var packagePath = Path.Combine(testRoot, "self-test-plugin.zip");
            ZipFile.CreateFromDirectory(packageSource, packagePath);
            string packageHash;
            await using (var packageStream = File.OpenRead(packagePath))
            {
                packageHash = Convert.ToHexString(await SHA256.HashDataAsync(packageStream));
            }

            using var rsa = RSA.Create(2048);
            var manifest = new PluginPackageManifest
            {
                Id = "self-test-plugin",
                DisplayName = "Self Test Plugin",
                Version = "1.0.0",
                Publisher = "BaChen Self Test",
                Category = "Utilities",
                Description = "Installer verification fixture",
                Executable = "start.cmd",
                Arguments = "--port {port}",
                Port = 17862,
                RequiredFiles = ["model.dat"],
                Dependencies = ["file:model.dat"],
                PackageSha256 = packageHash,
                LicenseName = "Self Test License",
                LicenseUrl = "https://example.com/self-test-license",
                RequiresLicenseAcceptance = true,
                Signature = new PluginManifestSignature { KeyId = "self-test-key", Algorithm = "RSA-SHA256" }
            };
            var payload = Encoding.UTF8.GetBytes(PluginManifestSignatureVerifier.CreateCanonicalPayload(manifest));
            manifest.Signature.Value = Convert.ToBase64String(rsa.SignData(payload, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
            var publishers = new TrustedPublisherStore
            {
                Publishers = [new TrustedPublisher { KeyId = "self-test-key", DisplayName = "Self Test Publisher", PublicKeyPem = rsa.ExportSubjectPublicKeyInfoPem() }]
            };

            var launcherUpdate = new LauncherUpdateManifest
            {
                Version = "99.0.0",
                MinimumCompatibleVersion = "0.11.0",
                DownloadUrl = "https://github.com/Bachen-beep/bachen-ai-launcher/releases/download/v99.0.0/BaChen.AI.Launcher.exe",
                Sha256 = new string('A', 64),
                ReleaseNotesUrl = "https://github.com/Bachen-beep/bachen-ai-launcher/releases/tag/v99.0.0",
                PublishedAt = DateTimeOffset.Parse("2026-07-28T00:00:00Z")
            };
            var updatePayload = Encoding.UTF8.GetBytes(LauncherUpdateManifestVerifier.CreateCanonicalPayload(launcherUpdate));
            launcherUpdate.Signature.Value = Convert.ToBase64String(rsa.SignData(updatePayload, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
            launcherUpdate.Signature.KeyId = LauncherUpdateManifestVerifier.KeyId;
            LauncherUpdateManifestVerifier.Validate(launcherUpdate, rsa.ExportSubjectPublicKeyInfoPem());
            lines.Add("PASS: Launcher update signature verification");
            launcherUpdate.Sha256 = new string('B', 64);
            AssertThrows(() => LauncherUpdateManifestVerifier.Validate(launcherUpdate, rsa.ExportSubjectPublicKeyInfoPem()), "Launcher update tamper detection", lines);
            launcherUpdate.Sha256 = new string('A', 64);

            Assert(PluginManifestSignatureVerifier.Verify(manifest, publishers).IsTrusted, "Signed manifest verification", lines);
            Assert(manifest.SchemaVersion == 3 && manifest.RequiresLicenseAcceptance, "Plugin license metadata", lines);
            manifest.Description += " tampered";
            Assert(!PluginManifestSignatureVerifier.Verify(manifest, publishers).IsTrusted, "Manifest tamper detection", lines);
            manifest.Description = "Installer verification fixture";

            using var client = new HttpClient();
            var packageService = new PluginPackageService(client);
            var dataRoot = Path.Combine(testRoot, "data");
            var install = await packageService.InstallAsync(manifest, packagePath, dataRoot);
            Assert(File.Exists(Path.Combine(install.Definition.RootDirectory, "start.cmd")), "Secure ZIP installation", lines);
            Assert(install.Definition.InstalledVersion == "1.0.0" && install.Definition.Dependencies.SequenceEqual(["file:model.dat"]), "Version and dependency metadata", lines);
            Assert(InstalledPluginTrustValidator.Verify(install.Definition, publishers).IsTrusted, "Installed command trust validation", lines);
            install.Definition.Arguments = "--tampered";
            Assert(!InstalledPluginTrustValidator.Verify(install.Definition, publishers).IsTrusted, "Catalog tamper detection", lines);
            install.Definition.Arguments = manifest.Arguments;

            var assessment = ResourceScheduler.Assess(
                install.Definition.ToServiceProfileForSelfTest(),
                [12001],
                [12001, 12002]);
            Assert(assessment.BlocksLaunch && assessment.ManagedProcessIds.Contains(12001) && assessment.UnknownPortProcessIds.Contains(12002), "Resource and port conflict scheduling", lines);

            var uninstall = packageService.Uninstall(install.Definition, dataRoot);
            Assert(uninstall.FilesMoved && uninstall.BackupPath is not null && Directory.Exists(uninstall.BackupPath), "Recoverable plugin uninstall", lines);

            var settingsPath = Path.Combine(testRoot, "config", "launcher.settings.json");
            var expectedSettings = new LauncherSettings { DataRoot = dataRoot, WooshPort = 18001, StablePort = 18002, IndexTtsPort = 18003 };
            LauncherConfigurationStore.SaveAtomic(settingsPath, expectedSettings);
            var loadedSettings = LauncherConfigurationStore.LoadOrCreate(settingsPath, () => new LauncherSettings());
            Assert(loadedSettings.WooshPort == 18001 && File.Exists(settingsPath), "Atomic configuration save and load", lines);
            await File.WriteAllTextAsync(settingsPath, "{ invalid json", Encoding.UTF8);
            _ = LauncherConfigurationStore.LoadOrCreate(settingsPath, () => new LauncherSettings());
            Assert(Directory.EnumerateFiles(Path.Combine(Path.GetDirectoryName(settingsPath)!, "backups", "corrupt-config")).Any(), "Corrupt configuration archival", lines);
            var previousToken = Environment.GetEnvironmentVariable("HF_TOKEN");
            try
            {
                Environment.SetEnvironmentVariable("HF_TOKEN", "self-test-secret-token");
                var diagnosticsPath = Path.Combine(testRoot, "diagnostics.txt");
                new LauncherDiagnosticsService().Export(diagnosticsPath, ["token=self-test-secret-token"]);
                Assert(!File.ReadAllText(diagnosticsPath).Contains("self-test-secret-token", StringComparison.Ordinal), "Diagnostic secret redaction", lines);
            }
            finally
            {
                Environment.SetEnvironmentVariable("HF_TOKEN", previousToken);
            }
            lines.Add("SELF TEST PASSED");
            await WriteReportAsync(reportPath, lines);
            return 0;
        }
        catch (Exception ex)
        {
            lines.Add("SELF TEST FAILED");
            lines.Add(ex.ToString());
            await WriteReportAsync(reportPath, lines);
            return 1;
        }
        finally
        {
            if (Directory.Exists(testRoot))
            {
                Directory.Delete(testRoot, true);
            }
        }
    }

    private static ServiceProfile ToServiceProfileForSelfTest(this LauncherModelDefinition definition)
        => new(definition.DisplayName, definition.Description, definition.RootDirectory, Path.Combine(definition.RootDirectory, definition.Executable), definition.Arguments, definition.Port, definition.IsHighVram, definition.RequiredFiles, 0, 0, definition.Dependencies);

    private static void Assert(bool condition, string name, ICollection<string> lines)
    {
        if (!condition)
        {
            throw new InvalidOperationException($"Self-test assertion failed: {name}");
        }
        lines.Add("PASS: " + name);
    }

    private static void AssertThrows(Action action, string name, ICollection<string> lines)
    {
        try
        {
            action();
        }
        catch
        {
            lines.Add("PASS: " + name);
            return;
        }
        throw new InvalidOperationException($"Self-test assertion failed: {name}");
    }

    private static async Task WriteReportAsync(string reportPath, IEnumerable<string> lines)
    {
        var fullPath = Path.GetFullPath(reportPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        await File.WriteAllLinesAsync(fullPath, lines, Encoding.UTF8);
    }
}
