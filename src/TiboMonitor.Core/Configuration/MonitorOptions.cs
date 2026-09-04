namespace TiboMonitor.Core.Configuration;

public sealed class MonitorOptions
{
    public const int MinimumPollingIntervalSeconds = 5 * 60;
    public const int MaximumPollingIntervalSeconds = 24 * 60 * 60;

    public string Account { get; set; } = "thsottiaux";
    public string FeedMode { get; set; } = "direct";
    public string FeedUrl { get; set; } = string.Empty;
    public string MockFeedPath { get; set; } = string.Empty;
    public int LocalPollingIntervalSeconds { get; set; } = 1200;
    public int HttpTimeoutSeconds { get; set; } = 20;
    public bool NotifyReplies { get; set; }
    public bool NotifyQuotes { get; set; } = true;
    public bool NotifyReposts { get; set; }
    public bool TopMost { get; set; } = true;
    public bool AutoStart { get; set; } = true;
    public bool NotifyRecentOnFirstRun { get; set; }
    public int FirstRunRecentCount { get; set; } = 1;
    public int MaxStatePosts { get; set; } = 1000;
    public long MaxLogBytes { get; set; } = 2_000_000;
    public int LogFileCount { get; set; } = 3;
}
