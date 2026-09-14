using System.Diagnostics;
using System.Reflection;
using Avalonia.Controls;

namespace WinNutCPlus.App.Views;

public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();

        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.1.0-dev";
        this.FindControl<TextBlock>("VersionText")!.Text = "Version " + version;

        this.FindControl<Button>("GitHubLink")!.Click += (_, _) =>
            Process.Start(new ProcessStartInfo("https://github.com/RikoDEV/WinNutCPlus") { UseShellExecute = true });
    }
}
