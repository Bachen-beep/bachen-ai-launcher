namespace BaChenAiLauncher;

internal sealed record GitHubUpdateSource(
    string DisplayName,
    string Repository,
    string Branch,
    string DeploymentRoot,
    string[] PreservedFiles,
    string[] DependencyFiles);

internal sealed record SourceUpdateState(string CommitSha, DateTimeOffset UpdatedAt);

internal sealed record SourceUpdateCheck(
    GitHubUpdateSource Source,
    string LatestSha,
    DateTimeOffset LatestDate,
    string LatestMessage,
    bool UpdateAvailable,
    bool HasLocalBaseline);

internal enum SourceUpdateProgressStage
{
    Downloading,
    Installing
}

internal sealed record SourceUpdateProgress(
    SourceUpdateProgressStage Stage,
    long Completed,
    long? Total);

internal sealed record UpdateBackupEntry(GitHubUpdateSource Source, string Path, DateTime LastWriteTime);

internal sealed record UpdateBackupMetadata(
    string DisplayName,
    string? PreviousCommitSha,
    DateTimeOffset CreatedAt,
    string? PreviousVersion = null,
    string[]? PreviousDependencies = null);
