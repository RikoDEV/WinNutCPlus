using System.Threading.Tasks;
using Avalonia.Controls;
using WinNutCPlus.App.ViewModels;

namespace WinNutCPlus.App.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (DataContext is MainWindowViewModel vm)
            {
                vm.PreferencesRequested += OnPreferencesRequested;
                vm.AboutRequested += () => ShowOwned(new AboutWindow());
                vm.ListVarRequested += OnListVarRequested;
            }
        };
    }

    /// <summary>
    /// Shows a window owned by this one — except this window can be started minimized to tray
    /// (see App.OnFrameworkInitializationCompleted) and never actually shown at all, in which
    /// case Avalonia's owned Show()/ShowDialog() throw ("Cannot show window with non-visible
    /// owner") since an invisible owner can't sensibly parent/center another window. Fall back to
    /// showing standalone (no owner) in that case — these are all reachable from the tray menu
    /// without the main window ever having been opened.
    /// </summary>
    private void ShowOwned(Window window)
    {
        if (IsVisible)
        {
            window.ShowDialog(this);
        }
        else
        {
            window.Show();
        }
    }

    private async void OnPreferencesRequested()
    {
        if (DataContext is not MainWindowViewModel mainVm) return;

        var prefsVm = new PreferencesViewModel(mainVm.Host);
        var window = new PreferencesWindow(prefsVm);

        var saved = false;
        prefsVm.Saved += () => saved = true;

        if (IsVisible)
        {
            await window.ShowDialog(this);
        }
        else
        {
            var tcs = new TaskCompletionSource();
            window.Closed += (_, _) => tcs.TrySetResult();
            window.Show();
            await tcs.Task;
        }

        if (saved)
        {
            await mainVm.ReapplyConnectionSettingsAsync();
        }
    }

    private void OnListVarRequested()
    {
        if (DataContext is not MainWindowViewModel mainVm || mainVm.CurrentDevice is null) return;

        var window = new ListVarWindow(new ViewModels.ListVarViewModel(mainVm.CurrentDevice));

        if (IsVisible)
        {
            window.Show(this);
        }
        else
        {
            window.Show();
        }
    }
}
