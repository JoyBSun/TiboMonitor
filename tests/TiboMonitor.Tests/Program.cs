using System.Net;
using System.Text.Json;
using TiboMonitor.Core.Configuration;
using TiboMonitor.Core.Models;
using TiboMonitor.Core.Services;

var tests = new (string Name, Func<Task> Run)[]
{
    ("首次运行只建立 baseline", TestFirstRunBaselineAsync),
    ("新 Post 进入未读", TestNewPostAsync),
    ("重复 Post ID 不重复提醒", TestDuplicateAsync),
    ("三条消息全部保留", TestThreePostsAsync),
    ("关闭再打开后未读仍存在", TestPersistenceAsync),
    ("损坏 state.json 被隔离且程序可继续", TestCorruptStateAsync),
    ("异常 Feed JSON 被拒绝且可重试", TestInvalidFeedAsync),
    ("我已读正确保存", TestMarkReadAsync),
    ("网络失败会返回可捕获异常", TestNetworkFailureAsync),
    ("本机 direct 模式直接生成标准 Feed", TestDirectModeAsync),
    ("本机 direct 模式强制最低 20 分钟", TestDirectIntervalFloorAsync),
    ("实时镜像解析原创与回复", TestFlashFillingParserAsync)
};

var failed = 0;
foreach (var test in tests)
{
    try
    {
        await test.Run();
        Console.WriteLine($"PASS  {test.Name}");
    }
    catch (Exception exception)
    {
        failed++;
        Console.WriteLine($"FAIL  {test.Name}: {exception.Message}");
    }
}

Console.WriteLine($"\nResult: {tests.Length - failed}/{tests.Length} passed");
return failed == 0 ? 0 : 1;

static Task TestFirstRunBaselineAsync()
{
    var state = new MonitorState();
    var service = new DeduplicationService();
    var result = service.Synchronize(state, Feed("100", "101"), Options(), DateTimeOffset.UtcNow);
    Assert(result.BaselineCreated, "应建立 baseline");
    Assert(result.NewPostCount == 0, "默认不能提醒历史消息");
    Assert(state.Posts.All(post => post.Read), "历史消息应标为已读基线");
    return Task.CompletedTask;
}

static Task TestNewPostAsync()
{
    var (state, service) = Initialized();
    var result = service.Synchronize(state, Feed("200"), Options(), DateTimeOffset.UtcNow);
    Assert(result.NewPostCount == 1 && result.UnreadCount == 1, "新消息应产生 1 条未读");
    return Task.CompletedTask;
}

static Task TestDuplicateAsync()
{
    var (state, service) = Initialized();
    service.Synchronize(state, Feed("200"), Options(), DateTimeOffset.UtcNow);
    var duplicate = service.Synchronize(state, Feed("200"), Options(), DateTimeOffset.UtcNow);
    Assert(duplicate.NewPostCount == 0, "重复 ID 不应再次加入");
    Assert(state.Posts.Count(post => post.Id == "200") == 1, "状态中只能保存一份相同 ID");
    return Task.CompletedTask;
}

static Task TestThreePostsAsync()
{
    var (state, service) = Initialized();
    var result = service.Synchronize(state, Feed("201", "202", "203"), Options(), DateTimeOffset.UtcNow);
    Assert(result.NewPostCount == 3 && result.UnreadCount == 3, "三条消息必须全部保留为未读");
    return Task.CompletedTask;
}

