namespace Full.NET.Modules.Files.Cleanup;

/// <summary>跨 Worker 作用域保留本周期扫描位置与上界，失败墓碑不会永久占满首批。</summary>
internal sealed class DeletedHostFileBlobCleanupCursor
{
    internal SemaphoreSlim Gate { get; } = new(1, 1);
    internal DateTimeOffset DeletedAtUtc { get; set; } = DateTimeOffset.UnixEpoch;
    internal Guid? Id { get; set; }
    internal DateTimeOffset? ScanThroughUtc { get; set; }

    internal void Reset()
    {
        DeletedAtUtc = DateTimeOffset.UnixEpoch;
        Id = null;
        ScanThroughUtc = null;
    }
}
