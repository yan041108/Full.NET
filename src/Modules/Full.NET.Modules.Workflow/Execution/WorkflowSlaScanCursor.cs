using Full.NET.Modules.Workflow.Persistence;

namespace Full.NET.Modules.Workflow.Execution;

/// <summary>跨短作用域保存扫描位置；满页停用租户只能影响本轮，不能永久挡住后续截止。</summary>
internal sealed class WorkflowSlaScanCursor
{
    private readonly object sync = new();
    private WorkflowSlaCursor value;
    public WorkflowSlaCursor Read() { lock (sync) return value; }
    public void Advance(IReadOnlyList<WorkflowSlaRecord> rows, int batchSize)
    {
        lock (sync) value = rows.Count == batchSize ? new(rows[^1].DueAtUtc, rows[^1].Id) : default;
    }
}

/// <summary>使用数据库真实排序的截止与激活标识作为稳定复合游标。</summary>
internal readonly record struct WorkflowSlaCursor(DateTimeOffset? DueAtUtc, Guid? Id);
