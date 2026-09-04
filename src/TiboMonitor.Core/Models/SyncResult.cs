namespace TiboMonitor.Core.Models;

public sealed record SyncResult(bool BaselineCreated, int NewPostCount, int UnreadCount);
