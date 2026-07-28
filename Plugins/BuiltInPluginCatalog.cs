namespace BaChenAiLauncher;

internal static class BuiltInPluginCatalog
{
    public static IReadOnlyList<PluginPackageManifest> CreateManifests(LauncherSettings settings)
        =>
        [
            new PluginPackageManifest
            {
                Id = "woosh-dflow",
                DisplayName = "Woosh-DFlow",
                Version = "0.0.0-local",
                Publisher = "SonyResearch",
                Category = "Sound design",
                Description = "Text to sound effects and ambience",
                Executable = ".venv/Scripts/python.exe",
                Arguments = "gradio_Woosh-DFlow.py --server-name 127.0.0.1 --server-port {port}",
                Runtime = "python",
                RuntimeVersion = ">=3.10",
                Port = settings.WooshPort,
                RecommendedVramMiB = 6800,
                RecommendedSystemMemoryMiB = 16384,
                RequiredFiles = ["gradio_Woosh-DFlow.py", "checkpoints"],
                Dependencies = ["python>=3.10", "cuda"],
                GitHubRepository = "SonyResearch/Woosh",
                PreservedPaths = [".venv", ".runtime", ".uv-cache", "checkpoints", "generated_audio", "outputs", "logs", "prompts", "archive", "launcher-update-backups", "gradio_Woosh-DFlow.py", "Start-Woosh-DFlow.cmd", "woosh-model-downloads.txt", "woosh-source.zip"]
            },
            new PluginPackageManifest
            {
                Id = "stable-audio-3",
                DisplayName = "Stable Audio 3",
                Version = "0.0.0-local",
                Publisher = "Stability AI",
                Category = "Audio generation",
                Description = "Sound effects, music, and medium generation",
                Executable = ".venv/Scripts/python.exe",
                Arguments = "run_gradio.py --model {model} --port {port}",
                Runtime = "python",
                RuntimeVersion = ">=3.10",
                Port = settings.StablePort,
                RecommendedVramMiB = 2200,
                RecommendedSystemMemoryMiB = 8192,
                RequiredFiles = ["run_gradio.py", "stable_audio_3"],
                Dependencies = ["python>=3.10", "cuda"],
                GitHubRepository = "Stability-AI/stable-audio-3",
                PreservedPaths = [".venv", ".runtime", ".uv-cache", "checkpoints", "generated_audio", "outputs", "logs", "prompts", "archive", "launcher-update-backups", "run_gradio.py", "LOCAL_DEPLOYMENT.md", "run-local-server.cmd", "start-small-sfx.cmd", "start-small-music.cmd", "start-medium.cmd", "stop-local-server.cmd", "verify-install.cmd", "hf-login.cmd"]
            },
            new PluginPackageManifest
            {
                Id = "indextts2",
                DisplayName = "IndexTTS2",
                Version = "0.0.0-local",
                Publisher = "IndexTTS",
                Category = "Character voice",
                Description = "Character voice and emotional speech",
                Executable = Path.Combine(Environment.GetEnvironmentVariable("WINDIR") ?? @"C:\Windows", "System32", "WindowsPowerShell", "v1.0", "powershell.exe"),
                Arguments = "-NoProfile -ExecutionPolicy Bypass -File \"{root}/tools/windows_launcher.ps1\" -PreferredPort {port}",
                Runtime = "python",
                RuntimeVersion = ">=3.10",
                Port = settings.IndexTtsPort,
                RecommendedVramMiB = 7500,
                RecommendedSystemMemoryMiB = 16384,
                IsHighVram = true,
                RequiredFiles = ["tools/windows_launcher.ps1", "webui.py", "checkpoints"],
                Dependencies = ["python>=3.10", "cuda"],
                GitHubRepository = "index-tts/index-tts",
                PreservedPaths = [".venv", ".runtime", ".uv-cache", "checkpoints", "generated_audio", "outputs", "logs", "prompts", "archive", "launcher-update-backups", "README.md", "webui.py", "gen_subtitle.py", "tools/windows_launcher.ps1", "Start-IndexTTS.bat", "User-Guide.txt"]
            }
        ];

    public static LauncherModelCatalog CreateCatalog(LauncherSettings settings)
    {
        var roots = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["woosh-dflow"] = settings.WooshRoot,
            ["stable-audio-3"] = settings.StableRoot,
            ["indextts2"] = settings.IndexTtsRoot
        };
        return new LauncherModelCatalog
        {
            SchemaVersion = 3,
            Models = CreateManifests(settings).Select(manifest => ToDefinition(manifest, roots[manifest.Id])).ToList()
        };
    }

    private static LauncherModelDefinition ToDefinition(PluginPackageManifest manifest, string rootDirectory)
        => new()
        {
            Id = manifest.Id,
            DisplayName = manifest.DisplayName,
            Description = manifest.Description,
            Category = manifest.Category,
            RootDirectory = rootDirectory,
            Executable = manifest.Executable,
            Arguments = manifest.Arguments,
            Runtime = manifest.Runtime,
            RuntimeVersion = manifest.RuntimeVersion,
            Port = manifest.Port,
            RecommendedVramMiB = manifest.RecommendedVramMiB,
            RecommendedSystemMemoryMiB = manifest.RecommendedSystemMemoryMiB,
            RequiredFiles = manifest.RequiredFiles,
            Dependencies = manifest.Dependencies,
            GitHubRepository = manifest.GitHubRepository,
            GitHubBranch = manifest.GitHubBranch,
            InstalledVersion = manifest.Version,
            Publisher = manifest.Publisher,
            PackageSizeBytes = manifest.PackageSizeBytes,
            PreservedPaths = manifest.PreservedPaths,
            TrustSource = "BuiltInManifest",
            IsManifestTrusted = true,
            IsBuiltIn = true,
            IsHighVram = manifest.IsHighVram
        };
}
