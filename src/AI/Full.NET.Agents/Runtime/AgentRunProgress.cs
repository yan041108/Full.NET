namespace Full.NET.Agents.Runtime;

/// <summary>单事务提交运行状态、可选步骤、检查点与事件；外部模型与工具调用不在事务内。</summary>
/// <param name="Lease">当前运行租约，用于乐观并发守卫。</param>
/// <param name="NewStatusKey">提交后切换到的运行状态键。</param>
/// <param name="Step">可选步骤 upsert；为 null 时不更新步骤表。</param>
/// <param name="Checkpoint">可选检查点 upsert；为 null 时不写入检查点。</param>
/// <param name="Event">可选事件 upsert；为 null 时不追加运行事件。</param>
public sealed record AgentRunProgressCommit(
    AgentRunLease Lease,
    string NewStatusKey,
    AgentStepUpsert? Step,
    AgentCheckpointUpsert? Checkpoint,
    AgentEventUpsert? Event);

/// <summary>
/// 步骤级 upsert 载荷；字段顺序与持久化投影一致，发布后不可重排。
/// </summary>
/// <param name="StepId">步骤唯一标识。</param>
/// <param name="StepKey">稳定步骤键，用于跨运行关联。</param>
/// <param name="Attempt">重试次数，从 1 开始。</param>
/// <param name="OperationId">关联的操作标识。</param>
/// <param name="StatusKey">步骤当前状态键。</param>
/// <param name="ModelConfigVersion">模型配置版本；非模型步骤为 null。</param>
/// <param name="ToolVersion">工具版本；非工具步骤为 null。</param>
/// <param name="PriceVersionId">计价版本标识；未计费为 null。</param>
/// <param name="InputTokens">输入令牌数；无用量为 null。</param>
/// <param name="OutputTokens">输出令牌数；无用量为 null。</param>
/// <param name="Cost">步骤成本；未计费为 null。</param>
/// <param name="Currency">成本币种；未计费为 null。</param>
/// <param name="TraceId">分布式追踪标识；无追踪为 null。</param>
/// <param name="ArgumentDigest">入参摘要，避免存储明文载荷。</param>
/// <param name="ErrorCode">稳定错误码；成功为 null。</param>
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

/// <summary>
/// 检查点 upsert 载荷；Payload 已加密，Checksum 用于校验解密完整性。
/// </summary>
/// <param name="CheckpointId">检查点唯一标识。</param>
/// <param name="Sequence">检查点序号，单调递增。</param>
/// <param name="FormatVersion">检查点格式版本，用于兼容升级。</param>
/// <param name="FrameworkVersion">生成检查点的框架版本。</param>
/// <param name="DefinitionVersion">关联定义版本。</param>
/// <param name="PayloadProtected">加密后的检查点载荷。</param>
/// <param name="Checksum">载荷完整性校验值。</param>
public sealed record AgentCheckpointUpsert(
    Guid CheckpointId,
    long Sequence,
    int FormatVersion,
    string FrameworkVersion,
    int DefinitionVersion,
    string PayloadProtected,
    string Checksum);

/// <summary>
/// 运行事件 upsert 载荷；EventType 与 PayloadVersion 共同决定载荷语义。
/// </summary>
/// <param name="EventId">事件唯一标识。</param>
/// <param name="Sequence">事件序号，运行内单调递增。</param>
/// <param name="EventType">稳定事件类型键。</param>
/// <param name="PayloadVersion">载荷 schema 版本。</param>
/// <param name="Payload">事件载荷 JSON。</param>
public sealed record AgentEventUpsert(
    Guid EventId,
    long Sequence,
    string EventType,
    int PayloadVersion,
    string Payload);
