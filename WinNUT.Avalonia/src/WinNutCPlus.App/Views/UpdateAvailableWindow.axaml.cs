using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Media;
using WinNutCPlus.App.Services;
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

        this.FindControl<ContentControl>("ChangeLogHost")!.Content = MarkdownRenderer.Render(
            viewModel.ChangeLog, GetBrush("TextPrimaryBrush", Colors.White),
            GetBrush("ControlBgBrush", Color.Parse("#2A2B30")), GetBrush("AccentBrush", Color.Parse("#4F7CFF")));
    }

    private IBrush GetBrush(string resourceKey, Color fallback)
    {
        if (this.TryFindResource(resourceKey, out var value) && value is IBrush brush)
        {
            return brush;
        }

        return new SolidColorBrush(fallback);
    }
}
