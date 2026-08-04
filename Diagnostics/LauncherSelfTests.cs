using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace BaChenAiLauncher;

internal static class LauncherSelfTests
{
    public static async Task<int> RunManagedPythonSmokeTestAsync(string dataRoot, string reportPath)
    {
        var lines = new List<string>();
        try
        {
            Directory.CreateDirectory(dataRoot);
            using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("BaChen-Managed-Python-Smoke-Test");
            var service = new ManagedPythonRuntimeService(client);
            foreach (var runtime in ManagedPythonRuntimeService.Supported)
            {
                var python = await service.EnsureAsync(runtime.Id, dataRoot);
                lines.Add($"PASS: Installed portable Python {runtime.Version} at {python}");
                var environment = Path.Combine(dataRoot, "smoke-environments", runtime.Id);
                await RunCheckedProcessAsync(python, ["-m", "venv", environment], dataRoot);
                var environmentPython = Path.Combine(environment, "Scripts", "python.exe");
                await RunCheckedProcessAsync(
                    environmentPython,
                    ["-c", "import pip, sys, venv; print(sys.version); print(pip.__version__)"],
                    dataRoot);
                lines.Add($"PASS: Portable Python {runtime.Version} creates a venv with pip");
            }
            lines.Add("MANAGED PYTHON SMOKE TEST PASSED");
            await WriteReportAsync(reportPath, lines);
            return 0;
        }
        catch (Exception ex)
        {
            lines.Add("FAIL: " + ex);
            await WriteReportAsync(reportPath, lines);
            return 1;
        }
    }

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
            AssertServiceControlLayout(760, lines);
            AssertServiceControlLayout(950, lines);
            AssertServiceControlLayout(1240, lines);
            AssertPluginUpdateActionLayout(lines);
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
                SchemaVersion = 5,
                Id = "self-test-plugin",
                DisplayName = "Self Test Plugin",
                Version = "1.0.0",
                Publisher = "BaChen Self Test",
                Category = "Utilities",
                Description = "Installer verification fixture",
                Executable = "start.cmd",
                Arguments = "--port {port}",
                Runtime = "python",
                RuntimeVersion = ">=3.10",
                Port = 17862,
                RequiredFiles = ["model.dat"],
                Dependencies = ["file:model.dat"],
                GitHubRepository = "example/self-test-plugin",
                PackageUrl = "https://example.com/self-test-plugin.zip",
                PackageSha256 = packageHash,
                PackageSizeBytes = new FileInfo(packagePath).Length,
                PreservedPaths = ["models", "outputs"],
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
            LauncherUpdateManifest CreateSignedLauncherUpdate(string version)
            {
                var update = new LauncherUpdateManifest
                {
                    Version = version,
                    MinimumCompatibleVersion = launcherUpdate.MinimumCompatibleVersion,
                    DownloadUrl = $"https://github.com/Bachen-beep/bachen-ai-launcher/releases/download/v{version}/BaChen.AI.Launcher.exe",
                    Sha256 = launcherUpdate.Sha256,
                    ReleaseNotesUrl = $"https://github.com/Bachen-beep/bachen-ai-launcher/releases/tag/v{version}",
                    PublishedAt = launcherUpdate.PublishedAt.AddMinutes(Version.Parse(version).Minor)
                };
                var signedPayload = Encoding.UTF8.GetBytes(LauncherUpdateManifestVerifier.CreateCanonicalPayload(update));
                update.Signature.Value = Convert.ToBase64String(rsa.SignData(signedPayload, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
                update.Signature.KeyId = LauncherUpdateManifestVerifier.KeyId;
                return update;
            }

            var previewReleaseJson = """
                [
                  {
                    "draft": false,
                    "prerelease": true,
                    "assets": [
                      {
                        "name": "launcher-update.json",
                        "browser_download_url": "https://github.com/Bachen-beep/bachen-ai-launcher/releases/download/v99.0.0-stage2-preview/launcher-update.json"
                      }
                    ]
                  }
                ]
                """;
            using var updateClient = new HttpClient(new StaticJsonHandler(previewReleaseJson));
            var updateService = new LauncherSelfUpdateService(updateClient);
            Assert((await updateService.ResolveManifestUriAsync(LauncherUpdateChannel.Stable)) == LauncherSelfUpdateService.DefaultManifestUri, "Stable update channel resolution", lines);
            var previewManifestUri = await updateService.ResolveManifestUriAsync(LauncherUpdateChannel.Preview);
            Assert(previewManifestUri.AbsoluteUri.Contains("stage2-preview/launcher-update.json", StringComparison.Ordinal), "Preview update channel resolution", lines);

            var oldStableUpdate = CreateSignedLauncherUpdate("98.0.0");
            var tamperedHighUpdate = CreateSignedLauncherUpdate("100.0.0");
            tamperedHighUpdate.Sha256 = new string('F', 64);
            var multiSourceHandler = new MultiSourceUpdateHandler(new Dictionary<string, string>
            {
                ["/old.json"] = JsonSerializer.Serialize(oldStableUpdate),
                ["/latest.json"] = JsonSerializer.Serialize(launcherUpdate),
                ["/tampered.json"] = JsonSerializer.Serialize(tamperedHighUpdate),
                ["/api/latest"] = "{\"assets\":[{\"name\":\"launcher-update.json\",\"browser_download_url\":\"https://updates.example/latest.json\"}]}"
            });
            using (var multiSourceClient = new HttpClient(multiSourceHandler))
            {
                var multiSourceService = new LauncherSelfUpdateService(
                    multiSourceClient,
                    () => rsa.ExportSubjectPublicKeyInfoPem(),
                    [
                        new LauncherUpdateSource("Cached feed", new Uri("https://updates.example/old.json")),
                        new LauncherUpdateSource("Current direct release", new Uri("https://updates.example/latest.json")),
                        new LauncherUpdateSource("Release API fallback", new Uri("https://updates.example/api/latest"), true),
                        new LauncherUpdateSource("Tampered high version", new Uri("https://updates.example/tampered.json")),
                        new LauncherUpdateSource("Unavailable feed", new Uri("https://updates.example/missing.json"))
                    ]);
                var selectedUpdate = await multiSourceService.CheckAsync(LauncherUpdateChannel.Stable);
                Assert(selectedUpdate.LatestVersion == new Version(99, 0, 0) && selectedUpdate.SelectedSource == "Current direct release", "Stable direct sources select the highest signed version", lines);
                Assert(selectedUpdate.HasSourceConflict && selectedUpdate.Sources.Count == 5, "Stable multi-source conflict diagnostics", lines);
                Assert(selectedUpdate.Sources.Single(source => source.Name == "Tampered high version").IsValid == false, "Unsigned higher update source is rejected", lines);
                Assert(!multiSourceHandler.RequestedPaths.Contains("/api/latest") && selectedUpdate.Sources.Single(source => source.Name == "Release API fallback").Detail.StartsWith("skipped", StringComparison.Ordinal), "Valid direct stable sources skip the anonymous GitHub API", lines);
                Assert(multiSourceHandler.AllRequestsBypassCache, "Launcher update requests bypass ordinary caches", lines);
                var stale = await AssertThrowsAsync<LauncherUpdateStaleException>(
                    () => multiSourceService.CheckAsync(LauncherUpdateChannel.Stable, highestObservedVersion: new Version(100, 0, 0)),
                    "Previously observed stable version rejects stale sources",
                    lines);
                Assert(stale.RemoteVersion == new Version(99, 0, 0), "Stale update source classification", lines);
                Assert(multiSourceHandler.RequestedPaths.Contains("/api/latest"), "Anonymous GitHub API is used only when direct sources are older than the verified version", lines);
            }
            using (var noPreviewClient = new HttpClient(new StaticJsonHandler("[]")))
            {
                var noPreviewService = new LauncherSelfUpdateService(noPreviewClient);
                var unavailable = await AssertThrowsAsync<LauncherUpdateUnavailableException>(
                    () => noPreviewService.ResolveManifestUriAsync(LauncherUpdateChannel.Preview),
                    "Missing preview release classification",
                    lines);
                Assert(unavailable.Channel == LauncherUpdateChannel.Preview, "Missing preview release channel", lines);
            }
            using (var serverFailureClient = new HttpClient(new StatusCodeHandler(System.Net.HttpStatusCode.InternalServerError)))
            {
                var serverFailureService = new LauncherSelfUpdateService(
                    serverFailureClient,
                    () => rsa.ExportSubjectPublicKeyInfoPem(),
                    [new LauncherUpdateSource("Only source", new Uri("https://updates.example/stable.json"))]);
                await AssertThrowsAsync<LauncherUpdateUnavailableException>(
                    () => serverFailureService.CheckAsync(LauncherUpdateChannel.Stable),
                    "All stable update sources unavailable classification",
                    lines);
            }
            using (var previewRateLimitedClient = new HttpClient(new PreviewRateLimitFallbackHandler(JsonSerializer.Serialize(launcherUpdate))))
            {
                var previewRateLimitedService = new LauncherSelfUpdateService(
                    previewRateLimitedClient,
                    () => rsa.ExportSubjectPublicKeyInfoPem(),
                    [
                        new LauncherUpdateSource("GitHub Raw", LauncherSelfUpdateService.StableFeedUri),
                        new LauncherUpdateSource("GitHub Latest", LauncherSelfUpdateService.DefaultManifestUri),
                        new LauncherUpdateSource("GitHub Release API", LauncherSelfUpdateService.LatestReleaseApiUri, true)
                    ]);
                Assert(await previewRateLimitedService.ResolveManifestUriWithFallbackAsync(LauncherUpdateChannel.Preview) == LauncherSelfUpdateService.DefaultManifestUri, "Preview API rate limit falls back to stable manifest", lines);
                var previewFallback = await previewRateLimitedService.CheckAsync(LauncherUpdateChannel.Preview);
                Assert(previewFallback.LatestVersion == new Version(99, 0, 0) && previewFallback.SelectedSource != "GitHub Preview API", "Preview update check automatically uses signed stable sources after API rate limiting", lines);
            }

            var offlineDataPath = Path.Combine(testRoot, "offline-existing-plugin.dat");
            await File.WriteAllTextAsync(offlineDataPath, "preserve", Encoding.ASCII);
            using (var offlineClient = new HttpClient(new FailureHandler(new HttpRequestException("simulated offline network"))))
            {
                var offlineService = new LauncherSelfUpdateService(offlineClient);
                await AssertThrowsAsync(
                    () => offlineService.CheckAsync(LauncherUpdateChannel.Stable),
                    "Offline update failure isolation",
                    lines);
            }
            Assert(File.ReadAllText(offlineDataPath, Encoding.ASCII) == "preserve", "Offline failure preserves plugin data", lines);
            using (var proxyHandler = new HttpClientHandler
            {
                Proxy = new System.Net.WebProxy("http://127.0.0.1:1"),
                UseProxy = true
            })
            using (var proxyClient = new HttpClient(proxyHandler) { Timeout = TimeSpan.FromSeconds(3) })
            {
                var proxyService = new LauncherSelfUpdateService(proxyClient);
                await AssertThrowsAsync(
                    () => proxyService.CheckAsync(LauncherUpdateChannel.Stable),
                    "Proxy-restricted update failure isolation",
                    lines);
            }
            Assert(File.ReadAllText(offlineDataPath, Encoding.ASCII) == "preserve", "Proxy failure preserves plugin data", lines);

            var interruptedRoot = Path.Combine(testRoot, "interrupted-download");
            using (var interruptedClient = new HttpClient(new FailureHandler(new IOException("simulated interrupted download"))))
            {
                var interruptedService = new LauncherSelfUpdateService(interruptedClient);
                await AssertThrowsAsync(
                    () => interruptedService.DownloadVerifiedAsync(launcherUpdate, interruptedRoot),
                    "Interrupted update download rejection",
                    lines);
            }
            Assert(!Directory.Exists(interruptedRoot), "Interrupted update staging cleanup", lines);

            var verifiedUpdateBytes = Enumerable.Range(0, 1024 * 1024).Select(index => (byte)(index % 251)).ToArray();
            var verifiedUpdateManifest = new LauncherUpdateManifest
            {
                Version = "99.0.1",
                DownloadUrl = "https://updates.example/BaChen.AI.Launcher.exe",
                Sha256 = Convert.ToHexString(SHA256.HashData(verifiedUpdateBytes))
            };
            var updateProgress = new List<LauncherUpdateProgress>();
            var verifiedUpdateRoot = Path.Combine(testRoot, "verified-update-download");
            using (var verifiedUpdateClient = new HttpClient(new StaticBytesHandler(verifiedUpdateBytes)))
            {
                var verifiedUpdateService = new LauncherSelfUpdateService(verifiedUpdateClient);
                var downloadedPath = await verifiedUpdateService.DownloadVerifiedAsync(
                    verifiedUpdateManifest,
                    verifiedUpdateRoot,
                    new CallbackProgress<LauncherUpdateProgress>(updateProgress.Add));
                Assert((await File.ReadAllBytesAsync(downloadedPath)).SequenceEqual(verifiedUpdateBytes), "Verified launcher update download", lines);
            }
            Assert(updateProgress.Any(progress => progress.Stage == LauncherUpdateProgressStage.Downloading && progress.CompletedBytes == verifiedUpdateBytes.Length), "Launcher update download progress completion", lines);
            Assert(updateProgress.Any(progress => progress.Stage == LauncherUpdateProgressStage.Downloading && progress.BytesPerSecond > 0), "Launcher update download speed reporting", lines);
            Assert(updateProgress.Any(progress => progress.Stage == LauncherUpdateProgressStage.Verifying && progress.CompletedBytes == verifiedUpdateBytes.Length), "Launcher update verification progress completion", lines);
            var resumedLauncherRoot = Path.Combine(testRoot, "resumed-launcher-update");
            Directory.CreateDirectory(resumedLauncherRoot);
            var resumedLauncherPartial = Path.Combine(resumedLauncherRoot, "BaChen AI Launcher.exe.partial");
            await File.WriteAllBytesAsync(resumedLauncherPartial, verifiedUpdateBytes[..(verifiedUpdateBytes.Length / 2)]);
            var resumedLauncherHandler = new RetryingRangeHandler(verifiedUpdateBytes, failuresBeforeSuccess: 2);
            using (var resumedLauncherClient = new HttpClient(resumedLauncherHandler))
            {
                var resumedLauncherService = new LauncherSelfUpdateService(resumedLauncherClient);
                var resumedPath = await resumedLauncherService.DownloadVerifiedAsync(verifiedUpdateManifest, resumedLauncherRoot);
                Assert((await File.ReadAllBytesAsync(resumedPath)).SequenceEqual(verifiedUpdateBytes), "Resumable launcher update survives transient EOF", lines);
                Assert(!File.Exists(resumedLauncherPartial), "Resumable launcher partial file finalization", lines);
            }
            Assert(resumedLauncherHandler.RangeRequests > 0, "Launcher update retries with HTTP Range", lines);
            Assert(LauncherForm.FormatTransferSpeed(12.5D * 1024D * 1024D) == "12.5 MB/s", "Launcher update download speed presentation", lines);
            Assert(LauncherForm.FormatTransferSpeed(640D * 1024D) == "640.0 KB/s", "Launcher update low-speed presentation", lines);
            var sampledAt = TimeSpan.Zero;
            var transferRateTracker = new TransferRateTracker(() => sampledAt);
            sampledAt = TimeSpan.FromSeconds(2);
            var firstRate = transferRateTracker.Sample(2L * 1024L * 1024L);
            sampledAt = TimeSpan.FromSeconds(3);
            var secondRate = transferRateTracker.Sample(3L * 1024L * 1024L);
            Assert(firstRate == 1024D * 1024D && secondRate == 1024D * 1024D, "Shared transfer rate sampling", lines);
            var pluginDownloadProgress = new SourceUpdateProgress(SourceUpdateProgressStage.Downloading, 50, 100, 12.5D * 1024D * 1024D);
            Assert(LauncherForm.FormatPluginUpdateStatus(pluginDownloadProgress, false) == "正在下载更新 50% · 12.5 MB/s", "Plugin update Chinese speed presentation", lines);
            Assert(LauncherForm.FormatPluginUpdateStatus(pluginDownloadProgress, true) == "Downloading update 50% · 12.5 MB/s", "Plugin update English speed presentation", lines);
            Assert(!LauncherForm.FormatPluginUpdateStatus(new SourceUpdateProgress(SourceUpdateProgressStage.Installing, 50, 100), false).Contains("/s", StringComparison.Ordinal), "Plugin install phase hides transfer speed", lines);

            Assert(PluginManifestSignatureVerifier.Verify(manifest, publishers).IsTrusted, "Signed manifest verification", lines);
            Assert(manifest.SchemaVersion == 5 && manifest.RequiresLicenseAcceptance, "Plugin license metadata", lines);
            manifest.Description += " tampered";
            Assert(!PluginManifestSignatureVerifier.Verify(manifest, publishers).IsTrusted, "Manifest tamper detection", lines);
            manifest.Description = "Installer verification fixture";

            using (var offlineCatalogClient = new HttpClient(new FailureHandler(new HttpRequestException("simulated offline catalog"))))
            {
                var signedCatalog = await new PluginCatalogService(offlineCatalogClient).LoadAsync();
                Assert(signedCatalog.Plugins.Count > 0 && signedCatalog.Plugins.All(plugin => plugin.SchemaVersion == 6), "Bundled signed plugin catalog fallback", lines);
                var catalogPlugin = signedCatalog.Plugins[0];
                var catalogInstallRoot = Path.Combine(testRoot, "signed-catalog-plugin");
                Directory.CreateDirectory(catalogInstallRoot);
                await File.WriteAllTextAsync(
                    Path.Combine(catalogInstallRoot, ".bachen-plugin-manifest.json"),
                    System.Text.Json.JsonSerializer.Serialize(catalogPlugin, new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase }),
                    Encoding.UTF8);
                PluginCatalogIndexVerifier.WriteInstalledCopy(signedCatalog, catalogInstallRoot);
                var catalogDefinition = DefinitionFromManifestForSelfTest(catalogPlugin, catalogInstallRoot);
                Assert(InstalledPluginTrustValidator.Verify(catalogDefinition, publishers).IsTrusted, "Installed signed catalog trust verification", lines);
                catalogDefinition.Arguments += " --tampered";
                Assert(!InstalledPluginTrustValidator.Verify(catalogDefinition, publishers).IsTrusted, "Signed catalog command tamper detection", lines);
                catalogDefinition.Arguments = catalogPlugin.Arguments;
                signedCatalog.Plugins[0].Description += " tampered";
                AssertThrows(() => PluginCatalogIndexVerifier.Validate(signedCatalog), "Plugin catalog signature tamper detection", lines);
            }

            var assetSource = Path.Combine(testRoot, "asset-source");
            Directory.CreateDirectory(Path.Combine(assetSource, "tiny-model"));
            await File.WriteAllTextAsync(Path.Combine(assetSource, "tiny-model", "model.bin"), "model-asset", Encoding.ASCII);
            var assetPackagePath = Path.Combine(testRoot, "tiny-model.zip");
            ZipFile.CreateFromDirectory(assetSource, assetPackagePath);
            var assetBytes = await File.ReadAllBytesAsync(assetPackagePath);
            var assetHash = Convert.ToHexString(SHA256.HashData(assetBytes));
            var versionSixManifest = CloneManifest(manifest);
            versionSixManifest.SchemaVersion = 6;
            versionSixManifest.Id = "version-six-plugin";
            versionSixManifest.CreateVirtualEnvironment = false;
            versionSixManifest.ManagedRuntimeId = string.Empty;
            versionSixManifest.PythonInstallArguments = [];
            versionSixManifest.RequiredFiles = ["start.cmd", "models/tiny-model/model.bin"];
            versionSixManifest.Dependencies = ["file:models/tiny-model/model.bin"];
            versionSixManifest.AssetPackages =
            [
                new PluginAssetPackage
                {
                    Id = "tiny-model",
                    Url = "https://example.com/tiny-model.zip",
                    Sha256 = assetHash,
                    SizeBytes = assetBytes.Length,
                    DestinationPath = "models"
                }
            ];
            ResignManifest(versionSixManifest, rsa);
            Assert(PluginManifestSignatureVerifier.Verify(versionSixManifest, publishers).IsTrusted, "Manifest v6 signature verification", lines);
            versionSixManifest.AssetPackages[0].DestinationPath = "tampered-models";
            Assert(!PluginManifestSignatureVerifier.Verify(versionSixManifest, publishers).IsTrusted, "Manifest v6 asset tamper detection", lines);
            versionSixManifest.AssetPackages[0].DestinationPath = "models";
            ResignManifest(versionSixManifest, rsa);
            using (var assetClient = new HttpClient(new StaticBytesHandler(assetBytes)))
            {
                var versionSixDataRoot = Path.Combine(testRoot, "version-six-data");
                var installStages = new List<string>();
                var versionSixInstall = await new PluginPackageService(assetClient).InstallAsync(
                    versionSixManifest,
                    packagePath,
                    versionSixDataRoot,
                    new CallbackProgress<string>(installStages.Add));
                Assert(File.Exists(Path.Combine(versionSixInstall.Definition.RootDirectory, "models", "tiny-model", "model.bin")), "Manifest v6 verified asset extraction", lines);
                var preflight = PluginInstallPreflightService.Assess(versionSixManifest, versionSixDataRoot, () => (0, 8192));
                Assert(preflight.RequiredDiskBytes >= (versionSixManifest.PackageSizeBytes + assetBytes.Length) * 2, "Asset packages included in disk preflight", lines);
                Assert(installStages.Any(stage => stage.StartsWith("Verifying plugin package", StringComparison.Ordinal)) &&
                    installStages.Any(stage => stage.StartsWith("Extracting plugin package", StringComparison.Ordinal)) &&
                    installStages.Any(stage => stage.StartsWith("Downloading model asset", StringComparison.Ordinal)) &&
                    installStages.Any(stage => stage.StartsWith("Extracting model asset", StringComparison.Ordinal)) &&
                    installStages.Any(stage => stage.StartsWith("Validating installed plugin files", StringComparison.Ordinal)) &&
                    installStages.Any(stage => stage.StartsWith("Activating plugin", StringComparison.Ordinal)) &&
                    installStages.Any(stage => stage.StartsWith("Plugin installation complete", StringComparison.Ordinal)),
                    "Plugin installation emits visible lifecycle stages", lines);
            }
            Assert(FirstRunWizardForm.CalculateOverallPercentage(0, 2, 50) == 25 &&
                FirstRunWizardForm.CalculateOverallPercentage(1, 2, 100) == 100 &&
                FirstRunWizardForm.CalculateOverallPercentage(0, 0, 100) == 0,
                "First-run overall installation progress calculation", lines);
            Assert(FirstRunWizardForm.EstimateStagePercentage("Installing Python dependencies") == 82 &&
                FirstRunWizardForm.EstimateStagePercentage("Plugin installation complete") == 100,
                "First-run installation stage presentation mapping", lines);
            Assert(ManagedPythonRuntimeService.Python311.Id == "python-3.11.9-x64" &&
                ManagedPythonRuntimeService.Python311.Url.EndsWith("python.3.11.9.nupkg", StringComparison.Ordinal) &&
                ManagedPythonRuntimeService.Python311.SizeBytes == 17478009 &&
                ManagedPythonRuntimeService.Python311.Sha256 == "9283876D58C017E0E846F95B490DA3BCA0FC0A6EE1134B2870677CFB7EEC3C67" &&
                ManagedPythonRuntimeService.Python312.Id == "python-3.12.10-x64" &&
                ManagedPythonRuntimeService.Python312.Url.EndsWith("python.3.12.10.nupkg", StringComparison.Ordinal) &&
                ManagedPythonRuntimeService.Python312.SizeBytes == 14515433 &&
                ManagedPythonRuntimeService.Python312.Sha256 == "0EB85C2DFCCCCF1B17352DE4C397F69194035B7D37149EACC16F1147D93DE3B8",
                "Portable managed Python runtimes are pinned by version size and SHA-256", lines);
            var portablePackage = Path.Combine(testRoot, "portable-python.nupkg");
            using (var archive = ZipFile.Open(portablePackage, ZipArchiveMode.Create))
            {
                var pythonEntry = archive.CreateEntry("tools/python.exe");
                await using var pythonStream = pythonEntry.Open();
                await pythonStream.WriteAsync("fixture"u8.ToArray());
            }
            var portableStaging = Path.Combine(testRoot, "portable-python-staging");
            Directory.CreateDirectory(portableStaging);
            var extractedPortableRoot = ManagedPythonRuntimeService.ExtractPortablePackage(portablePackage, portableStaging);
            Assert(File.Exists(Path.Combine(extractedPortableRoot, "python.exe")), "Portable managed Python package extraction", lines);
            Assert(ManagedPythonRuntimeService.SelectForConstraint(">=3.10,<3.12") == ManagedPythonRuntimeService.Python311, "Python 3.11 selected for upper-bounded repository", lines);
            Assert(ManagedPythonRuntimeService.SelectForConstraint(">=3.12") == ManagedPythonRuntimeService.Python312, "Python 3.12 selected for modern repository", lines);
            Assert(ManagedPythonRuntimeService.SatisfiesConstraint(new Version(3, 11, 9), ">=3.10,<3.12") &&
                !ManagedPythonRuntimeService.SatisfiesConstraint(new Version(3, 12, 10), ">=3.10,<3.12"),
                "Python compound constraint evaluation", lines);
            AssertThrows(() => ManagedPythonRuntimeService.SelectForConstraint(">=3.13"), "Unsupported Python constraint fails before environment creation", lines);

            using var client = new HttpClient();
            var packageService = new PluginPackageService(client);
            var dataRoot = Path.Combine(testRoot, "data");
            var install = await packageService.InstallAsync(manifest, packagePath, dataRoot);
            Assert(File.Exists(Path.Combine(install.Definition.RootDirectory, "start.cmd")), "Secure ZIP installation", lines);
            Assert(install.Definition.InstalledVersion == "1.0.0" && install.Definition.Dependencies.SequenceEqual(["file:model.dat"]), "Version and dependency metadata", lines);
            Assert(install.Definition.Runtime == "python" && install.Definition.RuntimeVersion == ">=3.10", "Structured runtime metadata", lines);
            Assert(install.Definition.PackageSizeBytes == new FileInfo(packagePath).Length, "Package size metadata", lines);
            Assert(install.Definition.PreservedPaths.SequenceEqual(["models", "outputs"]), "Preserved path metadata", lines);
            Assert(InstalledPluginTrustValidator.Verify(install.Definition, publishers).IsTrusted, "Installed command trust validation", lines);
            install.Definition.Arguments = "--tampered";
            Assert(!InstalledPluginTrustValidator.Verify(install.Definition, publishers).IsTrusted, "Catalog tamper detection", lines);
            install.Definition.Arguments = manifest.Arguments;

            var sizeMismatchManifest = CloneManifest(manifest);
            sizeMismatchManifest.PackageSizeBytes++;
            ResignManifest(sizeMismatchManifest, rsa);
            await AssertThrowsAsync<InvalidDataException>(
                () => packageService.InstallAsync(sizeMismatchManifest, packagePath, Path.Combine(testRoot, "size-mismatch")),
                "Package size mismatch rejection",
                lines);

            var legacyManifest = CloneManifest(manifest);
            legacyManifest.SchemaVersion = 3;
            legacyManifest.Runtime = string.Empty;
            legacyManifest.RuntimeVersion = string.Empty;
            legacyManifest.PackageSizeBytes = 0;
            legacyManifest.PreservedPaths = [];
            ResignManifest(legacyManifest, rsa);
            var legacyInstall = await packageService.InstallAsync(legacyManifest, packagePath, Path.Combine(testRoot, "legacy-v3"));
            Assert(File.Exists(Path.Combine(legacyInstall.Definition.RootDirectory, "start.cmd")), "Legacy manifest v3 compatibility", lines);

            Assert(new LauncherModelCatalog().Models.Count == 0, "Clean model catalog contains no built-in plugins", lines);
            Assert(GitHubModelImportService.TryNormalizeRepository("https://github.com/SonyResearch/Woosh.git", out var normalizedRepository) && normalizedRepository == "SonyResearch/Woosh", "Full GitHub repository URL normalization", lines);
            Assert(GitHubModelImportService.TryNormalizeRepository("git@github.com:owner/repository.git", out normalizedRepository) && normalizedRepository == "owner/repository", "GitHub SSH repository normalization", lines);
            Assert(!GitHubModelImportService.TryNormalizeRepository("https://example.com/owner/repository", out _), "Non-GitHub repository URL rejection", lines);
            Assert(LauncherForm.TryValidateProxyUrl("http://127.0.0.1:7890", out _) && !LauncherForm.TryValidateProxyUrl("http://user:secret@127.0.0.1:7890", out _), "GitHub proxy URL validation", lines);
            var parsedGpus = SystemResourceProbe.ParseGpuOutput("0, NVIDIA GeForce RTX 4060, 512, 8188\r\n1, NVIDIA GeForce RTX 4090, 1024, 24564\r\n");
            Assert(parsedGpus.Count == 2 && parsedGpus.OrderByDescending(gpu => gpu.TotalMiB).First().Name == "NVIDIA GeForce RTX 4090", "Actual multi-GPU model parsing and primary selection", lines);
            Assert(SystemResourceProbe.FormatGpuUsageGiB(1024, 8188) == "1.00 / 8.00 GiB", "GPU MiB to GiB display formatting", lines);
            var downwardLogBounds = LauncherForm.CalculateDownwardLogBounds(new Rectangle(100, 100, 1200, 800), 210, 72, new Rectangle(0, 0, 1920, 1080));
            Assert(downwardLogBounds.Top == 100 && downwardLogBounds.Height == 938, "Runtime log expands the window downward", lines);
            var edgeAdjustedLogBounds = LauncherForm.CalculateDownwardLogBounds(new Rectangle(100, 200, 1200, 800), 210, 72, new Rectangle(0, 0, 1920, 1080));
            Assert(edgeAdjustedLogBounds.Top == 142 && edgeAdjustedLogBounds.Bottom == 1080, "Downward runtime log remains inside the working area", lines);

            var gitHubSourceRoot = Path.Combine(testRoot, "github-source");
            Directory.CreateDirectory(gitHubSourceRoot);
            await File.WriteAllTextAsync(Path.Combine(gitHubSourceRoot, "app.py"), "print('ready')", Encoding.UTF8);
            var gitHubArchive = Path.Combine(testRoot, "github-source.zip");
            ZipFile.CreateFromDirectory(gitHubSourceRoot, gitHubArchive, CompressionLevel.Fastest, true);
            var gitHubArchiveBytes = await File.ReadAllBytesAsync(gitHubArchive);
            const string importedCommit = "0123456789abcdef0123456789abcdef01234567";
            using (var importClient = new HttpClient(new GitHubImportHandler(importedCommit, gitHubArchiveBytes)))
            {
                importClient.DefaultRequestHeaders.UserAgent.ParseAdd("BaChen-Self-Test");
                var importRoot = Path.Combine(testRoot, "github-import-data");
                var imported = await new GitHubModelImportService(importClient).ImportAsync("https://github.com/example/model-repo.git", string.Empty, importRoot);
                Assert(imported.CommitSha == importedCommit && imported.Branch == "main" && File.Exists(Path.Combine(imported.RootDirectory, "app.py")), "GitHub model import pinned to immutable commit and default branch", lines);
                Assert(LauncherForm.DetectPythonEntryPoint(imported.RootDirectory) == "app.py", "Python launch entry auto-detection", lines);
                Assert(File.Exists(Path.Combine(imported.RootDirectory, ".bachen-github-source.json")), "GitHub import provenance metadata", lines);
                Assert(File.Exists(Path.Combine(imported.RootDirectory, ".bachen-ai-launcher-update.json")), "GitHub import creates an initial update baseline", lines);
                var reused = await new GitHubModelImportService(importClient).ImportAsync("example/model-repo", "main", importRoot);
                Assert(reused.RootDirectory == imported.RootDirectory, "Verified GitHub source reuse after setup retry", lines);
                File.Delete(Path.Combine(imported.RootDirectory, "app.py"));
                var repaired = await new GitHubModelImportService(importClient).ImportAsync("example/model-repo", "main", importRoot);
                Assert(File.Exists(Path.Combine(repaired.RootDirectory, "app.py")), "Incomplete cached GitHub source is automatically restored", lines);
                var customInstallDirectory = Path.Combine(testRoot, "custom-plugin-location");
                Directory.CreateDirectory(customInstallDirectory);
                var customImported = await new GitHubModelImportService(importClient).ImportAsync("example/custom-location", "main", importRoot, customInstallDirectory);
                Assert(customImported.RootDirectory == Path.GetFullPath(customInstallDirectory) && File.Exists(Path.Combine(customInstallDirectory, "app.py")), "User-selected empty GitHub plugin install directory", lines);
            }
            using (var rateLimitedImportClient = new HttpClient(new RateLimitedGitHubImportHandler(importedCommit, gitHubArchiveBytes)))
            {
                rateLimitedImportClient.DefaultRequestHeaders.UserAgent.ParseAdd("BaChen-Self-Test");
                var rateLimitedRoot = Path.Combine(testRoot, "github-rate-limited-import");
                var imported = await new GitHubModelImportService(rateLimitedImportClient).ImportAsync("example/rate-limited", string.Empty, rateLimitedRoot);
                Assert(imported.Branch == "main" && imported.CommitSha == importedCommit && File.Exists(Path.Combine(imported.RootDirectory, "app.py")), "GitHub rate limit falls back to the public Atom feed", lines);
            }

            var analyzedWooshRoot = Path.Combine(testRoot, "analyzed-woosh");
            Directory.CreateDirectory(analyzedWooshRoot);
            await File.WriteAllTextAsync(Path.Combine(analyzedWooshRoot, "README.md"), "# Woosh\n\nText-to-audio sound effect generation.\n\nuv sync --extra cuda\n", Encoding.UTF8);
            await File.WriteAllTextAsync(Path.Combine(analyzedWooshRoot, "pyproject.toml"), "[project]\nname = \"woosh\"\ndescription = \"Sound effect foundation model\"\nrequires-python = \">=3.12\"\n[project.optional-dependencies]\ncuda = [\"torch\"]\ncpu = [\"torch\"]\n[tool.uv]\n", Encoding.UTF8);
            await File.WriteAllTextAsync(Path.Combine(analyzedWooshRoot, "gradio_Woosh-Flow.py"), "# --server-name --server-port\n", Encoding.UTF8);
            await File.WriteAllTextAsync(Path.Combine(analyzedWooshRoot, "gradio_Woosh-DFlow.py"), "# --server-name --server-port\n", Encoding.UTF8);
            var wooshAnalysis = GitHubRepositoryAnalyzer.Analyze("SonyResearch/Woosh", analyzedWooshRoot, true);
            Assert(wooshAnalysis.LaunchOptions.Length == 2 && wooshAnalysis.LaunchOptions[0].EntryScript == "gradio_Woosh-DFlow.py", "Repository analyzer recommends Woosh DFlow", lines);
            Assert(wooshAnalysis.LaunchOptions[0].Arguments.Contains("--server-port {port}", StringComparison.Ordinal) && wooshAnalysis.EnvironmentManager == "uv" && wooshAnalysis.EnvironmentArguments.Contains("cuda"), "Repository analyzer configures Woosh port and CUDA uv profile", lines);
            Assert(KnownRepositoryAssetService.GetRequiredFiles("SonyResearch/Woosh", wooshAnalysis.LaunchOptions[0].Arguments).SequenceEqual(["checkpoints/Woosh-AE/config.yaml", "checkpoints/Woosh-AE/weights.safetensors", "checkpoints/TextConditionerA/config.yaml", "checkpoints/TextConditionerA/weights.safetensors", "checkpoints/Woosh-DFlow/config.yaml", "checkpoints/Woosh-DFlow/weights.safetensors"]), "Woosh DFlow import requires checkpoint files rather than placeholder directories", lines);
            Assert(KnownRepositoryAssetService.GetRequiredFiles("example/generic", wooshAnalysis.LaunchOptions[0].Arguments).Length == 0, "Generic imports do not receive Woosh checkpoint requirements", lines);
            var nestedCheckpointRoot = Path.Combine(analyzedWooshRoot, "checkpoints", "checkpoints");
            foreach (var relative in KnownRepositoryAssetService.GetRequiredFiles("SonyResearch/Woosh", wooshAnalysis.LaunchOptions[0].Arguments))
            {
                var nestedFile = Path.Combine(nestedCheckpointRoot, relative["checkpoints/".Length..].Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(nestedFile)!);
                await File.WriteAllTextAsync(nestedFile, "fixture", Encoding.ASCII);
            }
            KnownRepositoryAssetService.RepairNestedCheckpointLayout("SonyResearch/Woosh", wooshAnalysis.LaunchOptions[0].Arguments, analyzedWooshRoot);
            Assert(!KnownRepositoryAssetService.HasMissingAssets("SonyResearch/Woosh", wooshAnalysis.LaunchOptions[0].Arguments, analyzedWooshRoot), "Woosh checkpoint repair removes the duplicated checkpoints directory level", lines);
            Assert(!Directory.Exists(nestedCheckpointRoot), "Woosh checkpoint repair removes the empty nested checkpoint directory", lines);
            Assert(wooshAnalysis.RuntimeVersion == ">=3.12" && wooshAnalysis.Category == "Audio generation", "Repository analyzer reads Python and category metadata", lines);
            var managedUvArguments = PythonEnvironmentService.BuildUvSyncArguments(wooshAnalysis.EnvironmentArguments, @"C:\managed-python\python.exe");
            Assert(managedUvArguments.Contains("--python") && managedUvArguments.Contains(@"C:\managed-python\python.exe") && !managedUvArguments.Contains("--active"), "External uv sync pins managed Python without self-hosted environment", lines);

            var analyzedStableAudioRoot = Path.Combine(testRoot, "analyzed-stable-audio");
            Directory.CreateDirectory(analyzedStableAudioRoot);
            await File.WriteAllTextAsync(Path.Combine(analyzedStableAudioRoot, "README.md"), "# Stable Audio 3\n\nAudio generation with a Gradio UI.\n", Encoding.UTF8);
            await File.WriteAllTextAsync(Path.Combine(analyzedStableAudioRoot, "pyproject.toml"), "[project]\nname = \"stable-audio-3\"\ndescription = \"Audio generation\"\nrequires-python = \">=3.10,<3.12\"\ndependencies = [\"torch==2.7.1\"]\n[project.optional-dependencies]\nui = [\"gradio==6.3.0\"]\n[tool.uv]\n", Encoding.UTF8);
            await File.WriteAllTextAsync(Path.Combine(analyzedStableAudioRoot, "uv.lock"), string.Empty, Encoding.UTF8);
            await File.WriteAllTextAsync(Path.Combine(analyzedStableAudioRoot, "run_gradio.py"), "import gradio\n# --model --port\n", Encoding.UTF8);
            var stableAudioAnalysis = GitHubRepositoryAnalyzer.Analyze("Stability-AI/stable-audio-3", analyzedStableAudioRoot, true);
            Assert(stableAudioAnalysis.RuntimeVersion == ">=3.10,<3.12" && ManagedPythonRuntimeService.SelectForConstraint(stableAudioAnalysis.RuntimeVersion) == ManagedPythonRuntimeService.Python311, "Stable Audio selects managed Python 3.11", lines);
            Assert(stableAudioAnalysis.EnvironmentArguments.Contains("ui") && stableAudioAnalysis.LaunchOptions.Length == 3, "Stable Audio 3 analysis installs the Gradio UI extra and offers three model profiles", lines);
            Assert(stableAudioAnalysis.LaunchOptions[0].Arguments == "run_gradio.py --model small-sfx" && stableAudioAnalysis.LaunchOptions[0].IsRecommended, "Stable Audio 3 analysis recommends a complete Small SFX launch command", lines);
            Assert(KnownRepositoryEnvironmentService.NormalizeLaunchArguments("Stability-AI/stable-audio-3", "run_gradio.py") == "run_gradio.py --model small-sfx", "Existing Stable Audio 3 imports receive the missing model argument", lines);
            Assert(KnownRepositoryEnvironmentService.NormalizeLaunchArguments("Stability-AI/stable-audio-3", "run_gradio.py --model small-sfx --port 7861") == "run_gradio.py --model small-sfx", "Stable Audio 3 removes the unsupported port command-line argument", lines);
            Assert(KnownRepositoryEnvironmentService.NormalizeLaunchArguments("Stability-AI/stable-audio-3", "run_gradio.py --model medium") == "run_gradio.py --model medium --model-half", "Existing Stable Audio Medium profile enables half precision", lines);
            var stableAudioProfileArguments = new[] { "small-sfx", "small-music", "medium" }
                .Select(KnownRepositoryEnvironmentService.BuildStableAudioLaunchArguments)
                .ToArray();
            Assert(stableAudioProfileArguments.SequenceEqual(new[]
            {
                "run_gradio.py --model small-sfx",
                "run_gradio.py --model small-music",
                "run_gradio.py --model medium --model-half"
            }) && stableAudioProfileArguments.All(arguments => !arguments.Contains("--port", StringComparison.OrdinalIgnoreCase)), "All Stable Audio launch profiles omit the unsupported port argument", lines);
            var smallSfxAuthorization = KnownRepositoryAuthorizationService.CreateLaunchManifest("Stability-AI/stable-audio-3", "run_gradio.py --model small-sfx", "Stable Audio 3");
            var smallMusicAuthorization = KnownRepositoryAuthorizationService.CreateLaunchManifest("Stability-AI/stable-audio-3", "run_gradio.py --model=small-music", "Stable Audio 3");
            var mediumAuthorization = KnownRepositoryAuthorizationService.CreateLaunchManifest("Stability-AI/stable-audio-3", "run_gradio.py --model medium", "Stable Audio 3");
            Assert(smallSfxAuthorization?.ModelId == "stabilityai/stable-audio-3-small-sfx", "Stable Audio Small SFX launch checks the matching gated repository", lines);
            Assert(smallMusicAuthorization?.ModelId == "stabilityai/stable-audio-3-small-music", "Stable Audio Small Music launch checks the matching gated repository", lines);
            Assert(mediumAuthorization?.ModelId == "stabilityai/stable-audio-3-medium" && mediumAuthorization.AuthorizationProbePath == "model_config.json", "Stable Audio Medium launch checks protected model access", lines);
            Assert(KnownRepositoryAuthorizationService.CreateLaunchManifest("Stability-AI/stable-audio-3", "run_gradio.py --model unknown", "Stable Audio 3") is null, "Unknown Stable Audio model is not assigned unrelated credentials", lines);
            var importedStableDefinition = new LauncherModelDefinition
            {
                Id = "stability-ai-stable-audio-3",
                GitHubRepository = "Stability-AI/stable-audio-3",
                Arguments = "run_gradio.py --model medium"
            };
            Assert(LauncherForm.SupportsStableAudioProfiles(importedStableDefinition), "GitHub-imported Stable Audio exposes all launch profiles", lines);
            Assert(KnownRepositoryAuthorizationService.GetStableAudioModel(importedStableDefinition.GitHubRepository, importedStableDefinition.Arguments) == "medium", "Stable Audio preserves the selected installation profile", lines);
            var credentialStartInfo = new System.Diagnostics.ProcessStartInfo();
            KnownRepositoryAuthorizationService.ApplyCredential(credentialStartInfo, "  self-test-token  ");
            Assert(credentialStartInfo.Environment["HF_TOKEN"] == "self-test-token" && !credentialStartInfo.ArgumentList.Contains("self-test-token"), "Hugging Face credential is injected through the child environment only", lines);
            Assert(KnownRepositoryEnvironmentService.HasMissingEnvironment("Stability-AI/stable-audio-3", "run_gradio.py", analyzedStableAudioRoot), "Stable Audio 3 missing Gradio dependency is detected", lines);
            var gradioPackage = Path.Combine(analyzedStableAudioRoot, ".venv", "Lib", "site-packages", "gradio", "__init__.py");
            Directory.CreateDirectory(Path.GetDirectoryName(gradioPackage)!);
            await File.WriteAllTextAsync(gradioPackage, string.Empty, Encoding.UTF8);
            var stablePyvenv = Path.Combine(analyzedStableAudioRoot, ".venv", "pyvenv.cfg");
            await File.WriteAllTextAsync(stablePyvenv, "version = 3.12.10\n", Encoding.UTF8);
            Assert(KnownRepositoryEnvironmentService.HasMissingEnvironment("Stability-AI/stable-audio-3", "run_gradio.py", analyzedStableAudioRoot), "Stable Audio incompatible Python environment is detected", lines);
            await File.WriteAllTextAsync(stablePyvenv, "version_info = 3.11.9\n", Encoding.UTF8);
            Assert(!KnownRepositoryEnvironmentService.HasMissingEnvironment("Stability-AI/stable-audio-3", "run_gradio.py", analyzedStableAudioRoot), "Stable Audio 3 installed Gradio dependency is accepted", lines);
            Assert(PluginDependencyChecker.Check(["python>=3.10,<3.12"], analyzedStableAudioRoot).Single().IsSatisfied, "Python dependency check validates compound environment constraint", lines);

            var analyzedIndexTtsRoot = Path.Combine(testRoot, "analyzed-index-tts");
            Directory.CreateDirectory(analyzedIndexTtsRoot);
            await File.WriteAllTextAsync(Path.Combine(analyzedIndexTtsRoot, "README.md"), "# IndexTTS2\n\nEmotionally expressive text-to-speech with a WebUI.\n", Encoding.UTF8);
            await File.WriteAllTextAsync(Path.Combine(analyzedIndexTtsRoot, "pyproject.toml"), "[project]\nname = \"indextts\"\ndescription = \"Text-to-speech\"\nrequires-python = \">=3.10,<3.12\"\ndependencies = [\"torch==2.8.*\"]\n[project.optional-dependencies]\nwebui = [\"gradio==5.45.0\"]\n[tool.uv]\n", Encoding.UTF8);
            await File.WriteAllTextAsync(Path.Combine(analyzedIndexTtsRoot, "uv.lock"), string.Empty, Encoding.UTF8);
            await File.WriteAllTextAsync(Path.Combine(analyzedIndexTtsRoot, "webui.py"), "import argparse\nimport gradio as gr\nparser = argparse.ArgumentParser()\nparser.add_argument(\"--host\")\nparser.add_argument(\"--port\")\n", Encoding.UTF8);
            var indexTtsAnalysis = GitHubRepositoryAnalyzer.Analyze("index-tts/index-tts", analyzedIndexTtsRoot, true);
            Assert(indexTtsAnalysis.EnvironmentArguments.Contains("webui"), "IndexTTS analysis installs the WebUI optional dependency", lines);
            Assert(indexTtsAnalysis.LaunchOptions.Length == 1 && indexTtsAnalysis.LaunchOptions[0].Arguments == "webui.py --host 127.0.0.1 --port {port}", "IndexTTS analysis configures its explicit WebUI host and port", lines);
            Assert(KnownRepositoryEnvironmentService.NormalizeLaunchArguments("index-tts/index-tts", "webui.py") == "webui.py --host 127.0.0.1 --port {port}", "Existing IndexTTS imports receive host and port arguments", lines);
            Assert(KnownRepositoryEnvironmentService.NormalizeLaunchArguments("index-tts/index-tts", "webui.py --host=0.0.0.0 --port 7860") == "webui.py --host 127.0.0.1 --port {port}", "Existing IndexTTS host and port arguments are migrated", lines);
            Assert(KnownRepositoryEnvironmentService.HasMissingEnvironment("index-tts/index-tts", "webui.py", analyzedIndexTtsRoot), "IndexTTS missing Gradio dependency is detected", lines);
            var indexGradioPackage = Path.Combine(analyzedIndexTtsRoot, ".venv", "Lib", "site-packages", "gradio", "__init__.py");
            Directory.CreateDirectory(Path.GetDirectoryName(indexGradioPackage)!);
            await File.WriteAllTextAsync(indexGradioPackage, string.Empty, Encoding.UTF8);
            await File.WriteAllTextAsync(Path.Combine(analyzedIndexTtsRoot, ".venv", "pyvenv.cfg"), "version_info = 3.11.9\n", Encoding.UTF8);
            Assert(!KnownRepositoryEnvironmentService.HasMissingEnvironment("index-tts/index-tts", "webui.py", analyzedIndexTtsRoot), "IndexTTS installed Gradio dependency is accepted", lines);

            var importedUpdateRoot = Path.Combine(testRoot, "imported-update-baseline");
            Directory.CreateDirectory(importedUpdateRoot);
            const string importedBaselineSha = "1234567890abcdef1234567890abcdef12345678";
            await File.WriteAllTextAsync(Path.Combine(importedUpdateRoot, ".bachen-github-source.json"), JsonSerializer.Serialize(new { repository = "example/model", branch = "main", commitSha = importedBaselineSha }), Encoding.UTF8);
            var importedUpdateSource = new GitHubUpdateSource("Imported model", "example/model", "main", importedUpdateRoot, [], []);
            var importedUpdateState = new GitHubUpdateService(new HttpClient()).LoadState(importedUpdateSource);
            Assert(importedUpdateState?.CommitSha == importedBaselineSha, "GitHub update checks use imported source metadata as the initial baseline", lines);

            var analyzedGenericRoot = Path.Combine(testRoot, "analyzed-generic");
            Directory.CreateDirectory(analyzedGenericRoot);
            await File.WriteAllTextAsync(Path.Combine(analyzedGenericRoot, "requirements.txt"), "gradio\n", Encoding.UTF8);
            await File.WriteAllTextAsync(Path.Combine(analyzedGenericRoot, "app.py"), "print('app')\n", Encoding.UTF8);
            var genericAnalysis = GitHubRepositoryAnalyzer.Analyze("example/generic", analyzedGenericRoot, false);
            Assert(genericAnalysis.LaunchOptions.Single().EntryScript == "app.py" && genericAnalysis.EnvironmentManager == "pip", "Generic Python repository automatic configuration", lines);
            var noEntryRoot = Path.Combine(testRoot, "analyzed-no-entry");
            Directory.CreateDirectory(noEntryRoot);
            await File.WriteAllTextAsync(Path.Combine(noEntryRoot, "worker.py"), "print('worker')\n", Encoding.UTF8);
            AssertThrows(() => GitHubRepositoryAnalyzer.Analyze("example/no-entry", noEntryRoot, false), "Unknown repository entry is not guessed", lines);

            var downloadRoot = Path.Combine(testRoot, "download-resume");
            var downloadDirectory = Path.Combine(downloadRoot, "downloads");
            Directory.CreateDirectory(downloadDirectory);
            var packageBytes = await File.ReadAllBytesAsync(packagePath);
            var resumedManifest = CloneManifest(manifest);
            resumedManifest.Id = "resumed-plugin";
            resumedManifest.PackageMirrors = ["https://mirror.example.com/resumed-plugin.zip"];
            var partialPath = Path.Combine(downloadDirectory, "resumed-plugin-1.0.0.zip.partial");
            await File.WriteAllBytesAsync(partialPath, packageBytes[..(packageBytes.Length / 2)]);
            using (var downloadClient = new HttpClient(new RetryingRangeHandler(packageBytes, failuresBeforeSuccess: 2)))
            {
                var downloadService = new PluginDownloadService(downloadClient);
                var downloadedPath = await downloadService.DownloadAsync(resumedManifest, downloadRoot);
                Assert((await File.ReadAllBytesAsync(downloadedPath)).SequenceEqual(packageBytes), "Resumable plugin download with retry", lines);
                Assert(!File.Exists(partialPath), "Completed download finalization", lines);
            }

            var preflightPass = PluginInstallPreflightService.Assess(manifest, dataRoot, () => (0, 8192));
            Assert(preflightPass.CanInstall, "Plugin disk and GPU preflight", lines);
            var lowDiskManifest = CloneManifest(manifest);
            lowDiskManifest.MinimumFreeDiskBytes = long.MaxValue;
            Assert(!PluginInstallPreflightService.Assess(lowDiskManifest, dataRoot, () => null).CanInstall, "Insufficient disk blocking", lines);

            using (var authorizedClient = new HttpClient(new AuthorizationHandler(System.Net.HttpStatusCode.PartialContent)))
            {
                var authorization = new ExternalModelAuthorizationService(authorizedClient);
                var gatedManifest = CloneManifest(manifest);
                gatedManifest.RequiresExternalAuthorization = true;
                gatedManifest.ModelProvider = "huggingface";
                gatedManifest.ModelId = "example/gated-model";
                gatedManifest.AuthorizationUrl = "https://huggingface.co/example/gated-model";
                gatedManifest.AuthorizationProbePath = "config.json";
                Assert((await authorization.VerifyAsync(gatedManifest, "test-token")).IsAuthorized, "Hugging Face access verification", lines);
                using var deniedClient = new HttpClient(new AuthorizationHandler(System.Net.HttpStatusCode.Forbidden));
                var denied = await new ExternalModelAuthorizationService(deniedClient).VerifyAsync(gatedManifest, "test-token");
                Assert(denied.Status == ExternalAuthorizationStatus.AccessNotGranted, "Gated model authorization classification", lines);
            }

            var cleanManifest = CloneManifest(manifest);
            cleanManifest.Id = "clean-python-plugin";
            cleanManifest.Executable = ".venv/Scripts/python.exe";
            cleanManifest.Arguments = "--version";
            cleanManifest.CreateVirtualEnvironment = true;
            cleanManifest.VirtualEnvironmentPath = ".venv";
            cleanManifest.RequirementsFile = string.Empty;
            cleanManifest.Dependencies = ["python>=3.10", "file:model.dat"];
            ResignManifest(cleanManifest, rsa);
            var cleanDataRoot = Path.Combine(testRoot, "clean-install-data");
            var cleanInstall = await packageService.InstallAsync(cleanManifest, packagePath, cleanDataRoot);
            Assert(File.Exists(Path.Combine(cleanInstall.Definition.RootDirectory, ".venv", "Scripts", "python.exe")), "Automatic Python virtual environment creation", lines);
            Assert(PluginDependencyChecker.Check(cleanInstall.Definition.Dependencies, cleanInstall.Definition.RootDirectory).All(result => result.IsSatisfied), "Clean directory plugin environment self-check", lines);

            var preservedDownload = Path.Combine(dataRoot, "downloads", "preserved-package.zip");
            Directory.CreateDirectory(Path.GetDirectoryName(preservedDownload)!);
            File.Copy(packagePath, preservedDownload, true);
            var failingManifest = CloneManifest(manifest);
            failingManifest.Id = "failing-install";
            failingManifest.RequiredFiles = ["missing.file"];
            ResignManifest(failingManifest, rsa);
            await AssertThrowsAsync<InvalidDataException>(() => packageService.InstallAsync(failingManifest, preservedDownload, dataRoot), "Failed plugin installation detection", lines);
            Assert(File.Exists(preservedDownload), "Completed download preserved after install failure", lines);
            Assert(!Directory.EnumerateDirectories(Path.Combine(dataRoot, "downloads"), "install-failing-install-*").Any(), "Failed installation staging cleanup", lines);

            var credentialTarget = "BaChenAILauncher/SelfTest/" + Guid.NewGuid().ToString("N");
            try
            {
                WindowsCredentialStore.Save(credentialTarget, "SelfTest", "temporary-secret");
                Assert(WindowsCredentialStore.Read(credentialTarget) == "temporary-secret", "Windows Credential Manager token storage", lines);
            }
            finally
            {
                WindowsCredentialStore.Delete(credentialTarget);
            }
            Assert(WindowsCredentialStore.Read(credentialTarget) is null, "Windows Credential Manager token removal", lines);

            var assessment = ResourceScheduler.Assess(
                install.Definition.ToServiceProfileForSelfTest(),
                [12001],
                [12001, 12002]);
            Assert(assessment.BlocksLaunch && assessment.ManagedProcessIds.Contains(12001) && assessment.UnknownPortProcessIds.Contains(12002), "Resource and port conflict scheduling", lines);
            var noGpuDependencies = PluginDependencyChecker.Check(["cuda"], install.Definition.RootDirectory, () => false);
            Assert(noGpuDependencies is [{ IsSatisfied: false, IsEnforced: true }], "No-GPU dependency detection", lines);

            var currentLauncherPath = Path.Combine(testRoot, "BaChen AI Launcher.exe");
            var stagedLauncherPath = Path.Combine(testRoot, "BaChen AI Launcher staged.exe");
            var launcherBackupPath = Path.Combine(testRoot, "update-backup", "BaChen AI Launcher.previous.exe");
            await File.WriteAllTextAsync(currentLauncherPath, "previous-version", Encoding.ASCII);
            await File.WriteAllTextAsync(stagedLauncherPath, "next-version", Encoding.ASCII);
            var stagedHash = Convert.ToHexString(SHA256.HashData(Encoding.ASCII.GetBytes("next-version")));
            await AssertThrowsAsync(
                () => LauncherSelfUpdateService.ApplyUpdateFilesAsync(
                    currentLauncherPath,
                    stagedLauncherPath,
                    stagedHash,
                    launcherBackupPath,
                    (source, destination, overwrite) =>
                    {
                        File.Delete(destination);
                        throw new IOException("simulated replacement interruption");
                    }),
                "Interrupted replacement failure detection",
                lines);
            Assert(File.ReadAllText(currentLauncherPath, Encoding.ASCII) == "previous-version", "Interrupted replacement automatic restore", lines);
            Assert(!File.Exists(currentLauncherPath + ".new"), "Interrupted replacement cleanup", lines);
            await LauncherSelfUpdateService.ApplyUpdateFilesAsync(currentLauncherPath, stagedLauncherPath, stagedHash, launcherBackupPath);
            Assert(File.ReadAllText(currentLauncherPath, Encoding.ASCII) == "next-version", "Atomic launcher replacement", lines);
            Assert(File.ReadAllText(launcherBackupPath, Encoding.ASCII) == "previous-version", "Launcher rollback backup preservation", lines);

            var moveAttempts = 0;
            var retrySource = Path.Combine(testRoot, "retry-uninstall-source");
            var retryDestination = Path.Combine(testRoot, "retry-uninstall-destination");
            Directory.CreateDirectory(retrySource);
            await PluginPackageService.MoveDirectoryWithRetryAsync(
                retrySource,
                retryDestination,
                (source, destination) =>
                {
                    moveAttempts++;
                    if (moveAttempts < 3) throw new IOException("simulated transient file lock");
                    Directory.Move(source, destination);
                },
                retryDelayMilliseconds: 0);
            Assert(moveAttempts == 3 && Directory.Exists(retryDestination), "Plugin uninstall retries transient directory locks", lines);

            var processRoot = Path.Combine(testRoot, "process-owned-plugin");
            Directory.CreateDirectory(processRoot);
            var holdScript = Path.Combine(processRoot, "hold-open.cmd");
            await File.WriteAllTextAsync(holdScript, "@ping 127.0.0.1 -n 30 > nul\r\n", Encoding.ASCII);
            using (var heldProcess = Process.Start(new ProcessStartInfo("cmd.exe")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                ArgumentList = { "/c", holdScript }
            }) ?? throw new InvalidOperationException("Unable to start process cleanup fixture."))
            {
                var foundPluginProcesses = await FindPluginProcessesWithRetryAsync(processRoot, heldProcess.Id);
                Assert(foundPluginProcesses.Contains(heldProcess.Id), "Plugin uninstall discovers processes using the plugin directory", lines);
                var stopResult = PluginProcessService.Stop(foundPluginProcesses);
                Assert(stopResult.Failures.Count == 0 && heldProcess.WaitForExit(5000), "Plugin uninstall stops remaining process trees", lines);
            }

