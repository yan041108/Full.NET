namespace Full.NET.Modules.Files.Reconciliation;

/// <summary>按扫描器隔离跨轮游标；到达尾部后回绕，失败前缀不会饿死后续记录。</summary>
internal sealed class HostFileScanCursor<TScan>
{
    internal SemaphoreSlim Gate { get; } = new(1, 1);
    internal DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UnixEpoch;
    internal Guid? Id { get; set; }
    // 一轮扫描固定上界，持续新增记录不能阻止尾部回绕并饿死失败前缀。
    internal DateTimeOffset? CutoffUtc { get; set; }
    internal DateTimeOffset? ReleaseBeforeUtc { get; set; }

    internal void Reset()
    {
        Timestamp = DateTimeOffset.UnixEpoch;
        Id = null;
        CutoffUtc = null; ReleaseBeforeUtc = null;
    }
}
