using System.Diagnostics;
using System.Text.RegularExpressions;

namespace BaChenAiLauncher;

internal static class KnownRepositoryAuthorizationService
{
    private const string StableAudioRepository = "Stability-AI/stable-audio-3";

    public static PluginPackageManifest? CreateLaunchManifest(
        string repository,
        string launchArguments,
        string displayName)
    {
        var model = GetStableAudioModel(repository, launchArguments) ?? string.Empty;
        var modelId = model switch
        {
            "small-sfx" => "stabilityai/stable-audio-3-small-sfx",
            "small-music" => "stabilityai/stable-audio-3-small-music",
            "medium" => "stabilityai/stable-audio-3-medium",
            _ => string.Empty
        };
        if (string.IsNullOrWhiteSpace(modelId))
        {
            return null;
        }

        return new PluginPackageManifest
        {
            DisplayName = $"{displayName} - {model}",
            RequiresExternalAuthorization = true,
            ModelProvider = "huggingface",
            ModelId = modelId,
            AuthorizationUrl = $"https://huggingface.co/{modelId}",
            AuthorizationProbePath = "model_config.json"
        };
    }

    public static string? GetStableAudioModel(string repository, string launchArguments)
    {
        if (!repository.Equals(StableAudioRepository, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var match = Regex.Match(
            launchArguments ?? string.Empty,
            @"(?:^|\s)--model(?:\s+|=)(?<model>[A-Za-z0-9_-]+)(?=\s|$)",
            RegexOptions.IgnoreCase);
        return match.Success ? match.Groups["model"].Value.ToLowerInvariant() : null;
    }

    public static void ApplyCredential(ProcessStartInfo startInfo, string token)
    {
        if (!string.IsNullOrWhiteSpace(token))
        {
            startInfo.Environment["HF_TOKEN"] = token.Trim();
        }
    }
}
