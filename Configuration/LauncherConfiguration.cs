namespace BaChenAiLauncher;

internal static class LauncherPaths
{
    private const string ConfigEnvironmentVariable = "BACHEN_AI_CONFIG_DIR";
    private const string DataEnvironmentVariable = "BACHEN_AI_DATA_ROOT";
    private const string LegacyConfigEnvironmentVariable = "BACHEN_AI_AUDIO_CONFIG_DIR";
    private const string LegacyDataEnvironmentVariable = "BACHEN_AI_AUDIO_DATA_ROOT";

    private static readonly string UserProfileDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    public static string BaseDirectory { get; } = Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory;

    public static string UserConfigDirectory { get; } =
        GetEnvironmentOverride(ConfigEnvironmentVariable, LegacyConfigEnvironmentVariable) is { Length: > 0 } configured
            ? Path.GetFullPath(configured)
            : Path.Combine(UserProfileDirectory, "AppData", "Local", "BaChen AI Launcher");

    public static string DefaultDataDirectory { get; } =
        GetEnvironmentOverride(DataEnvironmentVariable, LegacyDataEnvironmentVariable) is { Length: > 0 } configured
            ? Path.GetFullPath(configured)
            : Path.Combine(UserProfileDirectory, "Documents", "BaChen AI Launcher Data");

    public static string LegacyUserConfigDirectory { get; } = Path.Combine(UserProfileDirectory, "AppData", "Local", "Bachen AI Audio");
    public static string BrandedLegacySettingsPath { get; } = Path.Combine(BaseDirectory, "BaChen AI Launcher.settings.json");
    public static string BrandedLegacyModelCatalogPath { get; } = Path.Combine(BaseDirectory, "BaChen AI Launcher.models.json");
    public static string LegacySettingsPath { get; } = Path.Combine(BaseDirectory, "AI Audio Launcher.settings.json");
    public static string LegacyModelCatalogPath { get; } = Path.Combine(BaseDirectory, "AI Audio Launcher.models.json");
    public static bool UsesConfigOverride { get; } = GetEnvironmentOverride(ConfigEnvironmentVariable, LegacyConfigEnvironmentVariable) is not null;
    public static bool UsesDataOverride { get; } = GetEnvironmentOverride(DataEnvironmentVariable, LegacyDataEnvironmentVariable) is not null;

    private static string? GetEnvironmentOverride(string currentName, string legacyName)
    {
        var current = Environment.GetEnvironmentVariable(currentName);
        return !string.IsNullOrWhiteSpace(current)
            ? current
            : Environment.GetEnvironmentVariable(legacyName) is { Length: > 0 } legacy ? legacy : null;
    }
}

internal sealed class LauncherSettings
{
    public int SchemaVersion { get; set; } = 2;
    public string DataRoot { get; set; } = LauncherPaths.DefaultDataDirectory;
    public string WooshRoot { get; set; } = Path.Combine(LauncherPaths.DefaultDataDirectory, "plugins", "Woosh");
    public string StableRoot { get; set; } = Path.Combine(LauncherPaths.DefaultDataDirectory, "plugins", "Stable Audio 3");
    public string IndexTtsRoot { get; set; } = Path.Combine(LauncherPaths.DefaultDataDirectory, "plugins", "IndexTTS");
    public int WooshPort { get; set; } = 7860;
    public int StablePort { get; set; } = 7861;
    public int IndexTtsPort { get; set; } = 7862;
    public bool AutomaticallyCheckLauncherUpdates { get; set; } = true;
    public string SkippedLauncherVersion { get; set; } = string.Empty;
    public DateTimeOffset? LauncherUpdateDeferredUntil { get; set; }
}

internal sealed class LauncherModelCatalog
{
    public int SchemaVersion { get; set; } = 2;
    public List<LauncherModelDefinition> Models { get; set; } = [];
}

internal sealed class LauncherModelDefinition
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string DisplayName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Category { get; set; } = "Experimental";
    public string RootDirectory { get; set; } = string.Empty;
    public string Executable { get; set; } = string.Empty;
    public string Arguments { get; set; } = string.Empty;
    public int Port { get; set; }
    public int RecommendedVramMiB { get; set; } = 4096;
    public int RecommendedSystemMemoryMiB { get; set; } = 8192;
    public string[] RequiredFiles { get; set; } = [];
    public string[] Dependencies { get; set; } = [];
    public string GitHubRepository { get; set; } = string.Empty;
    public string GitHubBranch { get; set; } = "main";
    public string InstalledVersion { get; set; } = "local";
    public string Publisher { get; set; } = string.Empty;
    public string SigningKeyId { get; set; } = string.Empty;
    public string PackageSha256 { get; set; } = string.Empty;
    public bool IsManifestTrusted { get; set; }
    public string TrustSource { get; set; } = "LocalUser";
    public bool IsBuiltIn { get; set; }
    public bool IsHighVram { get; set; }
}
