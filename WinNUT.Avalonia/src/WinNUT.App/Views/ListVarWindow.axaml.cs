using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Platform.Storage;
using WinNUT.App.ViewModels;

namespace WinNUT.App.Views;

public partial class ListVarWindow : Window
{
    public ListVarWindow()
    {
        InitializeComponent();
    }

    public ListVarWindow(ListVarViewModel viewModel) : this()
    {
        DataContext = viewModel;

        viewModel.RequestClipboardCopy += async text =>
        {
            if (Clipboard is not null) await Clipboard.SetTextAsync(text);
        };

        viewModel.RequestSaveFile += async () =>
        {
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                SuggestedFileName = "winnut-vars.txt",
                FileTypeChoices = new[] { new FilePickerFileType("Text files") { Patterns = new[] { "*.txt" } } },
            });

            if (file is null) return;

            await using var stream = await file.OpenWriteAsync();
            await using var writer = new StreamWriter(stream);
            await writer.WriteAsync(viewModel.SerializeAll());
        };

        Opened += async (_, _) => await viewModel.LoadAsync();
        Closed += (_, _) => viewModel.Dispose();
    }
}
