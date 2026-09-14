using System.Runtime.InteropServices;
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
        Opened += (_, _) => AlertUser();
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

    /// <summary>
    /// This is a safety-critical warning that can appear while the user is doing something else
    /// entirely, so it flashes the taskbar icon (matching Windows' own "urgent attention" pattern
    /// — keeps flashing until the window is brought to the foreground, rather than a fixed
    /// count) and plays the system exclamation sound once, on top of already being Topmost.
    /// </summary>
    private void AlertUser()
    {
        try
        {
            MessageBeep(MB_ICONEXCLAMATION);

            if (TryGetPlatformHandle() is { } handle)
            {
                var info = new FLASHWINFO
                {
                    cbSize = (uint)Marshal.SizeOf<FLASHWINFO>(),
                    hwnd = handle.Handle,
                    dwFlags = FLASHW_ALL | FLASHW_TIMERNOFG,
                    uCount = uint.MaxValue,
                    dwTimeout = 0,
                };
                FlashWindowEx(ref info);
            }
        }
        catch
        {
            // Best-effort; the dialog itself (Topmost, ShowInTaskbar) is the real alert.
        }
    }

    private const uint MB_ICONEXCLAMATION = 0x30;
    private const uint FLASHW_ALL = 0x3;
    private const uint FLASHW_TIMERNOFG = 0xC;

    [DllImport("user32.dll")]
    private static extern bool MessageBeep(uint uType);

    [DllImport("user32.dll")]
    private static extern bool FlashWindowEx(ref FLASHWINFO pwfi);

    [StructLayout(LayoutKind.Sequential)]
    private struct FLASHWINFO
    {
        public uint cbSize;
        public IntPtr hwnd;
        public uint dwFlags;
        public uint uCount;
        public uint dwTimeout;
    }
}
