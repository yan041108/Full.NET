namespace Full.NET.Modules.Files.Reconciliation;

/// <summary>同一 Worker 内跨作用域保留扫描位置；重启后安全地从头扫描，单轮互斥防止游标倒退。</summary>
internal sealed class TenantResourceFileReconciliationCursor
{
    internal SemaphoreSlim Gate { get; } = new(1, 1);
    internal DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UnixEpoch;
    internal Guid? Id { get; set; }

    internal void Reset()
    {
        CreatedAtUtc = DateTimeOffset.UnixEpoch;
        Id = null;
    }
}
