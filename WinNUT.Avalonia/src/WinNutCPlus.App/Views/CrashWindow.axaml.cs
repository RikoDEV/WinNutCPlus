using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using WinNutCPlus.Core.Logging;

namespace WinNutCPlus.App.Views;

public partial class CrashWindow : Window
{
    private readonly string _report;
    private readonly string _dataDirectory;

    public CrashWindow()
    {
        InitializeComponent();
        _report = string.Empty;
        _dataDirectory = string.Empty;
    }

    public CrashWindow(string report, string dataDirectory) : this()
    {
        _report = report;
        _dataDirectory = dataDirectory;
        this.FindControl<TextBox>("ReportBox")!.Text = report;

        this.FindControl<Button>("CopyButton")!.Click += async (_, _) =>
        {
            if (Clipboard is not null) await Clipboard.SetTextAsync(_report);
        };

        this.FindControl<Button>("SaveButton")!.Click += (_, _) =>
        {
            var path = CrashReporter.WriteReportToFile(_report, _dataDirectory);
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
        };

        this.FindControl<Button>("CloseButton")!.Click += (_, _) => Environment.Exit(1);
    }
}
