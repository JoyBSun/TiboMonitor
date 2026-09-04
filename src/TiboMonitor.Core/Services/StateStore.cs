using System.Text.Json;
using TiboMonitor.Core.Models;

namespace TiboMonitor.Core.Services;

public sealed class StateStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };

    private readonly string _path;
    private readonly RollingFileLogger _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public StateStore(string path, RollingFileLogger logger)
    {
        _path = path;
        _logger = logger;
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
    }

    public string Path => _path;

    public async Task<MonitorState> LoadAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!File.Exists(_path))
            {
                return new MonitorState();
            }

            try
            {
                await using var stream = File.OpenRead(_path);
                return await JsonSerializer.DeserializeAsync<MonitorState>(stream, JsonOptions, cancellationToken)
                    ?? new MonitorState();
            }
            catch (JsonException exception)
            {
                var backupPath = $"{_path}.corrupt-{DateTime.Now:yyyyMMdd-HHmmss}.json";
                File.Move(_path, backupPath, true);
                _logger.Error($"state.json 损坏，已隔离为 {backupPath}", exception);
                return new MonitorState();
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                _logger.Error("读取 state.json 失败，本次使用安全空状态；稍后将重试。", exception);
                return new MonitorState();
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveAsync(MonitorState state, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var temporaryPath = $"{_path}.tmp";
            await using (var stream = File.Create(temporaryPath))
            {
                await JsonSerializer.SerializeAsync(stream, state, JsonOptions, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }

            File.Move(temporaryPath, _path, true);
        }
        finally
        {
            _gate.Release();
        }
    }
}
