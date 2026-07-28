using System.Text;
using System.Text.Json;
using System.Xml.Linq;

namespace BaChenAiLauncher;

internal sealed class GitHubUpdateService(HttpClient client)
{
    public static string UpdateStatePath(GitHubUpdateSource source)
        => Path.Combine(source.DeploymentRoot, ".bachen-ai-launcher-update.json");

    public SourceUpdateState? LoadState(GitHubUpdateSource source)
    {
        try
        {
            var path = UpdateStatePath(source);
            if (!File.Exists(path))
            {
                path = Path.Combine(source.DeploymentRoot, ".ai-audio-launcher-update.json");
            }
            return File.Exists(path) ? JsonSerializer.Deserialize<SourceUpdateState>(File.ReadAllText(path)) : null;
        }
        catch
        {
            return null;
        }
    }

    public void SaveState(GitHubUpdateSource source, string commitSha)
    {
        var json = JsonSerializer.Serialize(new SourceUpdateState(commitSha, DateTimeOffset.Now), new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(UpdateStatePath(source), json, Encoding.UTF8);
    }

    public async Task<SourceUpdateCheck> FetchCheckAsync(GitHubUpdateSource source)
    {
        string sha;
        DateTimeOffset date;
        string message;
        try
        {
            using var response = await client.GetAsync($"https://api.github.com/repos/{source.Repository}/commits/{source.Branch}");
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync();
            using var document = await JsonDocument.ParseAsync(stream);
            var root = document.RootElement;
            sha = root.GetProperty("sha").GetString() ?? throw new InvalidDataException("GitHub response did not contain a commit SHA.");
            var dateText = root.GetProperty("commit").GetProperty("committer").GetProperty("date").GetString();
            date = DateTimeOffset.TryParse(dateText, out var parsedDate) ? parsedDate : DateTimeOffset.MinValue;
            message = root.GetProperty("commit").GetProperty("message").GetString()?.Split('\n')[0] ?? string.Empty;
        }
        catch (Exception restException)
        {
            try
            {
                (sha, date, message) = await FetchFromAtomAsync(source);
            }
            catch (Exception atomException)
            {
                throw new InvalidOperationException(
                    $"GitHub REST API and Atom feed both failed for {source.Repository}. REST: {restException.Message} Atom: {atomException.Message}",
                    atomException);
            }
        }

        var state = LoadState(source);
        return new SourceUpdateCheck(source, sha, date, message, state?.CommitSha != sha, state is not null);
    }

    public async Task<string[]> GetChangedDependencyFilesAsync(GitHubUpdateSource source)
    {
        var changed = new List<string>();
        foreach (var relativePath in source.DependencyFiles)
        {
            try
            {
                var remote = await client.GetByteArrayAsync($"https://raw.githubusercontent.com/{source.Repository}/{source.Branch}/{relativePath}");
                var localPath = Path.Combine(source.DeploymentRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
                var local = File.Exists(localPath) ? await File.ReadAllBytesAsync(localPath) : null;
                if (local is null || !remote.SequenceEqual(local))
                {
                    changed.Add(relativePath);
                }
            }
            catch
            {
                // Optional dependency files do not block the preview.
            }
        }
        return changed.ToArray();
    }

    private async Task<(string Sha, DateTimeOffset Date, string Message)> FetchFromAtomAsync(GitHubUpdateSource source)
    {
        using var response = await client.GetAsync($"https://github.com/{source.Repository}/commits/{source.Branch}.atom");
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync();
        var feed = await XDocument.LoadAsync(stream, LoadOptions.None, CancellationToken.None);
        XNamespace atom = "http://www.w3.org/2005/Atom";
        var entry = feed.Root?.Element(atom + "entry") ?? throw new InvalidDataException("GitHub Atom feed did not contain a commit entry.");
        var sha = entry.Element(atom + "id")?.Value.Split('/').LastOrDefault();
        if (string.IsNullOrWhiteSpace(sha))
        {
            throw new InvalidDataException("GitHub Atom feed did not contain a commit SHA.");
        }
        var updatedText = entry.Element(atom + "updated")?.Value;
        var date = DateTimeOffset.TryParse(updatedText, out var parsedDate) ? parsedDate : DateTimeOffset.MinValue;
        var message = entry.Element(atom + "title")?.Value.Trim() ?? string.Empty;
        return (sha, date, message);
    }
}