            var uninstall = await packageService.UninstallAsync(install.Definition, dataRoot);
            Assert(uninstall.FilesMoved && uninstall.BackupPath is not null && Directory.Exists(uninstall.BackupPath), "Recoverable plugin uninstall", lines);

            var settingsPath = Path.Combine(testRoot, "config", "launcher.settings.json");
            var expectedSettings = new LauncherSettings { DataRoot = dataRoot, WooshPort = 18001, StablePort = 18002, IndexTtsPort = 18003 };
            LauncherConfigurationStore.SaveAtomic(settingsPath, expectedSettings);
            var loadedSettings = LauncherConfigurationStore.LoadOrCreate(settingsPath, () => new LauncherSettings());
            Assert(loadedSettings.WooshPort == 18001 && File.Exists(settingsPath), "Atomic configuration save and load", lines);
            Assert(loadedSettings.LauncherUpdateChannel == LauncherUpdateChannel.Stable, "Stable update channel default", lines);
            loadedSettings.LauncherUpdateChannel = LauncherUpdateChannel.Preview;
            LauncherConfigurationStore.SaveAtomic(settingsPath, loadedSettings);
            var previewSettings = LauncherConfigurationStore.LoadOrCreate(settingsPath, () => new LauncherSettings());
            Assert(previewSettings.LauncherUpdateChannel == LauncherUpdateChannel.Preview, "Preview update channel persistence", lines);
            previewSettings.HighestObservedStableVersion = "99.0.0";
            previewSettings.HighestObservedStableVersionAt = DateTimeOffset.Parse("2026-07-29T00:00:00Z");
            previewSettings.HighestObservedStableVersionSource = "GitHub Raw";
            LauncherConfigurationStore.SaveAtomic(settingsPath, previewSettings);
            previewSettings = LauncherConfigurationStore.LoadOrCreate(settingsPath, () => new LauncherSettings());
            Assert(previewSettings.HighestObservedStableVersion == "99.0.0" && previewSettings.HighestObservedStableVersionSource == "GitHub Raw", "Highest observed stable version persistence", lines);
            Assert(previewSettings.FirstRunCompleted, "Existing-user first-run migration default", lines);
            previewSettings.FirstRunCompleted = false;
            previewSettings.FirstRunWizardStep = 3;
            previewSettings.FirstRunSelectedPluginIds = ["woosh-dflow"];
            LauncherConfigurationStore.SaveAtomic(settingsPath, previewSettings);
            var firstRunSettings = LauncherConfigurationStore.LoadOrCreate(settingsPath, () => new LauncherSettings());
            Assert(!firstRunSettings.FirstRunCompleted && firstRunSettings.FirstRunWizardStep == 3 && firstRunSettings.FirstRunSelectedPluginIds.SequenceEqual(["woosh-dflow"]), "First-run wizard state persistence", lines);
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

