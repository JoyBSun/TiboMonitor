using TiboMonitor.Core.Configuration;
using TiboMonitor.Core.Models;

namespace TiboMonitor.Core.Services;

public sealed class DeduplicationService
{
    public SyncResult Synchronize(
        MonitorState state,
        RemoteFeed feed,
        MonitorOptions options,
        DateTimeOffset detectedAt)
    {
        var candidates = feed.Posts
            .Where(post => ShouldNotify(post.Type, options))
            .GroupBy(post => post.Id, StringComparer.Ordinal)
            .Select(group => group.First())
            .OrderBy(post => post.CreatedAt)
            .ThenBy(post => post.Id, SnowflakeIdComparer.Instance)
            .ToList();

        if (!state.Initialized)
        {
            state.Initialized = true;
            state.BaselinePostId = candidates.Select(post => post.Id).Max(SnowflakeIdComparer.Instance);

            var notifyIds = options.NotifyRecentOnFirstRun
                ? candidates.TakeLast(options.FirstRunRecentCount).Select(post => post.Id).ToHashSet(StringComparer.Ordinal)
                : new HashSet<string>(StringComparer.Ordinal);

            foreach (var post in candidates)
            {
                state.Posts.Add(ToStoredPost(post, detectedAt, !notifyIds.Contains(post.Id)));
            }

            Trim(state, options.MaxStatePosts);
            return new SyncResult(true, notifyIds.Count, state.Posts.Count(post => !post.Read));
        }

        var knownIds = state.Posts.Select(post => post.Id).ToHashSet(StringComparer.Ordinal);
        var added = 0;
        foreach (var post in candidates)
        {
            if (!knownIds.Add(post.Id))
            {
                continue;
            }

            state.Posts.Add(ToStoredPost(post, detectedAt, read: false));
            state.BaselinePostId = MaxId(state.BaselinePostId, post.Id);
            added++;
        }

        Trim(state, options.MaxStatePosts);
        return new SyncResult(false, added, state.Posts.Count(post => !post.Read));
    }

    public static void MarkRead(MonitorState state, string postId)
    {
        var post = state.Posts.FirstOrDefault(item => string.Equals(item.Id, postId, StringComparison.Ordinal));
        if (post is not null)
        {
            post.Read = true;
        }
    }

    public static IReadOnlyList<StoredPost> GetUnread(MonitorState state) =>
        state.Posts
            .Where(post => !post.Read)
            .OrderBy(post => post.CreatedAt)
            .ThenBy(post => post.Id, SnowflakeIdComparer.Instance)
            .ToList();

    private static bool ShouldNotify(PostType type, MonitorOptions options) => type switch
    {
        PostType.Reply => options.NotifyReplies,
        PostType.Quote => options.NotifyQuotes,
        PostType.Repost => options.NotifyReposts,
        _ => true
    };

    private static StoredPost ToStoredPost(XPost post, DateTimeOffset detectedAt, bool read) => new()
    {
        Id = post.Id,
        Text = post.Text,
        CreatedAt = post.CreatedAt,
        FirstDetectedAt = detectedAt,
        Url = post.Url,
        Type = post.Type,
        Read = read
    };

    private static string MaxId(string? left, string right) =>
        string.IsNullOrWhiteSpace(left) || SnowflakeIdComparer.Instance.Compare(left, right) < 0 ? right : left;

    private static void Trim(MonitorState state, int maximum)
    {
        if (state.Posts.Count <= maximum)
        {
            return;
        }

        var readToRemove = state.Posts
            .Where(post => post.Read)
            .OrderBy(post => post.CreatedAt)
            .Take(state.Posts.Count - maximum)
            .ToHashSet();

        state.Posts.RemoveAll(readToRemove.Contains);
    }
}

public sealed class SnowflakeIdComparer : IComparer<string>
{
    public static SnowflakeIdComparer Instance { get; } = new();

    public int Compare(string? x, string? y)
    {
        x ??= string.Empty;
        y ??= string.Empty;
        var lengthComparison = x.Length.CompareTo(y.Length);
        return lengthComparison != 0 ? lengthComparison : string.CompareOrdinal(x, y);
    }
}
