using System.IO;
using System.Windows;
using TiboMonitor.Core.Configuration;
using TiboMonitor.Core.Services;
using MessageBox = System.Windows.MessageBox;

namespace TiboMonitor.App;

public partial class SettingsWindow : Window
{
    private readonly MonitorOptions _options;
    private readonly string _configPath;
    private readonly string _dataRoot;
    private readonly AutoStartService _autoStart;
    private readonly RollingFileLogger _logger;
    private readonly Action _onSaved;

    public SettingsWindow(
        MonitorOptions options,
        string configPath,
        string dataRoot,
        AutoStartService autoStart,
        RollingFileLogger logger,
        Action onSaved)
    {
        InitializeComponent();
        _options = options;
        _configPath = configPath;
        _dataRoot = dataRoot;
        _autoStart = autoStart;
        _logger = logger;
        _onSaved = onSaved;

        AccountText.Text = $"@{options.Account}";
        SourceText.Text = options.FeedMode == "direct" ? "公开实时源（本机直连）" : "自定义远程 JSON Feed";
        DataPathText.Text = dataRoot;
        IntervalMinutesTextBox.Text = Math.Max(
            MonitorOptions.MinimumPollingIntervalSeconds / 60,
            options.LocalPollingIntervalSeconds / 60).ToString();
        NotifyRepliesCheckBox.IsChecked = options.NotifyReplies;
        NotifyQuotesCheckBox.IsChecked = options.NotifyQuotes;
        NotifyRepostsCheckBox.IsChecked = options.NotifyReposts;
        TopMostCheckBox.IsChecked = options.TopMost;
        AutoStartCheckBox.IsChecked = autoStart.IsEnabled();
    }

    private void SettingsWindow_Loaded(object sender, RoutedEventArgs e)
    {
        var workArea = SystemParameters.WorkArea;
        const double screenMargin = 32;
        var availableWidth = Math.Max(360, workArea.Width - screenMargin);
        var availableHeight = Math.Max(320, workArea.Height - screenMargin);

        MinWidth = 0;
        MinHeight = 0;
        MaxWidth = Math.Min(720, availableWidth);
        MaxHeight = Math.Min(760, availableHeight);
        MinWidth = Math.Min(480, MaxWidth);
        MinHeight = Math.Min(420, MaxHeight);
        Width = Math.Min(560, MaxWidth);
        Height = Math.Min(680, MaxHeight);

        Left = workArea.Left + Math.Max(0, (workArea.Width - Width) / 2);
        Top = workArea.Top + Math.Max(0, (workArea.Height - Height) / 2);
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(IntervalMinutesTextBox.Text.Trim(), out var intervalMinutes) ||
            intervalMinutes is < MonitorOptions.MinimumPollingIntervalSeconds / 60 or
                > MonitorOptions.MaximumPollingIntervalSeconds / 60)
        {
            MessageBox.Show("检查间隔必须是 5～1440 之间的整数分钟。", "设置无效",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            IntervalMinutesTextBox.Focus();
            IntervalMinutesTextBox.SelectAll();
            return;
        }

        var oldInterval = _options.LocalPollingIntervalSeconds;
        var oldNotifyReplies = _options.NotifyReplies;
        var oldNotifyQuotes = _options.NotifyQuotes;
        var oldNotifyReposts = _options.NotifyReposts;
        var oldTopMost = _options.TopMost;
        var oldAutoStartOption = _options.AutoStart;
        var oldAutoStartRegistry = _autoStart.IsEnabled();
        var requestedAutoStart = AutoStartCheckBox.IsChecked == true;

        try
        {
            if (requestedAutoStart != oldAutoStartRegistry && !_autoStart.SetEnabled(requestedAutoStart))
            {
                throw new InvalidOperationException("无法修改 Windows 开机启动项，请查看日志。");
            }

            _options.LocalPollingIntervalSeconds = intervalMinutes * 60;
            _options.NotifyReplies = NotifyRepliesCheckBox.IsChecked == true;
            _options.NotifyQuotes = NotifyQuotesCheckBox.IsChecked == true;
            _options.NotifyReposts = NotifyRepostsCheckBox.IsChecked == true;
            _options.TopMost = TopMostCheckBox.IsChecked == true;
            _options.AutoStart = requestedAutoStart;
            ConfigLoader.Save(_options, _configPath);
            _onSaved();
            _logger.Info($"Settings saved; interval={intervalMinutes}m, replies={_options.NotifyReplies}, quotes={_options.NotifyQuotes}, reposts={_options.NotifyReposts}, topmost={_options.TopMost}, autostart={_options.AutoStart}");
            Close();
        }
        catch (Exception exception)
        {
            _options.LocalPollingIntervalSeconds = oldInterval;
            _options.NotifyReplies = oldNotifyReplies;
            _options.NotifyQuotes = oldNotifyQuotes;
            _options.NotifyReposts = oldNotifyReposts;
            _options.TopMost = oldTopMost;
            _options.AutoStart = oldAutoStartOption;
            if (requestedAutoStart != oldAutoStartRegistry)
            {
                _autoStart.SetEnabled(oldAutoStartRegistry);
            }

            _logger.Error("保存设置失败。", exception);
            MessageBox.Show($"保存设置失败：{exception.Message}", "Tibo Monitor",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e) => Close();

    private void OpenDataButton_Click(object sender, RoutedEventArgs e) =>
        TrayIconService.OpenFolder(_dataRoot);

    private void OpenLogsButton_Click(object sender, RoutedEventArgs e) =>
        TrayIconService.OpenFolder(Path.Combine(_dataRoot, "Logs"));
}
