using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using Avalonia.Threading;
using WinNutCPlus.App.Localization;
using WinNutCPlus.App.Services;
using WinNutCPlus.App.ViewModels;
using WinNutCPlus.App.Views;
using WinNutCPlus.Core.Update;

namespace WinNutCPlus.App;

public partial class App : Application
{
    private AppHost? _host;
    private TrayIcon? _trayIcon;
    private ShutdownWindow? _shutdownWindow;
    private MainWindow? _mainWindow;

    /// <summary>Called from a background pipe listener when another launch attempt requests activation.</summary>
    public static void RequestForegroundActivation()
    {
        if (Current is not App app || app._mainWindow is null) return;

        Dispatcher.UIThread.Post(() =>
        {
            app._mainWindow.Show();
            app._mainWindow.WindowState = WindowState.Normal;
            app._mainWindow.Activate();
            if (app._trayIcon is not null) app._trayIcon.IsVisible = false;
        });
    }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var args = desktop.Args ?? Array.Empty<string>();
            _host = new AppHost(args);

            RequestedThemeVariant = _host.Settings.LG_Theme switch
            {
                1 => ThemeVariant.Light,
                2 => ThemeVariant.Dark,
                _ => ThemeVariant.Default,
            };

            AppDomain.CurrentDomain.UnhandledException += (_, e) => HandleCrash(e.ExceptionObject as Exception);

            var mainViewModel = new MainWindowViewModel(_host);
            var mainWindow = new MainWindow { DataContext = mainViewModel };
            _mainWindow = mainWindow;
            desktop.MainWindow = mainWindow;

            SetupTrayIcon(mainWindow, mainViewModel);
            SetupShutdownCoordinator(mainViewModel);
            SetupUpdateChecker(mainViewModel);

            if (_host.Settings.MinimizeOnStart && _host.Settings.MinimizeToTray)
            {
                mainWindow.Opened += (_, _) =>
                {
                    mainWindow.Hide();
                    _trayIcon!.IsVisible = true;
                };
            }

            desktop.ShutdownRequested += async (_, _) =>
            {
                if (mainViewModel.IsConnected)
                {
                    await mainViewModel.DisconnectCommand.ExecuteAsync(null);
                }

                _host.Dispose();
            };

            if (_host.ImportedLegacySettings is { } imported)
            {
                ToastService.Send("WinNutCPlus", Localize.Format("ToastImportedSettings", imported.FieldsImported));
            }

            if (_host.Settings.NUT_AutoReconnect)
            {
                Dispatcher.UIThread.Post(async () => await mainViewModel.ConnectCommand.ExecuteAsync(null));
            }
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void HandleCrash(Exception? exception)
    {
        if (_host is null || exception is null) return;

        _host.LogFile.LogTracing("Unhandled exception encountered.", Core.Models.LogLevel.Error, this);
        _host.LogFile.LogException(exception, this);

        var report = WinNutCPlus.Core.Logging.CrashReporter.BuildReport(
            exception, _host.LogFile.LastEvents, _host.Settings, "0.1.0-dev");

        var window = new CrashWindow(report, _host.DataDirectory);
        window.Show();
    }

    private bool _manualUpdateCheckPending;

    private void SetupUpdateChecker(MainWindowViewModel mainViewModel)
    {
        var checker = _host!.UpdateChecker;

        checker.UpdateCheckCompleted += result =>
        {
            _host.Settings.UP_LastCheck = DateTime.Now;
            _host.SettingsStore.Save();

            var wasManual = _manualUpdateCheckPending;
            _manualUpdateCheckPending = false;

            if (result.Error is not null)
            {
                _host.LogFile.LogTracing("No update found or update check failed.", Core.Models.LogLevel.Notice, this);
                if (wasManual) ToastService.Send("WinNutCPlus", Localize.Get("UpdateCheckFailed"));
                return;
            }

            if (result.LatestRelease is null || !IsNewerVersion(result.LatestRelease.TagName))
            {
                _host.LogFile.LogTracing("No newer version available.", Core.Models.LogLevel.Notice, this);
                if (wasManual) ToastService.Send("WinNutCPlus", Localize.Get("UpdateNoneAvailable"));
                return;
            }

            if (result.LatestReleaseAsset is null)
            {
                _host.LogFile.LogTracing("Latest release has no installer asset.", Core.Models.LogLevel.Error, this);
                if (wasManual) ToastService.Send("WinNutCPlus", Localize.Get("UpdateCheckFailed"));
                return;
            }

            Dispatcher.UIThread.Post(() =>
            {
                var vm = new UpdateAvailableViewModel(checker, result.LatestRelease, result.LatestReleaseAsset);
                new UpdateAvailableWindow(vm).Show();
            });
        };

        mainViewModel.CheckForUpdatesRequested += async () =>
        {
            _manualUpdateCheckPending = true;
            await checker.BeginUpdateCheckAsync(_host.Settings.UP_Branch == 1);
        };

        if (_host.Settings.UP_CheckAtStart &&
            UpdateChecker.UpdateCheckDelayPassed(_host.Settings.UP_AutoChkDelay, _host.Settings.UP_LastCheck))
        {
            Dispatcher.UIThread.Post(async () => await checker.BeginUpdateCheckAsync(_host.Settings.UP_Branch == 1));
        }
    }

