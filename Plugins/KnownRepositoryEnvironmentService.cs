using System.Text.RegularExpressions;

namespace BaChenAiLauncher;

internal static class KnownRepositoryEnvironmentService
{
    private const string StableAudioRepository = "Stability-AI/stable-audio-3";

    public static bool HasMissingEnvironment(string repository, string launchArguments, string rootDirectory)
        => IsStableAudioGradio(repository, launchArguments) &&
           (!File.Exists(Path.Combine(rootDirectory, ".venv", "Lib", "site-packages", "gradio", "__init__.py")) ||
            !PythonEnvironmentService.IsEnvironmentCompatible(rootDirectory, GitHubRepositoryAnalyzer.ReadPythonConstraint(rootDirectory)));

    public static string NormalizeLaunchArguments(string repository, string launchArguments)
    {
        if (!IsStableAudioGradio(repository, launchArguments))
        {
            return launchArguments;
        }

        var normalized = Regex.Replace(launchArguments, @"\s+--port\s+\S+", string.Empty, RegexOptions.IgnoreCase).Trim();
        if (!normalized.Contains("--model", StringComparison.OrdinalIgnoreCase))
        {
            normalized += " --model small-sfx";
        }
        return GetStableAudioModel(normalized) == "medium" && !normalized.Contains("--model-half", StringComparison.OrdinalIgnoreCase)
            ? normalized + " --model-half"
            : normalized;
    }

    public static string BuildStableAudioLaunchArguments(string model)
    {
        model = model.Trim().ToLowerInvariant();
        if (model is not ("small-sfx" or "small-music" or "medium"))
        {
            throw new ArgumentOutOfRangeException(nameof(model), model, "Unknown Stable Audio 3 model profile.");
        }
        return model == "medium"
            ? "run_gradio.py --model medium --model-half"
            : $"run_gradio.py --model {model}";
    }

    public static async Task EnsureEnvironmentAsync(
        string repository,
        string launchArguments,
        string rootDirectory,
        string dataRoot,
        HttpClient httpClient,
        bool hasNvidiaGpu,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!HasMissingEnvironment(repository, launchArguments, rootDirectory))
        {
            return;
        }

        var analysis = GitHubRepositoryAnalyzer.Analyze(repository, rootDirectory, hasNvidiaGpu);
        await PythonEnvironmentService.EnsureRepositoryAsync(analysis, rootDirectory, dataRoot, httpClient, progress, cancellationToken);
        if (HasMissingEnvironment(repository, launchArguments, rootDirectory))
        {
            throw new InvalidOperationException("Stable Audio 3 UI dependency installation completed, but the gradio package is still missing.");
        }
    }

    private static bool IsStableAudioGradio(string repository, string launchArguments)
        => repository.Equals(StableAudioRepository, StringComparison.OrdinalIgnoreCase) &&
           launchArguments.Contains("run_gradio.py", StringComparison.OrdinalIgnoreCase);

    private static string GetStableAudioModel(string launchArguments)
    {
        var match = Regex.Match(launchArguments, @"(?:^|\s)--model(?:\s+|=)(?<model>[^\s]+)", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups["model"].Value.Trim().ToLowerInvariant() : string.Empty;
    }
}
