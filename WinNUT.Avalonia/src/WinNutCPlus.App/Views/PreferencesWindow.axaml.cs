using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using WinNutCPlus.App.ViewModels;

namespace WinNutCPlus.App.Views;

public partial class PreferencesWindow : Window
{
    public PreferencesWindow()
    {
        InitializeComponent();
    }

    public PreferencesWindow(PreferencesViewModel viewModel) : this()
    {
        DataContext = viewModel;
        viewModel.CloseRequested += Close;
        viewModel.RestartRequested += RestartApplication;
    }

    /// <summary>Relaunches the app as a fresh process and exits this one — the only reliable way
    /// to apply a language change, since most localized text resolves once at window-construction
    /// time and simply relaunching the .exe while this instance is still running (e.g. minimized
    /// to tray) wouldn't start a new process at all (single-instance mutex just reactivates this one).</summary>
    private static void RestartApplication()
    {
        try
        {
            if (Environment.ProcessPath is { } path)
            {
                Process.Start(path);
            }
        }
        catch
        {
            // Best-effort; if relaunch fails the user can still start the app manually.
        }

        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Shutdown();
        }
    }
}
