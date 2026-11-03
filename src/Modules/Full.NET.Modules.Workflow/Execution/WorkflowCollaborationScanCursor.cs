using Full.NET.Modules.Workflow.Persistence;

namespace Full.NET.Modules.Workflow.Execution;

/// <summary>满页后继续向后扫描，停用或失效的早期请求不能饿死后续有效调用；未满页时从头重试。</summary>
internal sealed class WorkflowCollaborationScanCursor
{
    private readonly object gate = new();
    private (DateTimeOffset? UpdatedAtUtc, Guid? Id) cursor;
    public (DateTimeOffset? UpdatedAtUtc, Guid? Id) Read() { lock (gate) return cursor; }
    public void Advance(IReadOnlyList<WorkflowCollaborationRelayCandidate> rows, int take)
    { lock (gate) cursor = rows.Count == take ? (rows[^1].UpdatedAtUtc, rows[^1].Id) : (null, null); }
}
