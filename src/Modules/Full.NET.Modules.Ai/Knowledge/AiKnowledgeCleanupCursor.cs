namespace Full.NET.Modules.Ai.Knowledge;

/// <summary>跨轮保留进度，失败项不阻塞后续项；重启丢失游标只会幂等重放。</summary>
internal sealed class AiKnowledgeCleanupCursor : IDisposable
{
    internal SemaphoreSlim Gate { get; } = new(1, 1);
    internal DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UnixEpoch;
    internal int Kind { get; set; }
    internal Guid? Id { get; set; }
    internal void Reset() { CreatedAtUtc = DateTimeOffset.UnixEpoch; Kind = 0; Id = null; }
    public void Dispose() => Gate.Dispose();
}
