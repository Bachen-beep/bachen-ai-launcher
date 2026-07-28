using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace BaChenAiLauncher;

internal sealed record GitHubModelImportResult(string RootDirectory, string CommitSha, string Branch);

internal sealed class GitHubModelImportService(HttpClient httpClient)
{
    internal static bool TryNormalizeRepository(string input, out string repository)
    {
        repository = string.Empty;
        input = input.Trim();
        if (input.StartsWith("git@github.com:", StringComparison.OrdinalIgnoreCase))
        {
            input = input["git@github.com:".Length..];
        }
        else if (Uri.TryCreate(input, UriKind.Absolute, out var uri))
        {
            if (!uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
                !uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) ||
                !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
            {
                return false;
            }
            input = uri.AbsolutePath.Trim('/');
        }
        input = Regex.Replace(input, "\\.git$", string.Empty, RegexOptions.IgnoreCase).Trim('/');
        if (!Regex.IsMatch(input, "^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$"))
        {
            return false;
        }
        repository = input;
        return true;
    }

    public async Task<GitHubModelImportResult> ImportAsync(
        string repository,
        string branch,
        string dataRoot,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!TryNormalizeRepository(repository, out repository))
        {
            throw new InvalidDataException("GitHub repository is invalid.");
        }
        branch = branch.Trim();
        if (string.IsNullOrWhiteSpace(branch))
        {
            progress?.Report("Resolving the repository default branch");
            var repositoryJson = await httpClient.GetStringAsync($"https://api.github.com/repos/{repository}", cancellationToken);
            using var repositoryDocument = JsonDocument.Parse(repositoryJson);
            branch = repositoryDocument.RootElement.GetProperty("default_branch").GetString() ?? string.Empty;
        }
        if (!Regex.IsMatch(branch, "^[A-Za-z0-9._/-]+$") || branch.Contains("..", StringComparison.Ordinal))
        {
            throw new InvalidDataException("GitHub default branch is missing or invalid.");
        }

        progress?.Report("Resolving the GitHub branch to an immutable commit");
        var commitJson = await httpClient.GetStringAsync(
            $"https://api.github.com/repos/{repository}/commits/{Uri.EscapeDataString(branch)}",
            cancellationToken);
        using var commitDocument = JsonDocument.Parse(commitJson);
        var commitSha = commitDocument.RootElement.GetProperty("sha").GetString();
        if (string.IsNullOrWhiteSpace(commitSha) || !Regex.IsMatch(commitSha, "^[a-fA-F0-9]{40}$"))
        {
            throw new InvalidDataException("GitHub did not return a valid commit SHA.");
        }

        var safeId = SanitizeId(repository.Replace('/', '-'));
        var pluginsRoot = Path.GetFullPath(Path.Combine(dataRoot, "plugins"));
        var downloadsRoot = Path.GetFullPath(Path.Combine(dataRoot, "downloads"));
        Directory.CreateDirectory(pluginsRoot);
        Directory.CreateDirectory(downloadsRoot);
        var targetRoot = Path.Combine(pluginsRoot, safeId);
        if (Directory.Exists(targetRoot))
        {
            var metadataPath = Path.Combine(targetRoot, ".bachen-github-source.json");
            if (File.Exists(metadataPath))
            {
                using var metadata = JsonDocument.Parse(await File.ReadAllTextAsync(metadataPath, cancellationToken));
                var savedRepository = metadata.RootElement.GetProperty("repository").GetString();
                var savedCommit = metadata.RootElement.GetProperty("commitSha").GetString();
                if (repository.Equals(savedRepository, StringComparison.OrdinalIgnoreCase) && commitSha.Equals(savedCommit, StringComparison.OrdinalIgnoreCase))
                {
                    progress?.Report("Reusing the previously verified GitHub source");
                    return new GitHubModelImportResult(targetRoot, commitSha, branch);
                }
            }
            throw new IOException($"The managed plugin directory already exists: {targetRoot}");
        }

        var archivePath = Path.Combine(downloadsRoot, $"{safeId}-{commitSha[..12]}.zip");
        if (!File.Exists(archivePath))
        {
            progress?.Report($"Downloading GitHub commit {commitSha[..12]}");
            using var response = await httpClient.GetAsync(
                $"https://github.com/{repository}/archive/{commitSha}.zip",
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            response.EnsureSuccessStatusCode();
            if (response.RequestMessage?.RequestUri?.Scheme != Uri.UriSchemeHttps)
            {
                throw new HttpRequestException("GitHub redirected the source archive to a non-HTTPS address.");
            }
            var partialPath = archivePath + ".partial";
            await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken))
            await using (var destination = new FileStream(partialPath, FileMode.Create, FileAccess.Write, FileShare.None, 1024 * 128, true))
            {
                await source.CopyToAsync(destination, cancellationToken);
            }
            File.Move(partialPath, archivePath, true);
        }

        var stagingRoot = Path.Combine(downloadsRoot, $"import-{safeId}-{Guid.NewGuid():N}");
        try
        {
            progress?.Report("Verifying and extracting the GitHub source archive");
            Directory.CreateDirectory(stagingRoot);
            ExtractSecurely(archivePath, stagingRoot);
            var directories = Directory.GetDirectories(stagingRoot);
            var files = Directory.GetFiles(stagingRoot);
            var contentRoot = directories.Length == 1 && files.Length == 0 ? directories[0] : stagingRoot;
            Directory.Move(contentRoot, targetRoot);
            await File.WriteAllTextAsync(
                Path.Combine(targetRoot, ".bachen-github-source.json"),
                JsonSerializer.Serialize(new { repository, branch, commitSha }, new JsonSerializerOptions { WriteIndented = true }),
                cancellationToken);
            if (!contentRoot.Equals(stagingRoot, StringComparison.OrdinalIgnoreCase) && Directory.Exists(stagingRoot))
            {
                Directory.Delete(stagingRoot, true);
            }
            return new GitHubModelImportResult(targetRoot, commitSha, branch);
        }
        catch
        {
            if (Directory.Exists(targetRoot))
            {
                Directory.Delete(targetRoot, true);
            }
            throw;
        }
        finally
        {
            if (Directory.Exists(stagingRoot))
            {
                Directory.Delete(stagingRoot, true);
            }
        }
    }

    private static void ExtractSecurely(string archivePath, string destinationRoot)
    {
        var normalizedRoot = Path.GetFullPath(destinationRoot) + Path.DirectorySeparatorChar;
        using var archive = ZipFile.OpenRead(archivePath);
        foreach (var entry in archive.Entries)
        {
            var destination = Path.GetFullPath(Path.Combine(destinationRoot, entry.FullName.Replace('/', Path.DirectorySeparatorChar)));
            if (!destination.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException($"Unsafe GitHub archive path: {entry.FullName}");
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

    private static string SanitizeId(string value)
        => new(value.ToLowerInvariant().Select(character => char.IsLetterOrDigit(character) || character is '-' or '_' ? character : '-').ToArray());
}