static async Task TestPersistenceAsync()
{
    var root = NewTemporaryRoot();
    try
    {
        var logger = Logger(root);
        var path = Path.Combine(root, "Data", "state.json");
        var store = new StateStore(path, logger);
        var (state, service) = Initialized();
        service.Synchronize(state, Feed("300"), Options(), DateTimeOffset.UtcNow);
        await store.SaveAsync(state);

        var reopenedStore = new StateStore(path, logger);
        var reopened = await reopenedStore.LoadAsync();
        Assert(DeduplicationService.GetUnread(reopened).Single().Id == "300", "重启后应恢复未读消息");
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

static async Task TestCorruptStateAsync()
{
    var root = NewTemporaryRoot();
    try
    {
        var path = Path.Combine(root, "Data", "state.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, "{ this is broken");
        var store = new StateStore(path, Logger(root));
        var recovered = await store.LoadAsync();
        Assert(!recovered.Initialized, "损坏状态后应返回安全空状态");
        Assert(Directory.EnumerateFiles(Path.GetDirectoryName(path)!, "state.json.corrupt-*.json").Any(),
            "损坏文件应备份而不是静默覆盖");
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

static async Task TestInvalidFeedAsync()
{
    var root = NewTemporaryRoot();
    try
    {
        var path = Path.Combine(root, "invalid.json");
        await File.WriteAllTextAsync(path, "{ invalid");
        using var httpClient = new HttpClient();
        var client = new RemoteFeedClient(httpClient);
        await AssertThrowsAsync<JsonException>(() => client.GetAsync(string.Empty, path));
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

static async Task TestMarkReadAsync()
{
    var root = NewTemporaryRoot();
    try
    {
        var logger = Logger(root);
        var store = new StateStore(Path.Combine(root, "Data", "state.json"), logger);
        var (state, service) = Initialized();
        service.Synchronize(state, Feed("400"), Options(), DateTimeOffset.UtcNow);
        DeduplicationService.MarkRead(state, "400");
        await store.SaveAsync(state);
        var loaded = await store.LoadAsync();
        Assert(DeduplicationService.GetUnread(loaded).Count == 0, "已读状态应持久化");
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

static async Task TestNetworkFailureAsync()
{
    using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
    var client = new RemoteFeedClient(httpClient);
    var failedAsExpected = false;
    try
    {
        await client.GetAsync("http://127.0.0.1:1/feed.json", string.Empty);
    }
    catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
    {
        failedAsExpected = true;
    }

    Assert(failedAsExpected, "网络不可用时应返回可由协调器捕获的异常");
}

static async Task TestDirectModeAsync()
{
    const string html = """
        <div class="tweet-card" onclick="if(!event.target.closest('a')) window.location.href='/thread/700'">
          <div class="tweet-header"><div class="tweet-author"><span class="username">@thsottiaux</span><span class="tweet-time">2026-08-27 14:31:31</span></div></div>
          <div class="tweet-text-container"><div class="tweet-text">direct message</div></div>
        </div>
        """;
    using var httpClient = new HttpClient(new StaticResponseHandler(HttpStatusCode.OK, html));
    var client = new RemoteFeedClient(httpClient);
    var options = new MonitorOptions
    {
        Account = "thsottiaux",
        FeedMode = "direct",
        AutoStart = false
    };

    var feed = await client.GetAsync(options);
    Assert(feed.User == "thsottiaux", "本机 Feed 账号应正确");
    Assert(feed.Posts.Single().Id == "700", "本机模式应直接解析嵌入时间线");
}

static Task TestFlashFillingParserAsync()
{
    const string html = """
        <div class="tweet-card" onclick="if(!event.target.closest('a')) window.location.href='/thread/2092756702349398036'">
          <div class="tweet-header"><span class="username">@thsottiaux</span><span class="tweet-time">2026-08-27 07:30:54</span></div>
          <div class="tweet-text-container"><div class="tweet-text">A few weeks at OpenAI feel like years</div></div>
        </div>
        <div class="tweet-card" onclick="if(!event.target.closest('a')) window.location.href='/thread/2092863071748583492'">
          <div class="quoted-tweet reply-tweet"><span class="username">@someone</span><div class="tweet-text">quoted text</div></div>
          <div class="tweet-header"><span class="username">@thsottiaux</span><span class="tweet-time">2026-08-27 14:33:34</span></div>
          <div class="tweet-text-container"><div class="tweet-text">@someone reply</div></div>
        </div>
        """;

    var posts = FlashFillingParser.ParsePostsFromHtml(html, "thsottiaux");
    Assert(posts.Count == 2, "应解析两条实时镜像动态");
    Assert(posts[0].Id == "2092756702349398036" && posts[0].Type == PostType.Original,
        "第一条应识别为原创");
    Assert(posts[1].Id == "2092863071748583492" && posts[1].Type == PostType.Reply,
        "第二条应忽略引用区并识别 Tibo 的回复");
    return Task.CompletedTask;
}

static Task TestDirectIntervalFloorAsync()
{
    var root = NewTemporaryRoot();
    try
    {
        var path = Path.Combine(root, "config.json");
        File.WriteAllText(path, """
            {
              "Account": "thsottiaux",
              "FeedMode": "direct",
              "LocalPollingIntervalSeconds": 60,
              "AutoStart": false
            }
            """);
        var options = ConfigLoader.Load(path);
        Assert(options.LocalPollingIntervalSeconds == 1200, "direct 模式必须限制为最低 1200 秒");
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }

    return Task.CompletedTask;
}

static (MonitorState State, DeduplicationService Service) Initialized()
{
    var state = new MonitorState();
    var service = new DeduplicationService();
    service.Synchronize(state, Feed("100"), Options(), DateTimeOffset.UtcNow);
    return (state, service);
}

static RemoteFeed Feed(params string[] ids) => new()
{
    User = "thsottiaux",
    GeneratedAt = DateTimeOffset.UtcNow,
    Posts = ids.Select(id => new XPost
    {
        Id = id,
        Text = $"message {id}",
        CreatedAt = DateTimeOffset.UtcNow.AddMinutes(long.Parse(id) % 10),
        Url = $"https://x.com/thsottiaux/status/{id}",
        Type = PostType.Original
    }).ToList()
};

static MonitorOptions Options() => new() { AutoStart = false };

static string NewTemporaryRoot()
{
    var root = Path.Combine(Path.GetTempPath(), "TiboMonitorTests", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(root);
    return root;
}

static RollingFileLogger Logger(string root) =>
    new(Path.Combine(root, "Logs", "test.log"), 100_000, 1);

static void Assert(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

static async Task AssertThrowsAsync<TException>(Func<Task> action) where TException : Exception
{
    try
    {
        await action();
    }
    catch (TException)
    {
        return;
    }

    throw new InvalidOperationException($"预期异常 {typeof(TException).Name} 未发生");
}

sealed class StaticResponseHandler(HttpStatusCode statusCode, string content) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken) => Task.FromResult(new HttpResponseMessage(statusCode)
    {
        Content = new StringContent(content),
        RequestMessage = request
    });
}
