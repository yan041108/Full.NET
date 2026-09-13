namespace Full.NET.Agents.Runtime;

/// <summary>单事务提交运行状态、可选步骤、检查点与事件；外部模型与工具调用不在事务内。</summary>
public sealed record AgentRunProgressCommit(
    AgentRunLease Lease,
    string NewStatusKey,
    AgentStepUpsert? Step,
    AgentCheckpointUpsert? Checkpoint,
    AgentEventUpsert? Event);

public sealed record AgentStepUpsert(
    Guid StepId,
    string StepKey,
    int Attempt,
    Guid OperationId,
    string StatusKey,
    int? ModelConfigVersion,
    int? ToolVersion,
    Guid? PriceVersionId,
    long? InputTokens,
    long? OutputTokens,
    decimal? Cost,
    string? Currency,
    string? TraceId,
    string? ArgumentDigest,
    string? ErrorCode);

public sealed record AgentCheckpointUpsert(
    Guid CheckpointId,
    long Sequence,
    int FormatVersion,
    string FrameworkVersion,
    int DefinitionVersion,
    string PayloadProtected,
    string Checksum);

public sealed record AgentEventUpsert(
    Guid EventId,
    long Sequence,
    string EventType,
    int PayloadVersion,
    string Payload);
