using System.Text.RegularExpressions;

namespace BaChenAiLauncher;

internal sealed record RepositoryLaunchOption(string DisplayName, string EntryScript, string Arguments, bool IsRecommended = false);

internal sealed record GitHubRepositoryAnalysis(
    string DisplayName,
    string Description,
    string Category,
    string Runtime,
    string RuntimeVersion,
    string Executable,
    string RequirementsFile,
    string EnvironmentManager,
    string[] EnvironmentArguments,
    int RecommendedVramMiB,
    int RecommendedSystemMemoryMiB,
    bool IsHighVram,
    string[] RequiredFiles,
    string[] Dependencies,
    RepositoryLaunchOption[] LaunchOptions,
    string Confidence,
    string[] Notes);

internal static class GitHubRepositoryAnalyzer
{
    private static readonly string[] ExactEntryNames =
    [
        "gradio_app.py", "app.py", "webui.py", "launch.py", "demo.py", "main.py",
        "gradio_demo.py", "infer_gradio.py"
    ];

    public static GitHubRepositoryAnalysis Analyze(string repository, string repositoryRoot, bool hasNvidiaGpu)
    {
        if (!GitHubModelImportService.TryNormalizeRepository(repository, out var normalizedRepository))
        {
            throw new InvalidDataException("GitHub repository is invalid.");
        }
        repositoryRoot = Path.GetFullPath(repositoryRoot);
        var readmePath = Directory.EnumerateFiles(repositoryRoot, "README*", SearchOption.TopDirectoryOnly).FirstOrDefault();
        var readme = readmePath is null ? string.Empty : File.ReadAllText(readmePath);
        var pyprojectPath = Path.Combine(repositoryRoot, "pyproject.toml");
        var pyproject = File.Exists(pyprojectPath) ? File.ReadAllText(pyprojectPath) : string.Empty;
        var requirementsFile = FindRequirementsFile(repositoryRoot);
        var repositoryName = normalizedRepository.Split('/')[1];
        var projectName = ReadTomlString(pyproject, "name") ?? repositoryName;
        var description = ReadTomlString(pyproject, "description") ?? FirstReadmeParagraph(readme) ?? $"Local AI plugin imported from {normalizedRepository}.";
        var runtimeVersion = NormalizePythonConstraint(ReadTomlString(pyproject, "requires-python"));
        var isPython = File.Exists(pyprojectPath) || requirementsFile.Length > 0 || Directory.EnumerateFiles(repositoryRoot, "*.py", SearchOption.AllDirectories).Any();
        if (!isPython)
        {
            throw new InvalidOperationException("No supported Python project or launch entry was detected. This repository needs a custom plugin manifest.");
        }

        var options = ApplyKnownRepositoryLaunchOptions(normalizedRepository, DetectLaunchOptions(repositoryRoot));
        if (options.Length == 0)
        {
            throw new InvalidOperationException("No safe launch entry could be detected. Review the repository README or use a signed plugin manifest.");
        }

        var combinedText = $"{normalizedRepository}\n{projectName}\n{description}\n{readme}";
        var category = DetectCategory(combinedText);
        var usesUv = File.Exists(Path.Combine(repositoryRoot, "uv.lock")) || pyproject.Contains("[tool.uv]", StringComparison.OrdinalIgnoreCase) || Regex.IsMatch(readme, @"\buv\s+sync\b", RegexOptions.IgnoreCase);
        var environmentArguments = usesUv
            ? BuildUvArguments(pyproject, readme, hasNvidiaGpu, options.Any(option => option.EntryScript.Contains("gradio", StringComparison.OrdinalIgnoreCase)))
            : pyproject.Length > 0
                ? ["-m", "pip", "install", "--disable-pip-version-check", "-e", "."]
                : [];
        var resource = EstimateResources(combinedText);
        var dependencies = new List<string> { $"python{runtimeVersion}" };
        if (hasNvidiaGpu && ContainsAny(combinedText, "cuda", "gpu", "torch")) dependencies.Add("cuda");
        var notes = new List<string>
        {
            $"Detected {options.Length} launch option(s).",
            usesUv ? $"Detected uv project; selected {(hasNvidiaGpu ? "CUDA" : "CPU")} dependency profile." : "Detected managed Python environment."
        };
        if (ContainsAny(combinedText, "huggingface", "hugging face", "hf_token", "access token"))
        {
            notes.Add("The repository mentions Hugging Face; model access or a token may be required after installation.");
        }
        if (ContainsAny(combinedText, "model weights", "pretrained weights", "checkpoints/") && ContainsAny(combinedText, "download", "releases/download"))
        {
            notes.Add("The repository references external model weights; review its download or authorization step before the first launch.");
        }

        return new GitHubRepositoryAnalysis(
            Humanize(projectName),
            Limit(description, 240),
            category,
            "python",
            runtimeVersion,
            ".venv/Scripts/python.exe",
            requirementsFile,
            usesUv ? "uv" : "pip",
            environmentArguments,
            resource.VramMiB,
            resource.RamMiB,
            resource.VramMiB >= 6144,
            options.Select(option => option.EntryScript).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            dependencies.ToArray(),
            options,
            options.Any(option => option.IsRecommended) ? "High" : "Medium",
            notes.ToArray());
    }

