namespace TiboMonitor.Core.Models;

public sealed class XPost
{
    public string Id { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public string Url { get; set; } = string.Empty;
    public PostType Type { get; set; } = PostType.Original;
}
