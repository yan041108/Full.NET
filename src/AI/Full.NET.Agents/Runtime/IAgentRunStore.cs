namespace Full.NET.Agents.Runtime;

/// <summary>持久运行存储 Port；实现由 Ai 模块提供，Agents 不访问 fn_ai_*。</summary>
public interface IAgentRunStore
{
    ValueTask<Guid> CreateOrGetAsync(AgentRunDraft draft, CancellationToken cancellationToken = default);

    ValueTask<AgentRunLease?> TryAcquireAsync(
        Guid runId,
        string workerId,
        DateTimeOffset now,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken = default);

    ValueTask<bool> RenewAsync(
        AgentRunLease lease,
        DateTimeOffset now,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken = default);

    ValueTask<bool> CommitProgressAsync(AgentRunProgressCommit commit, CancellationToken cancellationToken = default);

    ValueTask<AgentCheckpointRecord?> FindLatestCheckpointAsync(Guid runId, CancellationToken cancellationToken = default);
}