    private static bool IsNewerVersion(string tagName)
    {
        try
        {
            var latest = new Version(tagName.TrimStart('v', 'V'));
            var current = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 1, 0);
            return latest > current;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private void SetupShutdownCoordinator(MainWindowViewModel mainViewModel)
    {
        var coordinator = _host!.ShutdownCoordinator;

        coordinator.ShutdownRequested += async () =>
        {
            if (_host.Settings.PW_Immediate)
            {
                await coordinator.ExecuteShutdownActionAsync().ConfigureAwait(false);
                return;
            }

            var vm = new ShutdownViewModel(_host, coordinator);
            _shutdownWindow = new ShutdownWindow(vm);
            vm.Start(mainViewModel.BatteryCharge, mainViewModel.BatteryRuntimeText);
            _shutdownWindow.Show();
        };

        coordinator.ShutdownCanceled += () =>
        {
            if (_shutdownWindow is null) return;
            _shutdownWindow.AllowClose();
            _shutdownWindow.Close();
            _shutdownWindow = null;
        };
    }

    /// <summary>
    /// Builds a live tray tooltip reflecting current connection/battery state (e.g.
    /// "WinNutCPlus — On battery (45%) — 00:12:30"), matching the original app's dynamic
    /// NotifyIcon.Text. Truncated to 63 chars — the classic Windows tray tooltip limit the
    /// original app defensively respected (Event_UpdateNotifyIconStr in WinNUT.vb).
    /// </summary>
    private void UpdateTrayTooltip(MainWindowViewModel viewModel)
    {
        if (_trayIcon is null) return;

        var text = $"WinNutCPlus — {viewModel.StatusText}";

        if (viewModel.IsConnected)
        {
            text += $" ({viewModel.BatteryCharge:0}%)";
            if (viewModel.BatteryRuntimeText is not ("-" or "unavailable"))
            {
                text += $" — {viewModel.BatteryRuntimeText}";
            }
        }

        if (text.Length > 63)
        {
            text = text[..60] + "...";
        }

        _trayIcon.ToolTipText = text;
    }

    private void SetupTrayIcon(MainWindow mainWindow, MainWindowViewModel viewModel)
    {
        _trayIcon = new TrayIcon
        {
            Icon = IconProvider.BaseAppIcon,
            ToolTipText = "WinNutCPlus",
            IsVisible = false,
        };

        mainWindow.Icon = IconProvider.BaseAppIcon;

        var showItem = new NativeMenuItem(Localize.Get("MainShowWindow"));
        showItem.Click += (_, _) =>
        {
            mainWindow.Show();
            mainWindow.WindowState = WindowState.Normal;
            mainWindow.Activate();
            _trayIcon!.IsVisible = false;
        };

        var connectItem = new NativeMenuItem(Localize.Get("MenuConnect"));
        connectItem.Click += async (_, _) => await viewModel.ConnectCommand.ExecuteAsync(null);

        var disconnectItem = new NativeMenuItem(Localize.Get("MenuDisconnect"));
        disconnectItem.Click += async (_, _) => await viewModel.DisconnectCommand.ExecuteAsync(null);

        void SyncTrayState()
        {
            var icon = IconProvider.GetIcon(viewModel.IconIndex);
            _trayIcon!.Icon = icon;
            mainWindow.Icon = icon;
            UpdateTrayTooltip(viewModel);
            connectItem.IsVisible = !viewModel.IsConnected;
            disconnectItem.IsVisible = viewModel.IsConnected;
        }

        viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(MainWindowViewModel.IconIndex)
                or nameof(MainWindowViewModel.StatusText)
                or nameof(MainWindowViewModel.BatteryCharge)
                or nameof(MainWindowViewModel.BatteryRuntimeText)
                or nameof(MainWindowViewModel.IsConnected))
            {
                SyncTrayState();
            }
        };
        SyncTrayState();

        var settingsItem = new NativeMenuItem(Localize.Get("MenuSettings"));
        settingsItem.Click += (_, _) => viewModel.OpenPreferencesCommand.Execute(null);

        var updateItem = new NativeMenuItem(Localize.Get("MenuCheckUpdates"));
        updateItem.Click += (_, _) => viewModel.CheckForUpdatesCommand.Execute(null);

        var exitItem = new NativeMenuItem(Localize.Get("MenuExit"));
        exitItem.Click += (_, _) =>
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.Shutdown();
            }
        };

        _trayIcon.Menu = new NativeMenu
        {
            Items =
            {
                showItem,
                new NativeMenuItemSeparator(),
                connectItem,
                disconnectItem,
                new NativeMenuItemSeparator(),
                settingsItem,
                updateItem,
                new NativeMenuItemSeparator(),
                exitItem,
            },
        };

        _trayIcon.Clicked += (_, _) =>
        {
            mainWindow.Show();
            mainWindow.WindowState = WindowState.Normal;
            mainWindow.Activate();
            _trayIcon!.IsVisible = false;
        };

        mainWindow.PropertyChanged += (_, e) =>
        {
            if (e.Property == Window.WindowStateProperty && mainWindow.WindowState == WindowState.Minimized
                && _host!.Settings.MinimizeToTray)
            {
                mainWindow.Hide();
                _trayIcon!.IsVisible = true;
            }
        };

        mainWindow.Closing += (_, e) =>
        {
            if (_host!.Settings.CloseToTray && _host.Settings.MinimizeToTray)
            {
                e.Cancel = true;
                mainWindow.Hide();
                _trayIcon!.IsVisible = true;
            }
        };
    }
}
