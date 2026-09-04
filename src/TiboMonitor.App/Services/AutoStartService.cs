using Microsoft.Win32;
using TiboMonitor.Core.Services;

namespace TiboMonitor.App;

public sealed class AutoStartService
{
    private const string RegistryPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "TiboMonitor";
    private readonly RollingFileLogger _logger;

    public AutoStartService(RollingFileLogger logger) => _logger = logger;

    public bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegistryPath, writable: false);
            return key?.GetValue(ValueName) is string value && !string.IsNullOrWhiteSpace(value);
        }
        catch (Exception exception)
        {
            _logger.Error("读取开机启动状态失败。", exception);
            return false;
        }
    }

    public bool SetEnabled(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RegistryPath, writable: true);
            if (enabled)
            {
                var executablePath = Environment.ProcessPath;
                if (string.IsNullOrWhiteSpace(executablePath))
                {
                    throw new InvalidOperationException("无法确定当前 exe 路径。");
                }

                key.SetValue(ValueName, $"\"{executablePath}\" --background", RegistryValueKind.String);
            }
            else
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            }

            _logger.Info($"AutoStart set to {enabled}");
            return true;
        }
        catch (Exception exception)
        {
            _logger.Error("修改开机启动失败。", exception);
            return false;
        }
    }
}
