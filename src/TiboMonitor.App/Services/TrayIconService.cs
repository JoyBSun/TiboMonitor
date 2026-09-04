using System.Diagnostics;
using System.IO;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;
using TiboMonitor.Core.Services;

namespace TiboMonitor.App;

public sealed class TrayIconService : IDisposable
{
    private readonly Forms.NotifyIcon _icon;
    private readonly Forms.ToolStripMenuItem _checkNowItem;
    private readonly Forms.ToolStripMenuItem _autoStartItem;
    private readonly RollingFileLogger _logger;
    private bool _updatingAutoStart;
    private string _status = "正在启动";
    private int _unreadCount;

    public TrayIconService(
        Action showWindow,
        Func<Task> checkNow,
        Action showSettings,
        Action showLogs,
        Action exit,
        AutoStartService autoStart,
        RollingFileLogger logger)
    {
        _logger = logger;
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("打开", null, (_, _) => showWindow());
        _checkNowItem = new Forms.ToolStripMenuItem("立即检查");
        _checkNowItem.Click += async (_, _) => await RunSafelyAsync(checkNow);
        menu.Items.Add(_checkNowItem);
        menu.Items.Add("查看未读", null, (_, _) => showWindow());
        menu.Items.Add("设置...", null, (_, _) => showSettings());
        menu.Items.Add("查看日志", null, (_, _) => showLogs());
        menu.Items.Add(new Forms.ToolStripSeparator());
        _autoStartItem = new Forms.ToolStripMenuItem("开机启动")
        {
            Checked = autoStart.IsEnabled(),
            CheckOnClick = true
        };
        _autoStartItem.CheckedChanged += (_, _) =>
        {
            if (_updatingAutoStart)
            {
                return;
            }

            if (!autoStart.SetEnabled(_autoStartItem.Checked))
            {
                _autoStartItem.Checked = autoStart.IsEnabled();
            }
        };
        menu.Items.Add(_autoStartItem);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("退出", null, (_, _) => exit());

        _icon = new Forms.NotifyIcon
        {
            Icon = Drawing.SystemIcons.Warning,
            Text = "Tibo Monitor · 正在启动",
            Visible = true,
            ContextMenuStrip = menu
        };
        _icon.DoubleClick += (_, _) => showWindow();
    }

    public void SetAutoStartChecked(bool enabled)
    {
        _updatingAutoStart = true;
        try
        {
            _autoStartItem.Checked = enabled;
        }
        finally
        {
            _updatingAutoStart = false;
        }
    }

    public void SetMonitoringEnabled(bool enabled) => _checkNowItem.Enabled = enabled;

    public void SetUnreadCount(int count)
    {
        _unreadCount = count;
        UpdateText();
    }

    public void SetStatus(string status)
    {
        _status = status;
        UpdateText();
    }

    private void UpdateText()
    {
        var text = $"Tibo Monitor · {_status} · 未读 {_unreadCount}";
        _icon.Text = text.Length > 63 ? text[..63] : text;
    }

    private async Task RunSafelyAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception exception)
        {
            _logger.Error("托盘操作失败。", exception);
        }
    }

    public static void OpenFolder(string path)
    {
        Directory.CreateDirectory(path);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }
}
