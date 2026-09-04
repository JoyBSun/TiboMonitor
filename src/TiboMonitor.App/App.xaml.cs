using System.IO;
using System.Net.Http;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using TiboMonitor.Core.Configuration;
using TiboMonitor.Core.Services;
using MessageBox = System.Windows.MessageBox;

namespace TiboMonitor.App;

public partial class App : System.Windows.Application
{
    private Mutex? _singleInstanceMutex;
    private MonitorCoordinator? _coordinator;
    private TrayIconService? _trayIcon;
    private RollingFileLogger? _logger;
    private MainWindow? _mainWindow;
    private SettingsWindow? _settingsWindow;
    private MonitorOptions? _options;
    private AutoStartService? _autoStart;
    private string? _configPath;
    private string? _appDataRoot;
    private bool _ownsMutex;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _singleInstanceMutex = new Mutex(true, "Local\\TiboMonitor-thsottiaux", out var createdNew);
        _ownsMutex = createdNew;
        if (!createdNew)
        {
            MessageBox.Show("Tibo Monitor 已经在运行，请查看系统托盘。", "Tibo Monitor",
                MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        var dataRootOverride = Environment.GetEnvironmentVariable("TIBO_DATA_ROOT");
        var appDataRoot = string.IsNullOrWhiteSpace(dataRootOverride)
            ? Path.Combine(AppContext.BaseDirectory, "UserData")
            : Path.GetFullPath(Environment.ExpandEnvironmentVariables(dataRootOverride));
        _appDataRoot = appDataRoot;
        var logPath = Path.Combine(appDataRoot, "Logs", "tibo-monitor.log");
        _logger = new RollingFileLogger(logPath, 2_000_000, 3);

        MonitorOptions options;
        var configPath = Path.Combine(AppContext.BaseDirectory, "config.json");
        _configPath = configPath;
        try
        {
            options = ConfigLoader.Load(configPath);
        }
        catch (Exception exception)
        {
            _logger.Error("配置文件读取失败，已使用安全默认值；远程检查暂时停用。", exception);
            options = new MonitorOptions { AutoStart = false };
            MessageBox.Show(
                $"config.json 无法读取，程序将继续运行，但远程检查暂时停用。\n\n{exception.Message}",
                "Tibo Monitor 配置错误",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        _options = options;

        _logger = new RollingFileLogger(logPath, options.MaxLogBytes, options.LogFileCount);
        _logger.Info("Startup");

        DispatcherUnhandledException += OnDispatcherUnhandledException;

        var stateStore = new StateStore(Path.Combine(appDataRoot, "Data", "state.json"), _logger);
        var httpClient = new HttpClient(new HttpClientHandler { UseProxy = false })
        {
            Timeout = TimeSpan.FromSeconds(options.HttpTimeoutSeconds)
        };
        var feedClient = new RemoteFeedClient(httpClient);
        _coordinator = new MonitorCoordinator(options, stateStore, feedClient, _logger, httpClient);
        _mainWindow = new MainWindow(_coordinator, options, _logger);
        MainWindow = _mainWindow;

        _autoStart = new AutoStartService(_logger);
        if (options.AutoStart && !_autoStart.IsEnabled())
        {
            _autoStart.SetEnabled(true);
        }

        _trayIcon = new TrayIconService(
            showWindow: () => _mainWindow.ShowReminder(activate: true),
            checkNow: async () => await _coordinator.CheckNowAsync(manual: true),
            showSettings: ShowSettings,
            showLogs: () => TrayIconService.OpenFolder(Path.GetDirectoryName(logPath)!),
            exit: ExitApplication,
            _autoStart,
            _logger);
        _trayIcon.SetMonitoringEnabled(options.MonitoringEnabled);

        _coordinator.UnreadChanged += OnUnreadChanged;
        _coordinator.StatusChanged += status =>
            Dispatcher.InvokeAsync(() => _trayIcon?.SetStatus(status));
        _coordinator.Start();
        await _coordinator.InitializeAsync();
        if (e.Args.Any(argument => string.Equals(argument, "--settings", StringComparison.OrdinalIgnoreCase)))
        {
            ShowSettings();
        }
    }

    private void ShowSettings()
    {
        Dispatcher.Invoke(() =>
        {
            if (_settingsWindow is { IsVisible: true })
            {
                _settingsWindow.Activate();
                return;
            }

            if (_options is null || _configPath is null || _appDataRoot is null ||
                _autoStart is null || _logger is null)
            {
                return;
            }

            _settingsWindow = new SettingsWindow(
                _options,
                _configPath,
                _appDataRoot,
                _autoStart,
                _logger,
                ApplySettings);
            if (_mainWindow is { IsVisible: true })
            {
                _settingsWindow.Owner = _mainWindow;
            }

            _settingsWindow.Closed += (_, _) => _settingsWindow = null;
            _settingsWindow.Show();
            _settingsWindow.Activate();
        });
    }

    private void ApplySettings(bool checkImmediately)
    {
        if (_options is null)
        {
            return;
        }

        _mainWindow?.ApplyOptions(_options);
        _coordinator?.RestartPolling();
        _trayIcon?.SetMonitoringEnabled(_options.MonitoringEnabled);
        _trayIcon?.SetAutoStartChecked(_autoStart?.IsEnabled() == true);
        if (checkImmediately && _coordinator is not null)
        {
            _ = _coordinator.CheckNowAsync(manual: false);
        }
    }

    private void OnUnreadChanged(int unreadCount, bool shouldShow)
    {
        Dispatcher.InvokeAsync(async () =>
        {
            _trayIcon?.SetUnreadCount(unreadCount);
            if (_mainWindow is null)
            {
                return;
            }

            await _mainWindow.RefreshAsync();
            if (shouldShow && unreadCount > 0)
            {
                _mainWindow.ShowReminder(activate: true);
            }
        });
    }

    private void ExitApplication()
    {
        if (_coordinator?.UnreadCount > 0)
        {
            var result = MessageBox.Show(
                $"仍有 {_coordinator.UnreadCount} 条未读消息。退出不会标记已读，下次启动还会重新显示。\n\n确定退出吗？",
                "退出 Tibo Monitor",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            if (result != MessageBoxResult.Yes)
            {
                return;
            }
        }

        _mainWindow?.ForceClose();
        Shutdown();
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        _logger?.Error("未处理的界面异常，程序将继续运行。", e.Exception);
        e.Handled = true;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _coordinator?.Dispose();
        _trayIcon?.Dispose();
        if (_ownsMutex)
        {
            _singleInstanceMutex?.ReleaseMutex();
        }
        _singleInstanceMutex?.Dispose();
        _logger?.Info("Exit");
        base.OnExit(e);
    }
}
