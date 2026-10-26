using Full.NET.Modules.Workflow.Persistence;

namespace Full.NET.Modules.Workflow.Execution;

/// <summary>满页持续向后扫描；暂停或停用租户的等待不能占住整个扫描页。</summary>
internal sealed class WorkflowSubprocessScanCursor
{
    private readonly object gate = new();
    private (DateTimeOffset? UpdatedAtUtc, Guid? Id) cursor;
    public (DateTimeOffset? UpdatedAtUtc, Guid? Id) Read() { lock (gate) return cursor; }
    public void Advance(IReadOnlyList<WorkflowSubprocessCallRecord> rows, int take)
    { lock (gate) cursor = rows.Count == take ? (rows[^1].UpdatedAtUtc, rows[^1].Id) : (null, null); }
}
