namespace Full.NET.Modules.Files.Reconciliation;

/// <summary>在 Worker 作用域间保留进度与冻结截止时间，避免仍有效的老引用永久占满首批。</summary>
internal sealed class ActiveHostFileReferenceClaimReconciliationCursor
{
    internal SemaphoreSlim Gate { get; } = new(1, 1);
    internal DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UnixEpoch;
    internal Guid? Id { get; set; }
    internal DateTimeOffset? StaleBeforeUtc { get; set; }

    internal void Reset()
    {
        UpdatedAtUtc = DateTimeOffset.UnixEpoch;
        Id = null;
        StaleBeforeUtc = null;
    }
}
