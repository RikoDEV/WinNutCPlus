using Avalonia.Controls;
using WinNUT.App.ViewModels;

namespace WinNUT.App.Views;

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
    }
}