    private static void AssertServiceControlLayout(int width, List<string> lines)
    {
        const int stopWidth = 190;
        const int refreshWidth = 160;
        const int openWidth = 200;
        const int checkWidth = 164;
        const int updateWidth = 176;
        var layout = ServiceControlLayoutPlanner.Calculate(width, stopWidth, refreshWidth, openWidth, checkWidth, updateWidth);
        var buttons = new[]
        {
            (layout.StopButton, stopWidth),
            (layout.RefreshButton, refreshWidth),
            (layout.OpenButton, openWidth),
            (layout.CheckUpdatesButton, checkWidth),
            (layout.UpdateSourceButton, updateWidth)
        };
        Assert(buttons.All(button => button.Item1.X >= 0 && button.Item1.X + button.Item2 <= width), $"Service controls remain visible at {width}px", lines);
    }

    private static void AssertPluginUpdateActionLayout(List<string> lines)
    {
        var threeButtonWidths = LauncherForm.CalculateEqualActionWidths(500, 3, 10);
        var fourButtonWidths = LauncherForm.CalculateEqualActionWidths(500, 4, 10);
        Assert(threeButtonWidths.SequenceEqual([160, 160, 160]), "Three plugin actions divide the available row equally", lines);
        Assert(fourButtonWidths.SequenceEqual([118, 118, 117, 117]), "Four plugin actions divide the available row with stable remainder handling", lines);
        Assert(threeButtonWidths.Sum() + 20 == 500 && fourButtonWidths.Sum() + 30 == 500, "Plugin action rows consume the full available width", lines);
        Assert(LauncherForm.CalculateEqualActionWidths(280, 4, 10).All(width => width >= 62), "Plugin action buttons retain usable minimum-window widths", lines);
        var localizedEntry = new LauncherLogEntry(DateTime.Now, "中文日志", "English log", null, false);
        Assert(localizedEntry.DisplayMessage(false) == "中文日志" && localizedEntry.DisplayMessage(true) == "English log", "Runtime logs re-render in the selected language", lines);
        using var progressFont = new Font("Microsoft YaHei UI", 9.5F, FontStyle.Bold);
        var fullStatusWidth = LauncherForm.CalculateLauncherUpdateProgressWidth("正在下载并校验启动器（下载 50% · 12.5 MB/s）", progressFont, 330);
        Assert(fullStatusWidth > 180 && fullStatusWidth <= 330, "Launcher update progress indicator follows full status text width", lines);
        using var reflectionBitmap = new Bitmap(180, 42);
        using (var reflectionGraphics = Graphics.FromImage(reflectionBitmap))
        using (var reflectionPath = new GraphicsPath())
        {
            reflectionGraphics.Clear(Theme.DeepTeal);
            reflectionPath.AddRectangle(new Rectangle(0, 0, reflectionBitmap.Width, reflectionBitmap.Height));
            GlassPaint.DrawReflection(reflectionGraphics, reflectionPath, new Rectangle(0, 0, reflectionBitmap.Width, reflectionBitmap.Height), 72, 0.5F, 90);
        }
        Assert(reflectionBitmap.GetPixel(20, 4).ToArgb() != reflectionBitmap.GetPixel(20, 36).ToArgb(), "Glass reflection layer renders a visible top highlight", lines);
    }

