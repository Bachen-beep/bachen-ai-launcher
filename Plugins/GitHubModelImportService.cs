using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

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
        string? installDirectory = null,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!TryNormalizeRepository(repository, out repository))
        {
            throw new InvalidDataException("GitHub repository is invalid.");
        }
        branch = branch.Trim();
        string commitSha;
        try
        {
            (branch, commitSha) = await ResolveFromApiAsync(repository, branch, progress, cancellationToken);
        }
        catch (HttpRequestException ex) when (ex.StatusCode is System.Net.HttpStatusCode.Forbidden or System.Net.HttpStatusCode.TooManyRequests)
        {
            progress?.Report("GitHub API rate limit reached; resolving the repository through its public Atom feed");
            (branch, commitSha) = await ResolveFromAtomAsync(repository, branch, cancellationToken);
        }

        ValidateResolvedSource(branch, commitSha);

        var safeId = SanitizeId(repository.Replace('/', '-'));
        var pluginsRoot = Path.GetFullPath(Path.Combine(dataRoot, "plugins"));
        var downloadsRoot = Path.GetFullPath(Path.Combine(dataRoot, "downloads"));
        Directory.CreateDirectory(pluginsRoot);
        Directory.CreateDirectory(downloadsRoot);
        var targetRoot = string.IsNullOrWhiteSpace(installDirectory)
            ? Path.Combine(pluginsRoot, safeId)
            : ValidateInstallDirectory(installDirectory);
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
                    var hasRepositoryContent = Directory.EnumerateFileSystemEntries(targetRoot)
                        .Any(path => !Path.GetFileName(path).Equals(".bachen-github-source.json", StringComparison.OrdinalIgnoreCase) &&
                                     !Path.GetFileName(path).Equals(".bachen-ai-launcher-update.json", StringComparison.OrdinalIgnoreCase));
                    if (hasRepositoryContent)
                    {
                        progress?.Report("Reusing the previously verified GitHub source");
                        return new GitHubModelImportResult(targetRoot, commitSha, branch);
                    }
                    progress?.Report("The cached source is incomplete; extracting it again");
                    Directory.Delete(targetRoot, true);
                }
            }
            if (Directory.Exists(targetRoot))
            {
                if (!Directory.EnumerateFileSystemEntries(targetRoot).Any())
                {
                    Directory.Delete(targetRoot);
                }
                else
                {
                    throw new IOException($"The managed plugin directory already exists and is not empty: {targetRoot}");
                }
            }
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
            await File.WriteAllTextAsync(
                Path.Combine(targetRoot, ".bachen-ai-launcher-update.json"),
                JsonSerializer.Serialize(new SourceUpdateState(commitSha, DateTimeOffset.Now), new JsonSerializerOptions { WriteIndented = true }),
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

    private async Task<(string Branch, string CommitSha)> ResolveFromApiAsync(string repository, string branch, IProgress<string>? progress, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(branch))
        {
            progress?.Report("Resolving the repository default branch");
            var repositoryJson = await httpClient.GetStringAsync($"https://api.github.com/repos/{repository}", cancellationToken);
            using var repositoryDocument = JsonDocument.Parse(repositoryJson);
            branch = repositoryDocument.RootElement.GetProperty("default_branch").GetString() ?? string.Empty;
        }

        ValidateBranch(branch);
        progress?.Report("Resolving the GitHub branch to an immutable commit");
        var commitJson = await httpClient.GetStringAsync(
            $"https://api.github.com/repos/{repository}/commits/{Uri.EscapeDataString(branch)}",
            cancellationToken);
        using var commitDocument = JsonDocument.Parse(commitJson);
        return (branch, commitDocument.RootElement.GetProperty("sha").GetString() ?? string.Empty);
    }

    private async Task<(string Branch, string CommitSha)> ResolveFromAtomAsync(string repository, string branch, CancellationToken cancellationToken)
    {
        var feedUrl = string.IsNullOrWhiteSpace(branch)
            ? $"https://github.com/{repository}/commits.atom"
            : $"https://github.com/{repository}/commits/{Uri.EscapeDataString(branch)}.atom";
        using var response = await httpClient.GetAsync(feedUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var feed = await XDocument.LoadAsync(stream, LoadOptions.None, cancellationToken);
        XNamespace atom = "http://www.w3.org/2005/Atom";
        if (string.IsNullOrWhiteSpace(branch))
        {
            var selfLink = feed.Root?.Elements(atom + "link")
                .FirstOrDefault(element => element.Attribute("rel")?.Value.Equals("self", StringComparison.OrdinalIgnoreCase) == true)
                ?.Attribute("href")?.Value;
            var match = Regex.Match(selfLink ?? string.Empty, @"/commits/(?<branch>.+)\.atom$");
            branch = match.Success ? Uri.UnescapeDataString(match.Groups["branch"].Value) : string.Empty;
        }
        var entryId = feed.Root?.Element(atom + "entry")?.Element(atom + "id")?.Value ?? string.Empty;
        var commitSha = entryId.Split('/').LastOrDefault() ?? string.Empty;
        return (branch, commitSha);
    }

    private static void ValidateResolvedSource(string branch, string commitSha)
    {
        ValidateBranch(branch);
        if (string.IsNullOrWhiteSpace(commitSha) || !Regex.IsMatch(commitSha, "^[a-fA-F0-9]{40}$"))
        {
            throw new InvalidDataException("GitHub did not return a valid commit SHA.");
        }
    }

    private static void ValidateBranch(string branch)
    {
        if (!Regex.IsMatch(branch, "^[A-Za-z0-9._/-]+$") || branch.Contains("..", StringComparison.Ordinal))
        {
            throw new InvalidDataException("GitHub default branch is missing or invalid.");
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

    private static string ValidateInstallDirectory(string installDirectory)
    {
        var fullPath = Path.GetFullPath(Environment.ExpandEnvironmentVariables(installDirectory.Trim()));
        var root = Path.GetPathRoot(fullPath);
        if (string.IsNullOrWhiteSpace(root) || fullPath.Equals(root, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("The plugin install directory cannot be a drive root.");
        }
        if (File.Exists(fullPath))
        {
            throw new IOException($"The plugin install directory is an existing file: {fullPath}");
        }
        return fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }
}
