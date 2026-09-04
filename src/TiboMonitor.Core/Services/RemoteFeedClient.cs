using System.Net.Http.Headers;
using System.Text.Json;
using TiboMonitor.Core.Configuration;
using TiboMonitor.Core.Models;

namespace TiboMonitor.Core.Services;

public sealed class RemoteFeedClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };

    private readonly HttpClient _httpClient;

    public RemoteFeedClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 TiboMonitor/1.0");
        _httpClient.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en-US,en;q=0.9");
    }

    public async Task<RemoteFeed> GetAsync(
        MonitorOptions options,
        CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(options.MockFeedPath))
        {
            return await GetAsync(options.FeedUrl, options.MockFeedPath, cancellationToken);
        }

        if (string.Equals(options.FeedMode, "direct", StringComparison.OrdinalIgnoreCase))
        {
            return await GetDirectMirrorAsync(options.Account, cancellationToken);
        }

        return await GetAsync(options.FeedUrl, string.Empty, cancellationToken);
    }

    public async Task<RemoteFeed> GetAsync(
        string feedUrl,
        string mockFeedPath,
        CancellationToken cancellationToken = default)
    {
        string json;
        if (!string.IsNullOrWhiteSpace(mockFeedPath))
        {
            var resolvedPath = Path.GetFullPath(mockFeedPath, AppContext.BaseDirectory);
            json = await File.ReadAllTextAsync(resolvedPath, cancellationToken);
        }
        else
        {
            if (!Uri.TryCreate(feedUrl, UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
            {
                throw new InvalidOperationException("FeedUrl 未配置为有效的 HTTP/HTTPS 地址。");
            }

            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.CacheControl = new CacheControlHeaderValue { NoCache = true };
            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();
            json = await response.Content.ReadAsStringAsync(cancellationToken);
        }

        var feed = JsonSerializer.Deserialize<RemoteFeed>(json, JsonOptions)
            ?? throw new InvalidDataException("远程 Feed 内容为空。");
        Validate(feed);
        return feed;
    }

    private async Task<RemoteFeed> GetDirectMirrorAsync(
        string account,
        CancellationToken cancellationToken)
    {
        var escapedAccount = Uri.EscapeDataString(account);
        var uri = new Uri($"https://flash-filling.com/user/{escapedAccount}");
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.CacheControl = new CacheControlHeaderValue { NoCache = true };
        using var response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        var html = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"实时镜像返回 {(int)response.StatusCode} {response.ReasonPhrase}。等待下一周期重试。");
        }

        var feed = new RemoteFeed
        {
            User = account,
            GeneratedAt = DateTimeOffset.UtcNow,
            Posts = FlashFillingParser.ParsePostsFromHtml(html, account).ToList()
        };
        Validate(feed);
        return feed;
    }

    private static void Validate(RemoteFeed feed)
    {
        if (string.IsNullOrWhiteSpace(feed.User))
        {
            throw new InvalidDataException("Feed 缺少 user 字段。");
        }

        if (feed.Posts.Count > 5000)
        {
            throw new InvalidDataException("Feed 消息数量异常，已拒绝处理。");
        }

        foreach (var post in feed.Posts)
        {
            if (string.IsNullOrWhiteSpace(post.Id) || string.IsNullOrWhiteSpace(post.Url))
            {
                throw new InvalidDataException("Feed 中存在缺少 id 或 url 的消息。");
            }
        }
    }
}
