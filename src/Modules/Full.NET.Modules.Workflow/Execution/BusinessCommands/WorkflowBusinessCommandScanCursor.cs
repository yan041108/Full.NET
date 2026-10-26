using Full.NET.Modules.Workflow.Persistence;

namespace Full.NET.Modules.Workflow.Execution.BusinessCommands;

/// <summary>两个独立扫描游标跨DI作用域保留，失效租户的满页记录不能饿死后续可执行调用。</summary>
internal sealed class WorkflowBusinessCommandScanCursor
{
    private readonly object gate = new();
    private (DateTimeOffset? UpdatedAtUtc, Guid? Id) ready;
    private (DateTimeOffset? UpdatedAtUtc, Guid? Id) compensation;
    public (DateTimeOffset? UpdatedAtUtc, Guid? Id) Read(bool compensating) { lock (gate) return compensating ? compensation : ready; }
    public void Advance(bool compensating, IReadOnlyList<WorkflowBusinessCommandRecord> rows, int take)
    {
        var next = rows.Count == take ? (rows[^1].UpdatedAtUtc, (Guid?)rows[^1].Id) : ((DateTimeOffset?)null, (Guid?)null);
        lock (gate) { if (compensating) compensation = next; else ready = next; }
    }
}