    private static ServiceProfile ToServiceProfileForSelfTest(this LauncherModelDefinition definition)
        => new(definition.DisplayName, definition.Description, definition.RootDirectory, Path.Combine(definition.RootDirectory, definition.Executable), definition.Arguments, definition.Port, definition.IsHighVram, definition.RequiredFiles, 0, 0, definition.Dependencies);

    private static LauncherModelDefinition DefinitionFromManifestForSelfTest(PluginPackageManifest manifest, string root)
        => new()
        {
            Id = manifest.Id,
            DisplayName = manifest.DisplayName,
            RootDirectory = root,
            Executable = manifest.Executable,
            Arguments = manifest.Arguments,
            Runtime = manifest.Runtime,
            RuntimeVersion = manifest.RuntimeVersion,
            Port = manifest.Port,
            RequiredFiles = manifest.RequiredFiles,
            Dependencies = manifest.Dependencies,
            PackageSha256 = manifest.PackageSha256,
            PackageSizeBytes = manifest.PackageSizeBytes,
            PreservedPaths = manifest.PreservedPaths,
            TrustSource = "SignedCatalog",
            SigningKeyId = "bachen-plugin-index-2026"
        };

    private static PluginPackageManifest CloneManifest(PluginPackageManifest manifest)
        => System.Text.Json.JsonSerializer.Deserialize<PluginPackageManifest>(
            System.Text.Json.JsonSerializer.Serialize(manifest))!;

