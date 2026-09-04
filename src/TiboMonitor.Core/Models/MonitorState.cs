namespace TiboMonitor.Core.Models;

public sealed class MonitorState
{
    public bool Initialized { get; set; }
    public string? BaselinePostId { get; set; }
    public List<StoredPost> Posts { get; set; } = [];
}
