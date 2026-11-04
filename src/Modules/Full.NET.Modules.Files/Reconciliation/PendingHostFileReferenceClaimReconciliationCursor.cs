namespace Full.NET.Modules.Files.Reconciliation;

/// <summary>跨作用域保留 Pending Claim 扫描位置和两类年龄上界，失败消费者在完整周期后重试。</summary>
internal sealed class PendingHostFileReferenceClaimReconciliationCursor
{
    internal SemaphoreSlim Gate { get; } = new(1, 1);
    internal DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UnixEpoch;
    internal Guid? Id { get; set; }
    internal DateTimeOffset? StaleBeforeUtc { get; set; }
    internal DateTimeOffset? ReleaseBeforeUtc { get; set; }
    internal void Reset() { UpdatedAtUtc = DateTimeOffset.UnixEpoch; Id = null; StaleBeforeUtc = null; ReleaseBeforeUtc = null; }
}
