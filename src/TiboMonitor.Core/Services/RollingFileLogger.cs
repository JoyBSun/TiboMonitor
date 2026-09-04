using System.Text;

namespace TiboMonitor.Core.Services;

public sealed class RollingFileLogger
{
    private readonly string _path;
    private readonly long _maxBytes;
    private readonly int _fileCount;
    private readonly object _gate = new();

    public RollingFileLogger(string path, long maxBytes, int fileCount)
    {
        _path = path;
        _maxBytes = maxBytes;
        _fileCount = fileCount;
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
    }

    public string Path => _path;

    public void Info(string message) => Write("INFO", message);
    public void Warn(string message) => Write("WARN", message);
    public void Error(string message, Exception? exception = null) =>
        Write("ERROR", exception is null ? message : $"{message} | {exception.GetType().Name}: {exception.Message}");

    private void Write(string level, string message)
    {
        try
        {
            lock (_gate)
            {
                RotateIfNeeded();
                var line = $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz} [{level}] {Sanitize(message)}{Environment.NewLine}";
                File.AppendAllText(_path, line, new UTF8Encoding(false));
            }
        }
        catch
        {
            // 日志失败不能导致提醒程序退出。
        }
    }

    private void RotateIfNeeded()
    {
        if (!File.Exists(_path) || new FileInfo(_path).Length < _maxBytes)
        {
            return;
        }

        var oldest = $"{_path}.{_fileCount}";
        if (File.Exists(oldest))
        {
            File.Delete(oldest);
        }

        for (var index = _fileCount - 1; index >= 1; index--)
        {
            var source = $"{_path}.{index}";
            var destination = $"{_path}.{index + 1}";
            if (File.Exists(source))
            {
                File.Move(source, destination, true);
            }
        }

        File.Move(_path, $"{_path}.1", true);
    }

    private static string Sanitize(string message) =>
        message.Replace('\r', ' ').Replace('\n', ' ');
}
