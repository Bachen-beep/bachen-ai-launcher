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

            var fallbackHandler = new StableMissingPreviewHandler();
            using (var noStableClient = new HttpClient(fallbackHandler))
            {
                var noStableService = new LauncherSelfUpdateService(noStableClient);
                var unavailable = await AssertThrowsAsync<LauncherUpdateUnavailableException>(
                    () => noStableService.CheckAsync(LauncherUpdateChannel.Stable),
                    "Stable channel preview fallback",
                    lines);
                Assert(unavailable.Channel == LauncherUpdateChannel.Preview && fallbackHandler.RequestCount == 2, "Missing stable release falls back to preview API", lines);
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
                var serverFailureService = new LauncherSelfUpdateService(serverFailureClient);
                await AssertThrowsAsync<HttpRequestException>(
                    () => serverFailureService.CheckAsync(LauncherUpdateChannel.Stable),
                    "GitHub server failure remains a network error",
                    lines);
            }
            using (var previewRateLimitedClient = new HttpClient(new StatusCodeHandler(System.Net.HttpStatusCode.Forbidden)))
            {
                var previewRateLimitedService = new LauncherSelfUpdateService(previewRateLimitedClient);
                Assert(await previewRateLimitedService.ResolveManifestUriWithFallbackAsync(LauncherUpdateChannel.Preview) == LauncherSelfUpdateService.DefaultManifestUri, "Preview API rate limit falls back to stable manifest", lines);
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
                var versionSixInstall = await new PluginPackageService(assetClient).InstallAsync(versionSixManifest, packagePath, versionSixDataRoot);
                Assert(File.Exists(Path.Combine(versionSixInstall.Definition.RootDirectory, "models", "tiny-model", "model.bin")), "Manifest v6 verified asset extraction", lines);
                var preflight = PluginInstallPreflightService.Assess(versionSixManifest, versionSixDataRoot, () => (0, 8192));
                Assert(preflight.RequiredDiskBytes >= (versionSixManifest.PackageSizeBytes + assetBytes.Length) * 2, "Asset packages included in disk preflight", lines);
            }
            Assert(ManagedPythonRuntimeService.Python312.Id == "python-3.12.10-x64" &&
                ManagedPythonRuntimeService.Python312.SizeBytes == 26964224 &&
                ManagedPythonRuntimeService.Python312.Sha256 == "67B5635E80EA51072B87941312D00EC8927C4DB9BA18938F7AD2D27B328B95FB",
                "Managed Python runtime is pinned by version size and SHA-256", lines);

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
            await File.WriteAllTextAsync(Path.Combine(analyzedStableAudioRoot, "pyproject.toml"), "[project]\nname = \"stable-audio-3\"\ndescription = \"Audio generation\"\nrequires-python = \">=3.10\"\ndependencies = [\"torch==2.7.1\"]\n[project.optional-dependencies]\nui = [\"gradio==6.3.0\"]\n[tool.uv]\n", Encoding.UTF8);
            await File.WriteAllTextAsync(Path.Combine(analyzedStableAudioRoot, "uv.lock"), string.Empty, Encoding.UTF8);
            await File.WriteAllTextAsync(Path.Combine(analyzedStableAudioRoot, "run_gradio.py"), "import gradio\n# --model --port\n", Encoding.UTF8);
            var stableAudioAnalysis = GitHubRepositoryAnalyzer.Analyze("Stability-AI/stable-audio-3", analyzedStableAudioRoot, true);
            Assert(stableAudioAnalysis.EnvironmentArguments.Contains("ui") && stableAudioAnalysis.LaunchOptions.Length == 3, "Stable Audio 3 analysis installs the Gradio UI extra and offers three model profiles", lines);
            Assert(stableAudioAnalysis.LaunchOptions[0].Arguments == "run_gradio.py --model small-sfx --port {port}" && stableAudioAnalysis.LaunchOptions[0].IsRecommended, "Stable Audio 3 analysis recommends a complete Small SFX launch command", lines);
            Assert(KnownRepositoryEnvironmentService.NormalizeLaunchArguments("Stability-AI/stable-audio-3", "run_gradio.py") == "run_gradio.py --model small-sfx --port {port}", "Existing Stable Audio 3 imports receive the missing model and port arguments", lines);
            Assert(KnownRepositoryEnvironmentService.HasMissingEnvironment("Stability-AI/stable-audio-3", "run_gradio.py", analyzedStableAudioRoot), "Stable Audio 3 missing Gradio dependency is detected", lines);
            var gradioPackage = Path.Combine(analyzedStableAudioRoot, ".venv", "Lib", "site-packages", "gradio", "__init__.py");
            Directory.CreateDirectory(Path.GetDirectoryName(gradioPackage)!);
            await File.WriteAllTextAsync(gradioPackage, string.Empty, Encoding.UTF8);
            Assert(!KnownRepositoryEnvironmentService.HasMissingEnvironment("Stability-AI/stable-audio-3", "run_gradio.py", analyzedStableAudioRoot), "Stable Audio 3 installed Gradio dependency is accepted", lines);

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

            var uninstall = packageService.Uninstall(install.Definition, dataRoot);
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

    private static async Task WriteReportAsync(string reportPath, IEnumerable<string> lines)
    {
        var fullPath = Path.GetFullPath(reportPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        await File.WriteAllLinesAsync(fullPath, lines, Encoding.UTF8);
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

    private sealed class StatusCodeHandler(System.Net.HttpStatusCode statusCode) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(statusCode));
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

    private sealed class RetryingRangeHandler(byte[] content, int failuresBeforeSuccess) : HttpMessageHandler
    {
        private int _attempts;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            _attempts++;
            if (_attempts <= failuresBeforeSuccess)
            {
                throw new HttpRequestException("simulated transient download failure");
            }
            var start = request.Headers.Range?.Ranges.FirstOrDefault()?.From ?? 0;
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
