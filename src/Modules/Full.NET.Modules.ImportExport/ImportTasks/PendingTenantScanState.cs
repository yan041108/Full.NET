namespace Full.NET.Modules.ImportExport.ImportTasks;

/// <summary>Worker 跨轮保留候选游标；只影响公平调度，不缓存租户授权或持有任务租约。</summary>
internal sealed class PendingTenantScanState
{
    private readonly object _gate = new();
    private Guid? _afterTenantId;

    internal Guid? Read() { lock (_gate) return _afterTenantId; }
    internal void Advance(Guid? tenantId) { lock (_gate) _afterTenantId = tenantId; }
}
