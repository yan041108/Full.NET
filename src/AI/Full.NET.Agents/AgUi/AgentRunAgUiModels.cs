namespace Full.NET.Agents.AgUi;

/// <summary>AG-UI 重放所需的运行快照；身份范围由调用方在 Port 实现中校验。</summary>
public sealed record AgentRunAgUiSnapshot(
    Guid RunId,
    Guid SessionId,
    string StatusKey,
    string DefinitionKey,
    long MaxEventSequence);

/// <summary>持久化运行事件；Sequence 为游标去重键。</summary>
public sealed record AgentRunPersistedEvent(
    long Sequence,
    string EventType,
    int PayloadVersion,
    string Payload,
    DateTimeOffset CreatedAtUtc);

/// <summary>步骤与预算摘要，供 STATE_SNAPSHOT 与工作台展示。</summary>
public sealed record AgentRunAgUiProgress(
    long? InputTokens,
    long? OutputTokens,
    string? UsageStatus,
    string? Outcome,
    IReadOnlyList<AgentRunAgUiStepSummary> Steps);

public sealed record AgentRunAgUiStepSummary(
    string StepKey,
    int Attempt,
    string StatusKey,
    long? InputTokens,
    long? OutputTokens,
    string? ErrorCode);

/// <summary>读取本人可访问运行的 AG-UI 重放数据；不得触发工具或模型执行。</summary>
public interface IAgentRunAgUiReader
{
    ValueTask<AgentRunAgUiSnapshot?> TryGetOwnedSnapshotAsync(
        Guid runId,
        string scopeKey,
        Guid actorUserId,
        CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<AgentRunPersistedEvent>> ListEventsAfterAsync(
        Guid runId,
        long afterSequence,
        int limit,
        CancellationToken cancellationToken = default);

    ValueTask<AgentRunAgUiProgress?> TryGetProgressAsync(
        Guid runId,
        string scopeKey,
        CancellationToken cancellationToken = default);
}
