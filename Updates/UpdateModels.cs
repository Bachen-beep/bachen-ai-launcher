using System.Diagnostics;

namespace BaChenAiLauncher;

internal sealed class TransferRateTracker
{
    private readonly Stopwatch _stopwatch;
    private readonly Func<TimeSpan> _elapsedProvider;
    private long _lastBytes;
    private TimeSpan _lastSampleAt;

    public TransferRateTracker(Func<TimeSpan>? elapsedProvider = null)
    {
        _stopwatch = Stopwatch.StartNew();
        _elapsedProvider = elapsedProvider ?? (() => _stopwatch.Elapsed);
    }

    public double? Sample(long completedBytes)
    {
        var sampledAt = _elapsedProvider();
        var elapsedSeconds = (sampledAt - _lastSampleAt).TotalSeconds;
        var bytesPerSecond = elapsedSeconds > 0
            ? (completedBytes - _lastBytes) / elapsedSeconds
            : (double?)null;
        _lastBytes = completedBytes;
        _lastSampleAt = sampledAt;
        return bytesPerSecond is > 0 ? bytesPerSecond : null;
    }
}

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
    long? Total,
    double? BytesPerSecond = null);

internal sealed record UpdateBackupEntry(GitHubUpdateSource Source, string Path, DateTime LastWriteTime);

internal sealed record UpdateBackupMetadata(
    string DisplayName,
    string? PreviousCommitSha,
    DateTimeOffset CreatedAt,
    string? PreviousVersion = null,
    string[]? PreviousDependencies = null);
