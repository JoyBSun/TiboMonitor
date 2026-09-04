using System.Text.Json;
using System.Text;

namespace TiboMonitor.Core.Configuration;

public static class ConfigLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = true
    };

    public static MonitorOptions Load(string path)
    {
        MonitorOptions options;
        if (File.Exists(path))
        {
            var json = File.ReadAllText(path);
            options = JsonSerializer.Deserialize<MonitorOptions>(json, JsonOptions)
                ?? throw new InvalidDataException($"配置文件为空：{path}");
        }
        else
        {
            options = new MonitorOptions();
        }

        var environmentFeedUrl = Environment.GetEnvironmentVariable("TIBO_FEED_URL");
        if (!string.IsNullOrWhiteSpace(environmentFeedUrl))
        {
            options.FeedUrl = environmentFeedUrl.Trim();
        }

        Validate(options);
        return options;
    }

    public static void Save(MonitorOptions options, string path)
    {
        Validate(options);
        var fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        var temporaryPath = $"{fullPath}.tmp";
        var json = JsonSerializer.Serialize(options, JsonOptions);
        File.WriteAllText(temporaryPath, json, new UTF8Encoding(false));
        File.Move(temporaryPath, fullPath, true);
    }

    private static void Validate(MonitorOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.Account))
        {
            throw new InvalidDataException("Account 不能为空。");
        }

        options.Account = options.Account.Trim().TrimStart('@');
        options.FeedMode = options.FeedMode.Trim().ToLowerInvariant();
        if (options.FeedMode is not ("direct" or "remote"))
        {
            throw new InvalidDataException("FeedMode 只能是 direct 或 remote。");
        }

        options.LocalPollingIntervalSeconds = Math.Clamp(
            options.LocalPollingIntervalSeconds,
            MonitorOptions.MinimumPollingIntervalSeconds,
            MonitorOptions.MaximumPollingIntervalSeconds);
        options.HttpTimeoutSeconds = Math.Clamp(options.HttpTimeoutSeconds, 5, 120);
        options.FirstRunRecentCount = Math.Clamp(options.FirstRunRecentCount, 1, 20);
        options.MaxStatePosts = Math.Clamp(options.MaxStatePosts, 100, 10_000);
        options.MaxLogBytes = Math.Max(100_000, options.MaxLogBytes);
        options.LogFileCount = Math.Clamp(options.LogFileCount, 1, 10);
    }
}
