using Avalonia.Controls;
using WinNutCPlus.App.ViewModels;

namespace WinNutCPlus.App.Views;

/// <summary>
/// This window deliberately cannot be dismissed via chrome/Alt-F4 while active — it's a
/// safety-critical low-battery warning. It only closes when the countdown expires, the user
/// clicks "shut down now", or the coordinator cancels the pending shutdown (UPS came back
/// online), all of which go through <see cref="AllowClose"/>.
/// </summary>
public partial class ShutdownWindow : Window
{
    private bool _allowClose;

    public ShutdownWindow()
    {
        InitializeComponent();
        Closing += OnClosing;
    }

    public ShutdownWindow(ShutdownViewModel viewModel) : this()
    {
        DataContext = viewModel;
        viewModel.CountdownExpired += () =>
        {
            AllowClose();
            Close();
        };
    }

    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (!_allowClose)
        {
            e.Cancel = true;
        }
    }

    public void AllowClose() => _allowClose = true;
}
