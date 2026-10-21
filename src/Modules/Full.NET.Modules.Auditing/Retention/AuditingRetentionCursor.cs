namespace Full.NET.Modules.Auditing.Retention;

/// <summary>低批次预算也轮流服务各审计类别，游标跨作用域保留并单飞保护。</summary>
internal sealed class AuditingRetentionCursor
{
    internal SemaphoreSlim Gate { get; } = new(1, 1);
    internal int NextCategory { get; set; }
}
