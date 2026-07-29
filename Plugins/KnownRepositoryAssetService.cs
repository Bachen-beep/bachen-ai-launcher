using System.IO.Compression;

namespace BaChenAiLauncher;

internal static class KnownRepositoryAssetService
{
    private const string WooshRepository = "SonyResearch/Woosh";

    private static readonly PluginAssetPackage[] WooshDFlowAssets =
    [
        new()
        {
            Id = "woosh-ae-v1.0.0",
            Url = "https://github.com/SonyResearch/Woosh/releases/download/v1.0.0/Woosh-AE.zip",
            Sha256 = "D6F77E3792EE43C21DA580F39D6576E0DA3E4B46B949223259ADF36036C1F9AF",
            SizeBytes = 822991075,
            DestinationPath = "."
        },
        new()
        {
            Id = "text-conditioner-a-v1.0.0",
            Url = "https://github.com/SonyResearch/Woosh/releases/download/v1.0.0/TextConditionerA.zip",
            Sha256 = "68A777B9AC28AA5DAF6017B21AF9A3659DE75074EA14DAC65F5231A42C375193",
            SizeBytes = 1297121262,
            DestinationPath = "."
        },
        new()
        {
            Id = "woosh-dflow-v1.0.0",
            Url = "https://github.com/SonyResearch/Woosh/releases/download/v1.0.0/Woosh-DFlow.zip",
            Sha256 = "26CFE732500E3952C58AAAF433D29D75B46D42AFE5E52F49430D6093EABFDB04",
            SizeBytes = 1281505601,
            DestinationPath = "."
        }
    ];

    private static readonly string[] WooshDFlowRequiredFiles =
    [
        "checkpoints/Woosh-AE/config.yaml",
        "checkpoints/Woosh-AE/weights.safetensors",
        "checkpoints/TextConditionerA/config.yaml",
        "checkpoints/TextConditionerA/weights.safetensors",
        "checkpoints/Woosh-DFlow/config.yaml",
        "checkpoints/Woosh-DFlow/weights.safetensors"
    ];

    public static string[] GetRequiredFiles(string repository, string launchArguments)
        => IsWooshDFlow(repository, launchArguments) ? WooshDFlowRequiredFiles : [];

    public static bool HasMissingAssets(string repository, string launchArguments, string rootDirectory)
        => GetRequiredFiles(repository, launchArguments)
            .Any(relative => !File.Exists(Path.Combine(rootDirectory, relative.Replace('/', Path.DirectorySeparatorChar))));

    public static async Task EnsureAssetsAsync(
        string repository,
        string launchArguments,
        string rootDirectory,
        string dataRoot,
        HttpClient httpClient,
        IProgress<string>? progress = null,
        IProgress<PluginDownloadProgress>? downloadProgress = null,
        CancellationToken cancellationToken = default)
    {
        RepairNestedCheckpointLayout(repository, launchArguments, rootDirectory);
        if (!HasMissingAssets(repository, launchArguments, rootDirectory))
        {
            return;
        }
        if (!IsWooshDFlow(repository, launchArguments))
        {
            return;
        }

        foreach (var asset in WooshDFlowAssets)
        {
            progress?.Report($"Downloading and verifying required Woosh model asset {asset.Id}");
            var archivePath = await new PluginDownloadService(httpClient).DownloadAssetAsync(
                "sonyresearch-woosh",
                asset,
                dataRoot,
                downloadProgress,
                cancellationToken);
            ExtractSecurely(archivePath, Path.Combine(rootDirectory, asset.DestinationPath));
        }

        if (HasMissingAssets(repository, launchArguments, rootDirectory))
        {
            var missing = GetRequiredFiles(repository, launchArguments)
                .Where(relative => !File.Exists(Path.Combine(rootDirectory, relative.Replace('/', Path.DirectorySeparatorChar))));
            throw new InvalidDataException("Woosh-DFlow model asset installation completed, but required checkpoint files are still missing: " + string.Join(", ", missing));
        }
    }

    internal static void RepairNestedCheckpointLayout(string repository, string launchArguments, string rootDirectory)
    {
        if (!IsWooshDFlow(repository, launchArguments))
        {
            return;
        }

        var nestedRoot = Path.Combine(rootDirectory, "checkpoints", "checkpoints");
        if (!Directory.Exists(nestedRoot))
        {
            return;
        }

        foreach (var relative in WooshDFlowRequiredFiles)
        {
            var checkpointRelative = relative["checkpoints/".Length..].Replace('/', Path.DirectorySeparatorChar);
            var source = Path.Combine(nestedRoot, checkpointRelative);
            var destination = Path.Combine(rootDirectory, relative.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(source) || File.Exists(destination))
            {
                continue;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Move(source, destination);
        }

        foreach (var directory in new[] { "Woosh-AE", "TextConditionerA", "Woosh-DFlow" })
        {
            DeleteIfEmpty(Path.Combine(nestedRoot, directory));
        }
        DeleteIfEmpty(nestedRoot);
    }

    private static void DeleteIfEmpty(string path)
    {
        if (Directory.Exists(path) && !Directory.EnumerateFileSystemEntries(path).Any())
        {
            Directory.Delete(path);
        }
    }

    private static bool IsWooshDFlow(string repository, string launchArguments)
        => repository.Equals(WooshRepository, StringComparison.OrdinalIgnoreCase) &&
           launchArguments.Contains("gradio_Woosh-DFlow.py", StringComparison.OrdinalIgnoreCase);

    private static void ExtractSecurely(string archivePath, string destinationRoot)
    {
        var root = Path.GetFullPath(destinationRoot) + Path.DirectorySeparatorChar;
        using var archive = ZipFile.OpenRead(archivePath);
        foreach (var entry in archive.Entries)
        {
            var destination = Path.GetFullPath(Path.Combine(destinationRoot, entry.FullName.Replace('/', Path.DirectorySeparatorChar)));
            if (!destination.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException($"Unsafe model asset path: {entry.FullName}");
            }
            if (string.IsNullOrEmpty(entry.Name))
            {
                Directory.CreateDirectory(destination);
                continue;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            entry.ExtractToFile(destination, true);
        }
    }
}
