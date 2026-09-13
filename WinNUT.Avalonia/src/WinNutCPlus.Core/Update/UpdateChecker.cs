using Octokit;
using WinNutCPlus.Core.Logging;

namespace WinNutCPlus.Core.Update;

public sealed class UpdateCheckResult
{
    public Release? LatestRelease { get; init; }
    public ReleaseAsset? LatestReleaseAsset { get; init; }
    public Exception? Error { get; init; }
}

public sealed class UpdateDownloadProgress
{
    public long BytesDownloaded { get; init; }
    public long TotalBytes { get; init; }
}

/// <summary>
/// Checks RikoDEV/WinNutCPlus GitHub releases for updates and downloads the installer asset.
/// Ported from UpdateUtil.vb, with the progress-throttle bug fixed (the original discarded the
/// result of Date.AddTicks, a value type, so the intended 500ms throttle never actually worked).
/// </summary>
public sealed class UpdateChecker
{
    private const string RepositoryOwner = "RikoDEV";
    private const string RepositoryName = "WinNutCPlus";
    private static readonly TimeSpan ProgressUpdateDelay = TimeSpan.FromMilliseconds(500);

    private readonly Logger _logFile;
    private readonly GitHubClient _client = new(new ProductHeaderValue(RepositoryOwner));

    public event Action<UpdateCheckResult>? UpdateCheckCompleted;
    public event Action<UpdateDownloadProgress>? UpdateDownloadProgressChanged;
    public event Action<string>? UpdateDownloadCompleted;

    public UpdateChecker(Logger logFile)
    {
        _logFile = logFile;
    }

    public async Task BeginUpdateCheckAsync(bool acceptPreRelease)
    {
        try
        {
            var releases = await _client.Repository.Release.GetAll(RepositoryOwner, RepositoryName).ConfigureAwait(false);
            var match = releases.FirstOrDefault(r => acceptPreRelease ? r.Prerelease : !r.Prerelease);

            if (match is null)
            {
                UpdateCheckCompleted?.Invoke(new UpdateCheckResult());
                return;
            }

            var asset = match.Assets.FirstOrDefault(a => a.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase));

            UpdateCheckCompleted?.Invoke(new UpdateCheckResult { LatestRelease = match, LatestReleaseAsset = asset });
        }
        catch (Exception ex)
        {
            _logFile.LogException(ex, this);
            UpdateCheckCompleted?.Invoke(new UpdateCheckResult { Error = ex });
        }
    }

    public async Task DownloadUpdateAsync(ReleaseAsset asset, CancellationToken cancellationToken = default)
    {
        var destinationPath = Path.Combine(Path.GetTempPath(), asset.Name);

        if (File.Exists(destinationPath) && new FileInfo(destinationPath).Length == asset.Size)
        {
            UpdateDownloadCompleted?.Invoke(destinationPath);
            return;
        }

        using var httpClient = new HttpClient();
        using var response = await httpClient.GetAsync(asset.BrowserDownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        await using var sourceStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);

        var buffer = new byte[4096];
        long totalRead = 0;
        var nextProgressUpdate = DateTime.UtcNow;
        int bytesRead;

        // Written in its own scope so the exclusive file lock is released (and the write
        // flushed to disk) before UpdateDownloadCompleted fires below — subscribers launch this
        // file via ShellExecute, which fails with a sharing violation if it's still open here.
        await using (var destinationStream = new FileStream(destinationPath, System.IO.FileMode.Create, FileAccess.Write, FileShare.None))
        {
            while ((bytesRead = await sourceStream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
            {
                await destinationStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken).ConfigureAwait(false);
                totalRead += bytesRead;

                var now = DateTime.UtcNow;
                if (now >= nextProgressUpdate)
                {
                    UpdateDownloadProgressChanged?.Invoke(new UpdateDownloadProgress { BytesDownloaded = totalRead, TotalBytes = asset.Size });
                    nextProgressUpdate = now + ProgressUpdateDelay;
                }
            }
        }

        UpdateDownloadProgressChanged?.Invoke(new UpdateDownloadProgress { BytesDownloaded = totalRead, TotalBytes = asset.Size });
        UpdateDownloadCompleted?.Invoke(destinationPath);
    }

    /// <summary>
    /// Maps an "UP_AutoChkDelay" setting (0=Day, 1=Weekday, 2=Month) plus the last-checked
    /// timestamp to whether an auto-update check is due.
    /// </summary>
    public static bool UpdateCheckDelayPassed(int autoCheckDelay, DateTime lastChecked)
    {
        if (lastChecked == DateTime.MinValue) return true;

        var now = DateTime.Now;

        // 0=Day, 1=Weekday (VB's DateInterval.Weekday counts calendar days, same as Day, for
        // DateDiff purposes), 2=Month (calendar-month boundaries crossed, ignoring day-of-month).
        long diff = autoCheckDelay switch
        {
            0 or 1 => (long)(now.Date - lastChecked.Date).TotalDays,
            _ => (now.Year - lastChecked.Year) * 12L + (now.Month - lastChecked.Month),
        };

        return diff >= 1;
    }
}
