using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using TiboMonitor.Core.Configuration;
using TiboMonitor.Core.Models;
using TiboMonitor.Core.Services;
using MessageBox = System.Windows.MessageBox;

namespace TiboMonitor.App;

public partial class MainWindow : Window
{
    private readonly MonitorCoordinator _coordinator;
    private readonly RollingFileLogger _logger;
    private IReadOnlyList<StoredPost> _unread = [];
    private int _index;
    private bool _forceClose;

    public MainWindow(MonitorCoordinator coordinator, MonitorOptions options, RollingFileLogger logger)
    {
        InitializeComponent();
        _coordinator = coordinator;
        _logger = logger;
        Topmost = options.TopMost;
        AccountText.Text = $"@{options.Account}";
    }

    public async Task RefreshAsync()
    {
        var currentId = CurrentPost?.Id;
        _unread = await _coordinator.GetUnreadAsync();
        if (currentId is not null)
        {
            var retainedIndex = _unread.ToList().FindIndex(post => post.Id == currentId);
            _index = retainedIndex >= 0 ? retainedIndex : Math.Min(_index, Math.Max(0, _unread.Count - 1));
        }
        else
        {
            _index = Math.Min(_index, Math.Max(0, _unread.Count - 1));
        }

        Render();
    }

    public void ShowReminder(bool activate)
    {
        _ = RefreshAsync();
        if (_unread.Count == 0 && _coordinator.UnreadCount == 0)
        {
            MessageBox.Show("当前没有未读消息。", "Tibo Monitor",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        Show();
        WindowState = WindowState.Normal;
        if (activate)
        {
            Activate();
        }

        _logger.Info("Popup displayed");
    }

    public void ForceClose()
    {
        _forceClose = true;
        Close();
    }

    public void ApplyOptions(MonitorOptions options) => Topmost = options.TopMost;

    private StoredPost? CurrentPost => _unread.Count == 0 ? null : _unread[_index];

    private void Render()
    {
        UnreadText.Text = $"未读：{_unread.Count}";
        if (CurrentPost is not { } post)
        {
            PositionText.Text = "0 / 0";
            TimeText.Text = "没有未读消息";
            TypeText.Text = string.Empty;
            BodyText.Text = "所有消息均已阅读。窗口即将隐藏。";
            PreviousButton.IsEnabled = false;
            NextButton.IsEnabled = false;
            OpenPostButton.IsEnabled = false;
            MarkReadButton.IsEnabled = false;
            if (IsVisible)
            {
                Hide();
            }

            return;
        }

        PositionText.Text = $"{_index + 1} / {_unread.Count}";
        TimeText.Text = $"发布：{post.CreatedAt.ToLocalTime():yyyy-MM-dd HH:mm}";
        TypeText.Text = $"类型：{ToChinese(post.Type)}";
        BodyText.Text = post.Text;
        PreviousButton.IsEnabled = _index > 0;
        NextButton.IsEnabled = _index < _unread.Count - 1;
        OpenPostButton.IsEnabled = true;
        MarkReadButton.IsEnabled = true;
    }

    private static string ToChinese(PostType type) => type switch
    {
        PostType.Reply => "回复",
        PostType.Quote => "引用",
        PostType.Repost => "转发",
        _ => "原创"
    };

    private void PreviousButton_Click(object sender, RoutedEventArgs e)
    {
        if (_index > 0)
        {
            _index--;
            Render();
        }
    }

    private void NextButton_Click(object sender, RoutedEventArgs e)
    {
        if (_index < _unread.Count - 1)
        {
            _index++;
            Render();
        }
    }

    private void OpenPostButton_Click(object sender, RoutedEventArgs e)
    {
        if (CurrentPost is not { } post)
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(post.Url) { UseShellExecute = true });
        }
        catch (Exception exception)
        {
            _logger.Error("打开原文失败。", exception);
            MessageBox.Show($"无法打开浏览器：{exception.Message}", "Tibo Monitor",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void MarkReadButton_Click(object sender, RoutedEventArgs e)
    {
        if (CurrentPost is not { } post)
        {
            return;
        }

        MarkReadButton.IsEnabled = false;
        await _coordinator.MarkReadAsync(post.Id);
        await RefreshAsync();
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_forceClose)
        {
            return;
        }

        e.Cancel = true;
        Hide();
        StatusText.Text = "窗口已隐藏，消息仍保持未读；可从系统托盘重新打开。";
        _logger.Info("Popup hidden by close button; unread state preserved");
    }
}
