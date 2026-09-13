using Avalonia.Controls;
using WinNUT.App.ViewModels;

namespace WinNUT.App.Views;

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
                vm.AboutRequested += () => new AboutWindow().ShowDialog(this);
                vm.ListVarRequested += OnListVarRequested;
            }
        };
    }

    private async void OnPreferencesRequested()
    {
        if (DataContext is not MainWindowViewModel mainVm) return;

        var prefsVm = new PreferencesViewModel(mainVm.Host);
        var window = new PreferencesWindow(prefsVm);

        var saved = false;
        prefsVm.Saved += () => saved = true;

        await window.ShowDialog(this);

        if (saved)
        {
            await mainVm.ReapplyConnectionSettingsAsync();
        }
    }

    private void OnListVarRequested()
    {
        if (DataContext is not MainWindowViewModel mainVm || mainVm.CurrentDevice is null) return;

        var window = new ListVarWindow(new ViewModels.ListVarViewModel(mainVm.CurrentDevice));
        window.Show(this);
    }
}