    private static RepositoryLaunchOption[] DetectLaunchOptions(string root)
    {
        var scripts = Directory.EnumerateFiles(root, "*.py", SearchOption.AllDirectories)
            .Where(path => Relative(root, path).Split('/').Length <= 3)
            .Select(path => new { Path = path, Relative = Relative(root, path), Name = Path.GetFileName(path) })
            .ToArray();
        var candidates = scripts
            .Where(script => ExactEntryNames.Contains(script.Name, StringComparer.OrdinalIgnoreCase) ||
                script.Name.StartsWith("gradio_", StringComparison.OrdinalIgnoreCase) ||
                script.Name.EndsWith("_gradio.py", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(script => script.Name.Contains("DFlow", StringComparison.OrdinalIgnoreCase))
            .ThenBy(script => Array.FindIndex(ExactEntryNames, name => name.Equals(script.Name, StringComparison.OrdinalIgnoreCase)) is var rank && rank >= 0 ? rank : 100)
            .ThenBy(script => script.Relative, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return candidates.Select((script, index) =>
        {
            var contents = File.ReadAllText(script.Path);
            var arguments = QuoteIfNeeded(script.Relative);
            if (contents.Contains("--server-name", StringComparison.Ordinal)) arguments += " --server-name 127.0.0.1";
            if (contents.Contains("--server-port", StringComparison.Ordinal)) arguments += " --server-port {port}";
            return new RepositoryLaunchOption(Humanize(Path.GetFileNameWithoutExtension(script.Name)), script.Relative, arguments, index == 0);
        }).ToArray();
    }

    private static string[] BuildUvArguments(string pyproject, string readme, bool hasNvidiaGpu, bool requiresUi)
    {
        var arguments = new List<string> { "sync" };
        var profile = hasNvidiaGpu ? "cuda" : "cpu";
        var supportsProfile = Regex.IsMatch(pyproject, $@"\b{profile}\s*=", RegexOptions.IgnoreCase) || Regex.IsMatch(readme, $@"uv\s+sync[^\r\n]*--extra\s+{profile}\b", RegexOptions.IgnoreCase);
        if (supportsProfile)
        {
            arguments.AddRange(["--extra", profile]);
        }
        if (requiresUi && Regex.IsMatch(pyproject, @"(?m)^\s*ui\s*=\s*\[", RegexOptions.IgnoreCase))
        {
            arguments.AddRange(["--extra", "ui"]);
        }
        return arguments.ToArray();
    }

    private static RepositoryLaunchOption[] ApplyKnownRepositoryLaunchOptions(string repository, RepositoryLaunchOption[] detected)
    {
        if (!repository.Equals("Stability-AI/stable-audio-3", StringComparison.OrdinalIgnoreCase) ||
            !detected.Any(option => option.EntryScript.Equals("run_gradio.py", StringComparison.OrdinalIgnoreCase)))
        {
            return detected;
        }

        return
        [
            new RepositoryLaunchOption("Small SFX", "run_gradio.py", "run_gradio.py --model small-sfx --port {port}", true),
            new RepositoryLaunchOption("Small Music", "run_gradio.py", "run_gradio.py --model small-music --port {port}"),
            new RepositoryLaunchOption("Medium", "run_gradio.py", "run_gradio.py --model medium --port {port}")
        ];
    }

    private static string FindRequirementsFile(string root)
    {
        var preferred = new[] { "requirements.txt", "requirements_windows.txt", "requirements-win.txt" };
        return preferred.FirstOrDefault(name => File.Exists(Path.Combine(root, name))) ?? string.Empty;
    }

    private static string? ReadTomlString(string toml, string key)
    {
        if (string.IsNullOrWhiteSpace(toml)) return null;
        var match = Regex.Match(toml, $"(?m)^\\s*{Regex.Escape(key)}\\s*=\\s*[\"'](?<value>[^\"']+)[\"']\\s*$");
        return match.Success ? match.Groups["value"].Value.Trim() : null;
    }

    private static string NormalizePythonConstraint(string? constraint)
        => string.IsNullOrWhiteSpace(constraint) ? ">=3.10" : constraint.Trim();

    private static string DetectCategory(string text)
    {
        if (ContainsAny(text, "text-to-speech", "tts", "voice cloning")) return "TTS";
        if (ContainsAny(text, "sound effect", "text-to-audio", "audio generation", "music generation")) return "Audio generation";
        if (ContainsAny(text, "image generation", "text-to-image", "diffusion image")) return "Image generation";
        if (ContainsAny(text, "video generation", "text-to-video")) return "Video generation";
        if (ContainsAny(text, "large language model", "chatbot", "llm")) return "LLM / Chat";
        return "Experimental";
    }

    private static (int VramMiB, int RamMiB) EstimateResources(string text)
    {
        if (ContainsAny(text, "cuda", "gpu", "torch")) return (6144, 16384);
        return (0, 8192);
    }

    private static string? FirstReadmeParagraph(string readme)
    {
        foreach (var paragraph in Regex.Split(readme, @"\r?\n\s*\r?\n"))
        {
            var value = Regex.Replace(paragraph, @"\s+", " ").Trim();
            if (value.Length >= 30 && !value.StartsWith('#') && !value.StartsWith("<", StringComparison.Ordinal)) return value;
        }
        return null;
    }

    private static string Humanize(string value)
        => Regex.Replace(value.Replace('-', ' ').Replace('_', ' '), @"\s+", " ").Trim();

    private static string Limit(string value, int length)
        => value.Length <= length ? value : value[..(length - 3)].TrimEnd() + "...";

    private static bool ContainsAny(string value, params string[] candidates)
        => candidates.Any(candidate => value.Contains(candidate, StringComparison.OrdinalIgnoreCase));

    private static string Relative(string root, string path)
        => Path.GetRelativePath(root, path).Replace('\\', '/');

    private static string QuoteIfNeeded(string value)
        => value.Contains(' ') ? $"\"{value}\"" : value;
}
