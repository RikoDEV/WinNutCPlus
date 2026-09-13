using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WinNUT.App.Models;
using WinNUT.Core.Device;

namespace WinNUT.App.ViewModels;

/// <summary>
/// Backs the UPS variable browser window. Ported from List_Var_Gui.vb: builds a tree of dotted
/// variable names, live-refreshes the value of whichever leaf is currently selected, and offers
/// copy-to-clipboard/save-to-file of the full variable dump.
/// </summary>
public partial class ListVarViewModel : ViewModelBase, IDisposable
{
    private readonly UpsDevice _device;
    private readonly DispatcherTimer _refreshTimer;

    public ObservableCollection<VarTreeNode> Roots { get; } = new();

    [ObservableProperty] private VarTreeNode? _selectedNode;
    [ObservableProperty] private string _nameText = string.Empty;
    [ObservableProperty] private string _valueText = string.Empty;
    [ObservableProperty] private string _descText = string.Empty;
    [ObservableProperty] private bool _isLoading;

    public event Func<Task>? RequestSaveFile;
    public event Action<string>? RequestClipboardCopy;

    public ListVarViewModel() : this(null!) { }

    public ListVarViewModel(UpsDevice device)
    {
        _device = device;
        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _refreshTimer.Tick += async (_, _) => await RefreshSelectedValueAsync();
    }

    public async Task LoadAsync()
    {
        IsLoading = true;
        Roots.Clear();

        try
        {
            // Pause data polling while doing the bulk fetch, matching the original's behavior
            // of not letting the periodic poll interleave with a full LIST VAR traversal.
            _device.SetUpdatingData(false);
            var entries = await _device.GetUpsListVarAsync().ConfigureAwait(true);

            var root = new VarTreeNode(_device.Name, _device.Name);

            foreach (var entry in entries)
            {
                if (entry.VarKey == "UPSNAME") continue;

                var segments = entry.VarKey.Split('.');
                var current = root;
                var path = string.Empty;

                foreach (var segment in segments)
                {
                    path = path.Length == 0 ? segment : path + "." + segment;
                    var existing = current.Children.FirstOrDefault(c => c.FullPath == path);
                    if (existing is null)
                    {
                        existing = new VarTreeNode(segment, path);
                        current.Children.Add(existing);
                    }

                    current = existing;
                }

                current.Entry = entry;
            }

            Roots.Add(root);
        }
        finally
        {
            _device.SetUpdatingData(true);
            IsLoading = false;
        }

        _refreshTimer.Start();
    }

    partial void OnSelectedNodeChanged(VarTreeNode? value)
    {
        if (value?.Entry is null)
        {
            NameText = string.Empty;
            ValueText = string.Empty;
            DescText = string.Empty;
            return;
        }

        NameText = value.Entry.VarKey;
        ValueText = value.Entry.VarValue;
        DescText = value.Entry.VarDesc;
    }

    private async Task RefreshSelectedValueAsync()
    {
        if (SelectedNode?.Entry is null || !SelectedNode.IsLeaf) return;

        try
        {
            var value = await _device.GetUpsVarAsync(SelectedNode.Entry.VarKey).ConfigureAwait(true);
            ValueText = value;
        }
        catch
        {
            // Best-effort live refresh; ignore transient errors here.
        }
    }

    [RelayCommand]
    private async Task ReloadAsync() => await LoadAsync();

    [RelayCommand]
    private void CopyToClipboard() => RequestClipboardCopy?.Invoke(SerializeAll());

    [RelayCommand]
    private async Task SaveToFileAsync()
    {
        if (RequestSaveFile is not null) await RequestSaveFile.Invoke();
    }

    public string SerializeAll()
    {
        var lines = new List<string> { $"UPS: {_device.Name}" };
        if (_device.UpsData is not null)
        {
            lines.Add($"Manufacturer: {_device.UpsData.Mfr}");
            lines.Add($"Model: {_device.UpsData.Model}");
            lines.Add($"Firmware: {_device.UpsData.Firmware}");
        }

        void Walk(VarTreeNode node)
        {
            if (node.Entry is not null)
            {
                lines.Add($"{node.Entry.VarKey} ({node.Entry.VarDesc}): {node.Entry.VarValue}");
            }

            foreach (var child in node.Children) Walk(child);
        }

        foreach (var root in Roots) Walk(root);

        return string.Join(Environment.NewLine, lines);
    }

    public void Dispose()
    {
        _refreshTimer.Stop();
    }
}
