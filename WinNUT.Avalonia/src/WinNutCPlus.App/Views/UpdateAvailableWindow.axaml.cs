using System.Diagnostics;
using Avalonia.Controls;
using WinNutCPlus.App.ViewModels;

namespace WinNutCPlus.App.Views;

public partial class UpdateAvailableWindow : Window
{
    public UpdateAvailableWindow()
    {
        InitializeComponent();
    }

    public UpdateAvailableWindow(UpdateAvailableViewModel viewModel) : this()
    {
        DataContext = viewModel;

        this.FindControl<Button>("VisitPageButton")!.Click += (_, _) =>
            Process.Start(new ProcessStartInfo(viewModel.HtmlUrl) { UseShellExecute = true });

        viewModel.DownloadCompleted += path =>
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            Environment.Exit(0);
        };
    }
}
