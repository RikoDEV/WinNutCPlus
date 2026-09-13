using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Octokit;
using WinNutCPlus.Core.Update;

namespace WinNutCPlus.App.ViewModels;

/// <summary>
/// Backs the update-available dialog. Ported from UpdateAvailableForm.vb: shows the release
/// name/changelog, downloads the installer asset with a progress bar, then launches it and
/// exits (the installer handles closing/replacing the running app).
/// </summary>
public partial class UpdateAvailableViewModel : ViewModelBase
{
    private readonly UpdateChecker _updateChecker;
    private readonly Release _release;
    private readonly ReleaseAsset _asset;

    [ObservableProperty] private string _releaseName = string.Empty;
    [ObservableProperty] private string _changeLog = string.Empty;
    [ObservableProperty] private bool _isDownloading;
    [ObservableProperty] private double _progressPercent;
    [ObservableProperty] private string _progressText = string.Empty;

    public event Action<string>? DownloadCompleted;

    public UpdateAvailableViewModel() : this(null!, null!, null!) { }

    public UpdateAvailableViewModel(UpdateChecker updateChecker, Release release, ReleaseAsset asset)
    {
        _updateChecker = updateChecker;
        _release = release;
        _asset = asset;
        ReleaseName = release?.Name ?? string.Empty;
        ChangeLog = release?.Body ?? string.Empty;

        if (updateChecker is not null)
        {
            updateChecker.UpdateDownloadProgressChanged += OnProgress;
            updateChecker.UpdateDownloadCompleted += path => DownloadCompleted?.Invoke(path);
        }
    }

    private void OnProgress(UpdateDownloadProgress progress)
    {
        if (progress.TotalBytes <= 0) return;
        ProgressPercent = 100.0 * progress.BytesDownloaded / progress.TotalBytes;
        ProgressText = $"{progress.BytesDownloaded / 1024.0 / 1024.0:0.00} / {progress.TotalBytes / 1024.0 / 1024.0:0.00} MB";
    }

    [RelayCommand]
    private async Task UpdateAsync()
    {
        IsDownloading = true;
        await _updateChecker.DownloadUpdateAsync(_asset);
    }

    public string HtmlUrl => _release?.HtmlUrl ?? string.Empty;
}
