namespace Full.NET.Modules.Files.Reconciliation;

/// <summary>按扫描器隔离跨轮游标；到达尾部后回绕，失败前缀不会饿死后续记录。</summary>
internal sealed class HostFileScanCursor<TScan>
{
    internal SemaphoreSlim Gate { get; } = new(1, 1);
    internal DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UnixEpoch;
    internal Guid? Id { get; set; }

    internal void Reset()
    {
        Timestamp = DateTimeOffset.UnixEpoch;
        Id = null;
    }
}
