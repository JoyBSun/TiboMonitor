using System.IO;
using System.Net.Http;
using TiboMonitor.Core.Configuration;
using TiboMonitor.Core.Models;
using TiboMonitor.Core.Services;

namespace TiboMonitor.App;

public sealed class MonitorCoordinator : IDisposable
{
    private readonly MonitorOptions _options;
    private readonly StateStore _stateStore;
    private readonly RemoteFeedClient _feedClient;
    private readonly RollingFileLogger _logger;
    private readonly HttpClient _httpClient;
    private readonly DeduplicationService _deduplication = new();
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private readonly CancellationTokenSource _shutdown = new();
    private readonly object _pollingGate = new();
    private MonitorState _state = new();
    private CancellationTokenSource? _pollingCancellation;
    private Task? _pollingTask;

    public MonitorCoordinator(
        MonitorOptions options,
        StateStore stateStore,
        RemoteFeedClient feedClient,
        RollingFileLogger logger,
        HttpClient httpClient)
    {
        _options = options;
        _stateStore = stateStore;
        _feedClient = feedClient;
        _logger = logger;
        _httpClient = httpClient;
    }

    public event Action<int, bool>? UnreadChanged;
    public event Action<string>? StatusChanged;

    public int UnreadCount { get; private set; }

    public void Start() => RestartPolling();

    public void RestartPolling()
    {
        var monitoringEnabled = _options.MonitoringEnabled && !_shutdown.IsCancellationRequested;
        lock (_pollingGate)
        {
            _pollingCancellation?.Cancel();
            _pollingCancellation?.Dispose();
            _pollingCancellation = null;
            _pollingTask = null;
            if (monitoringEnabled)
            {
                _pollingCancellation = CancellationTokenSource.CreateLinkedTokenSource(_shutdown.Token);
                _pollingTask = PollAsync(_pollingCancellation.Token);
            }
        }

        if (monitoringEnabled)
        {
            _logger.Info($"Monitoring enabled; polling interval={_options.LocalPollingIntervalSeconds} seconds");
        }
        else
        {
            _logger.Info("Monitoring paused; periodic checks stopped");
            StatusChanged?.Invoke($"监控已暂停 · 未读 {UnreadCount}");
        }
    }

    public async Task InitializeAsync()
    {
        await _operationGate.WaitAsync(_shutdown.Token);
        try
        {
            _state = await _stateStore.LoadAsync(_shutdown.Token);
            UnreadCount = DeduplicationService.GetUnread(_state).Count;
            _logger.Info($"Loaded {_state.Posts.Count} messages; unread={UnreadCount}");
        }
        finally
        {
            _operationGate.Release();
        }

        UnreadChanged?.Invoke(UnreadCount, UnreadCount > 0);
        if (!_options.MonitoringEnabled)
        {
            StatusChanged?.Invoke($"监控已暂停 · 未读 {UnreadCount}");
            return;
        }

        await CheckNowAsync(manual: false);
    }

    public async Task CheckNowAsync(bool manual)
    {
        if (!_options.MonitoringEnabled)
        {
            if (manual)
            {
                _logger.Info("Manual check skipped because monitoring is paused");
            }

            StatusChanged?.Invoke($"监控已暂停 · 未读 {UnreadCount}");
            return;
        }

        if (!await _operationGate.WaitAsync(0, _shutdown.Token))
        {
            if (manual)
            {
                StatusChanged?.Invoke("正在检查，请稍候");
            }

            return;
        }

        try
        {
            if (string.Equals(_options.FeedMode, "remote", StringComparison.OrdinalIgnoreCase) &&
                string.IsNullOrWhiteSpace(_options.FeedUrl) &&
                string.IsNullOrWhiteSpace(_options.MockFeedPath))
            {
                StatusChanged?.Invoke("等待配置 Feed");
                _logger.Warn("FeedUrl 和 MockFeedPath 均未配置，跳过远程检查。");
                return;
            }

            StatusChanged?.Invoke("正在检查");
            _logger.Info($"Checking feed; mode={_options.FeedMode}");
            var feed = await _feedClient.GetAsync(_options, _shutdown.Token);
            if (!string.Equals(feed.User.TrimStart('@'), _options.Account, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException($"Feed 账号不匹配：期望 {_options.Account}，实际 {feed.User}");
            }

            var result = _deduplication.Synchronize(_state, feed, _options, DateTimeOffset.UtcNow);
            await _stateStore.SaveAsync(_state, _shutdown.Token);
            UnreadCount = result.UnreadCount;

            if (result.BaselineCreated && result.NewPostCount == 0)
            {
                _logger.Info("First run baseline created; historical posts were not marked unread");
            }
            else if (result.NewPostCount > 0)
            {
                _logger.Info($"New posts detected: {result.NewPostCount}");
            }
            else
            {
                _logger.Info("No new posts");
            }

            var modeLabel = string.Equals(_options.FeedMode, "direct", StringComparison.OrdinalIgnoreCase)
                ? "本机直连"
                : "远程 Feed";
            StatusChanged?.Invoke($"{modeLabel}运行中 · 未读 {UnreadCount}");
            UnreadChanged?.Invoke(UnreadCount, result.NewPostCount > 0);
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _logger.Error("检查失败，将在下一周期重试。", exception);
            StatusChanged?.Invoke("检查失败 · 稍后重试");
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task<IReadOnlyList<StoredPost>> GetUnreadAsync()
    {
        await _operationGate.WaitAsync(_shutdown.Token);
        try
        {
            return DeduplicationService.GetUnread(_state);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task MarkReadAsync(string postId)
    {
        await _operationGate.WaitAsync(_shutdown.Token);
        try
        {
            DeduplicationService.MarkRead(_state, postId);
            await _stateStore.SaveAsync(_state, _shutdown.Token);
            UnreadCount = DeduplicationService.GetUnread(_state).Count;
            _logger.Info($"User marked {postId} as read");
        }
        finally
        {
            _operationGate.Release();
        }

        UnreadChanged?.Invoke(UnreadCount, false);
    }

    private async Task PollAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(_options.LocalPollingIntervalSeconds));
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                await CheckNowAsync(manual: false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    public void Dispose()
    {
        _shutdown.Cancel();
        lock (_pollingGate)
        {
            _pollingCancellation?.Cancel();
        }
        try
        {
            _pollingTask?.Wait(TimeSpan.FromSeconds(2));
        }
        catch (AggregateException)
        {
        }

        _pollingCancellation?.Dispose();
        _operationGate.Dispose();
        _shutdown.Dispose();
        _httpClient.Dispose();
    }
}
