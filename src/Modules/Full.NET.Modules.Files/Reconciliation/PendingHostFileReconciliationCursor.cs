namespace Full.NET.Modules.Files.Reconciliation;

/// <summary>跨 Worker 作用域保留上传对账进度及周期上界，保留的慢上传或失败对象不占死首批。</summary>
internal sealed class PendingHostFileReconciliationCursor
{
    internal SemaphoreSlim Gate { get; } = new(1, 1);
    internal DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UnixEpoch;
    internal Guid? Id { get; set; }
    internal DateTimeOffset? CreatedBeforeUtc { get; set; }
    internal void Reset() { CreatedAtUtc = DateTimeOffset.UnixEpoch; Id = null; CreatedBeforeUtc = null; }
}