    private static void ResignManifest(PluginPackageManifest manifest, RSA rsa)
    {
        var payload = Encoding.UTF8.GetBytes(PluginManifestSignatureVerifier.CreateCanonicalPayload(manifest));
        manifest.Signature.Value = Convert.ToBase64String(rsa.SignData(payload, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
    }

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

    private static async Task AssertThrowsAsync(Func<Task> action, string name, ICollection<string> lines)
    {
        try
        {
            await action();
        }
        catch
        {
            lines.Add("PASS: " + name);
            return;
        }
        throw new InvalidOperationException($"Self-test assertion failed: {name}");
    }

    private static async Task<TException> AssertThrowsAsync<TException>(Func<Task> action, string name, ICollection<string> lines)
        where TException : Exception
    {
        try
        {
            await action();
        }
        catch (TException ex)
        {
            lines.Add("PASS: " + name);
            return ex;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Self-test assertion failed: {name}; expected {typeof(TException).Name}, received {ex.GetType().Name}.", ex);
        }
        throw new InvalidOperationException($"Self-test assertion failed: {name}");
    }

    private static async Task<List<int>> FindPluginProcessesWithRetryAsync(string pluginRoot, int expectedProcessId)
    {
        var processes = new List<int>();
        for (var attempt = 1; attempt <= 12; attempt++)
        {
            processes = PluginProcessService.FindProcessesByPluginRoots([pluginRoot]);
            if (processes.Contains(expectedProcessId))
            {
                return processes;
            }
            if (attempt < 12)
            {
                await Task.Delay(250);
            }
        }
        return processes;
    }

    private static async Task WriteReportAsync(string reportPath, IEnumerable<string> lines)
    {
        var fullPath = Path.GetFullPath(reportPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        await File.WriteAllLinesAsync(fullPath, lines, Encoding.UTF8);
    }

    private static async Task RunCheckedProcessAsync(string executable, IEnumerable<string> arguments, string workingDirectory)
    {
        var startInfo = new ProcessStartInfo(executable)
        {
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }
        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException($"Could not start {executable}.");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"Process failed ({process.ExitCode}): {executable}\n{await error}\n{await output}".Trim());
        }
    }

    private sealed class StaticJsonHandler(string json) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            });
    }

    private sealed class StaticBytesHandler(byte[] bytes) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(bytes)
            });
    }

    private sealed class GitHubImportHandler(string commitSha, byte[] archiveBytes) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri?.AbsolutePath.Equals("/repos/example/model-repo", StringComparison.OrdinalIgnoreCase) == true)
            {
                return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    RequestMessage = request,
                    Content = new StringContent("{\"default_branch\":\"main\"}", Encoding.UTF8, "application/json")
                });
            }
            if (request.RequestUri?.AbsolutePath.Contains("/commits/", StringComparison.OrdinalIgnoreCase) == true)
            {
                return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    RequestMessage = request,
                    Content = new StringContent($"{{\"sha\":\"{commitSha}\"}}", Encoding.UTF8, "application/json")
                });
            }
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                RequestMessage = request,
                Content = new ByteArrayContent(archiveBytes)
            });
        }
    }

    private sealed class RateLimitedGitHubImportHandler(string commitSha, byte[] archiveBytes) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri?.Host.Equals("api.github.com", StringComparison.OrdinalIgnoreCase) == true)
            {
                return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.Forbidden)
                {
                    RequestMessage = request,
                    Content = new StringContent("{\"message\":\"API rate limit exceeded\"}", Encoding.UTF8, "application/json")
                });
            }
            if (request.RequestUri?.AbsolutePath.EndsWith("/commits.atom", StringComparison.OrdinalIgnoreCase) == true)
            {
                var feed = $"<?xml version=\"1.0\" encoding=\"UTF-8\"?><feed xmlns=\"http://www.w3.org/2005/Atom\"><link rel=\"self\" href=\"https://github.com/example/rate-limited/commits/main.atom\"/><entry><id>tag:github.com,2008:Grit::Commit/{commitSha}</id></entry></feed>";
                return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    RequestMessage = request,
                    Content = new StringContent(feed, Encoding.UTF8, "application/atom+xml")
                });
            }
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                RequestMessage = request,
                Content = new ByteArrayContent(archiveBytes)
            });
        }
    }

    private sealed class StatusCodeHandler(System.Net.HttpStatusCode statusCode) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(statusCode));
    }

    private sealed class MultiSourceUpdateHandler(IReadOnlyDictionary<string, string> responses) : HttpMessageHandler
    {
        private readonly List<bool> cacheChecks = [];

        public bool AllRequestsBypassCache => cacheChecks.Count > 0 && cacheChecks.All(value => value);
        public List<string> RequestedPaths { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestedPaths.Add(request.RequestUri?.AbsolutePath ?? string.Empty);
            cacheChecks.Add(
                request.RequestUri?.Query.Contains("bachen_request=", StringComparison.Ordinal) == true &&
                request.Headers.CacheControl?.NoCache == true &&
                request.Headers.CacheControl?.NoStore == true);
            if (request.RequestUri is not null && responses.TryGetValue(request.RequestUri.AbsolutePath, out var json))
            {
                return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    RequestMessage = request,
                    Content = new StringContent(json, Encoding.UTF8, "application/json")
                });
            }
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.ServiceUnavailable)
            {
                RequestMessage = request
            });
        }
    }

    private sealed class PreviewRateLimitFallbackHandler(string stableManifest) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri?.Host.Equals("api.github.com", StringComparison.OrdinalIgnoreCase) == true)
            {
                return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.Forbidden)
                {
                    RequestMessage = request,
                    Content = new StringContent("{\"message\":\"API rate limit exceeded\"}", Encoding.UTF8, "application/json")
                });
            }
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                RequestMessage = request,
                Content = new StringContent(stableManifest, Encoding.UTF8, "application/json")
            });
        }
    }

    private sealed class StableMissingPreviewHandler : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            return Task.FromResult(request.RequestUri == LauncherSelfUpdateService.DefaultManifestUri
                ? new HttpResponseMessage(System.Net.HttpStatusCode.NotFound)
                : new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent("[]", Encoding.UTF8, "application/json")
                });
        }
    }

    private sealed class FailureHandler(Exception exception) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromException<HttpResponseMessage>(exception);
    }

    private sealed class CallbackProgress<T>(Action<T> callback) : IProgress<T>
    {
        public void Report(T value) => callback(value);
    }

    private sealed class RetryingRangeHandler(byte[] content, int failuresBeforeSuccess) : HttpMessageHandler
    {
        private int _attempts;
        public int RangeRequests { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            _attempts++;
            if (_attempts <= failuresBeforeSuccess)
            {
                throw new HttpRequestException("simulated transient download failure");
            }
            var start = request.Headers.Range?.Ranges.FirstOrDefault()?.From ?? 0;
            if (start > 0)
            {
                RangeRequests++;
            }
            if (start >= content.Length)
            {
                return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.RequestedRangeNotSatisfiable));
            }
            var bytes = content[(int)start..];
            var response = new HttpResponseMessage(start > 0 ? System.Net.HttpStatusCode.PartialContent : System.Net.HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(bytes)
            };
            if (start > 0)
            {
                response.Content.Headers.ContentRange = new System.Net.Http.Headers.ContentRangeHeaderValue(start, content.Length - 1, content.Length);
            }
            return Task.FromResult(response);
        }
    }

    private sealed class AuthorizationHandler(System.Net.HttpStatusCode probeStatus) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(request.RequestUri?.AbsolutePath.Contains("whoami", StringComparison.OrdinalIgnoreCase) == true
                ? new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent("{}") }
                : new HttpResponseMessage(probeStatus) { Content = new ByteArrayContent([0]) });
    }
}
