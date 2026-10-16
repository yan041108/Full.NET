namespace Full.NET.Modules.Ai.Features.IncrementalKnowledgeIndexing;

/// <summary>同进程串行扫描；游标只用于调度，重启丢失后由文档事务幂等重放。</summary>
internal sealed class AiKnowledgeIncrementalRecoveryCursor : IDisposable
{
    internal SemaphoreSlim Gate { get; } = new(1, 1);
    internal Guid? Id { get; set; }
    public void Dispose() => Gate.Dispose();
}
