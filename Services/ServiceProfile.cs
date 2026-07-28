namespace BaChenAiLauncher;

internal sealed record ServiceProfile(
    string Name,
    string Description,
    string WorkingDirectory,
    string Executable,
    string Arguments,
    int Port,
    bool IsMedium = false,
    string[]? RequiredFiles = null,
    int RecommendedVramMiB = 0,
    int RecommendedSystemMemoryMiB = 4096,
    string[]? Dependencies = null);
