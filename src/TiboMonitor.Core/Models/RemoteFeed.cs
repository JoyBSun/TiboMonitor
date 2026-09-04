namespace TiboMonitor.Core.Models;

public sealed class RemoteFeed
{
    public string User { get; set; } = string.Empty;
    public DateTimeOffset GeneratedAt { get; set; }
    public List<XPost> Posts { get; set; } = [];
}
