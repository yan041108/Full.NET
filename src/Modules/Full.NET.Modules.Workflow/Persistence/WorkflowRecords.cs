namespace Full.NET.Modules.Workflow.Persistence;

/// <summary>工作流定义持久化投影。</summary>
internal sealed record WorkflowDefinitionRecord(
    Guid Id,
    Guid? TenantId,
    string ScopeKey,
    string TenantScopeKey,
    string DefinitionKey,
    Guid? DraftId,
    Guid? LatestPublishedVersionId,
    Guid CreatedById,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc,
    long Version);

/// <summary>工作流定义草稿持久化投影。</summary>
internal sealed record WorkflowDefinitionDraftRecord(
    Guid Id,
    Guid DefinitionId,
    string DraftJson,
    long DraftRevision,
    string ContentHash,
    Guid UpdatedById,
    DateTimeOffset UpdatedAtUtc);

/// <summary>不可变工作流定义版本持久化投影。</summary>
internal sealed record WorkflowDefinitionVersionRecord(
    Guid Id,
    Guid DefinitionId,
    Guid FormVersionId,
    int VersionNumber,
    int SchemaVersion,
    string CanonicalJson,
    string ContentHash,
    Guid PublishedById,
    DateTimeOffset PublishedAtUtc);

/// <summary>工作流表单定义持久化投影。</summary>
internal sealed record WorkflowFormDefinitionRecord(
    Guid Id,
    Guid? TenantId,
    string ScopeKey,
    string TenantScopeKey,
    string FormKey,
    string DraftSchemaJson,
    long DraftRevision,
    Guid? LatestPublishedVersionId,
    Guid CreatedById,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc);

/// <summary>不可变工作流表单版本持久化投影。</summary>
internal sealed record WorkflowFormVersionRecord(
    Guid Id,
    Guid FormDefinitionId,
    int VersionNumber,
    int SchemaVersion,
    int AdapterVersion,
    int ComponentCatalogVersion,
    string FormSchemaJson,
    string WebRenderSchemaJson,
    string ContentHash,
    Guid PublishedById,
    DateTimeOffset PublishedAtUtc);

/// <summary>工作流实例持久化投影。</summary>
internal sealed record WorkflowInstanceRecord(
    Guid Id,
    Guid? TenantId,
    string ScopeKey,
    string TenantScopeKey,
    Guid DefinitionVersionId,
    Guid? FormVersionId,
    string BusinessType,
    string BusinessId,
    string StatusKey,
    long Revision,
    Guid StartedById,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    Guid? CancelledById,
    DateTimeOffset? CancelledAtUtc,
    string? CancellationReason,
    string? LeaseOwnerKey,
    DateTimeOffset? LeaseExpiresAtUtc);

/// <summary>工作流待办持久化投影。</summary>
internal sealed record WorkflowTodoRecord(
    Guid Id,
    Guid InstanceId,
    Guid StepId,
    Guid AssigneeUserId,
    string StatusKey,
    DateTimeOffset ArrivedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    string? ResultActionKey,
    long Revision);

/// <summary>后台超时扫描使用的有界待办投影。</summary>
/// <param name="TenantId">租户标识；Host 作用域为空。</param>
/// <param name="ScopeKey">作用域键。</param>
/// <param name="TenantScopeKey">可信作用域唯一键。</param>
/// <param name="InstanceId">实例标识。</param>
/// <param name="TodoId">待办标识。</param>
/// <param name="StepId">步骤标识。</param>
/// <param name="AssigneeUserId">扫描时的当前办理人。</param>
/// <param name="BusinessType">稳定业务类型。</param>
/// <param name="BusinessId">稳定业务标识。</param>
/// <param name="Revision">待办修订号。</param>
/// <param name="NextReminderAtUtc">下一催办时间。</param>
/// <param name="EscalateAtUtc">升级时间。</param>
/// <param name="ReminderIntervalMinutes">催办间隔。</param>
/// <param name="MaxReminderCount">最大催办次数。</param>
/// <param name="ReminderCount">已发送催办次数。</param>
/// <param name="EscalationRecipientUserId">固定升级接收人。</param>
/// <param name="EscalatedAtUtc">已升级时间。</param>
/// <param name="NextTimeoutSignalAtUtc">本次扫描命中的调度时间。</param>
internal sealed record WorkflowTodoTimeoutCandidateRecord(
    Guid? TenantId,
    string ScopeKey,
    string TenantScopeKey,
    Guid InstanceId,
    Guid TodoId,
    Guid StepId,
    Guid AssigneeUserId,
    string BusinessType,
    string BusinessId,
    long Revision,
    DateTimeOffset? NextReminderAtUtc,
    DateTimeOffset? EscalateAtUtc,
    int ReminderIntervalMinutes,
    int MaxReminderCount,
    int ReminderCount,
    Guid? EscalationRecipientUserId,
    DateTimeOffset? EscalatedAtUtc,
    DateTimeOffset NextTimeoutSignalAtUtc);

/// <summary>并行汇合状态持久化记录。</summary>
internal sealed record WorkflowParallelJoinRecord(
    Guid Id,
    Guid InstanceId,
    string ForkNodeKey,
    string JoinNodeKey,
    string GatewayTypeKey,
    int RequiredBranchCount,
    int ArrivedBranchCount,
    string StatusKey,
    long Revision,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? CompletedAtUtc);

/// <summary>实例详情页使用的并行或包容分支状态投影。</summary>
internal sealed record WorkflowParallelJoinStatusRecord(
    Guid Id,
    string ForkNodeKey,
    string JoinNodeKey,
    string GatewayTypeKey,
    int RequiredBranchCount,
    int ArrivedBranchCount,
    string StatusKey,
    string? BranchKey,
    DateTimeOffset? ArrivedAtUtc);
/// <param name="Id">活动待办标识。</param>
/// <param name="DueAtUtc">截止时间。</param>
/// <param name="ReminderCount">已发送催办次数。</param>
/// <param name="EscalatedAtUtc">已升级时间。</param>
internal sealed record WorkflowTodoTimeoutSummaryRecord(
    Guid Id,
    DateTimeOffset? DueAtUtc,
    int ReminderCount,
    DateTimeOffset? EscalatedAtUtc);

/// <summary>取消实例时同时携带活动待办与步骤修订号，保证三类状态在同一乐观锁事务内关闭。</summary>
internal sealed record WorkflowActiveWorkRecord(
    Guid TodoId,
    long TodoRevision,
    Guid StepId,
    long StepRevision);

/// <summary>办理单条待办时携带当前步骤节点键，避免字段策略解析额外查询步骤表。</summary>
internal sealed record WorkflowTodoRuntimeRecord(
    Guid Id,
    Guid InstanceId,
    Guid StepId,
    Guid AssigneeUserId,
    string StatusKey,
    DateTimeOffset ArrivedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    string? ResultActionKey,
    long Revision,
    string NodeKey,
    long StepRevision,
    string? ApprovalModeKey,
    int? RequiredApprovalCount,
    int? ApprovalSlotCount,
    Guid? ParallelJoinId = null,
    string? ParallelBranchKey = null,
    string? ParallelJoinNodeKey = null);

/// <summary>当前待办对应的一人一票审批席位。</summary>
/// <param name="Id">审批席位标识。</param>
/// <param name="Revision">席位乐观锁修订号。</param>
internal sealed record WorkflowApprovalSlotRecord(Guid Id, long Revision);

/// <summary>当前活动多人审批步骤的权威进度快照。</summary>
/// <param name="NodeKey">稳定节点键。</param>
/// <param name="ApprovalModeKey">单人、会签、或签或 N-of-M 模式键。</param>
/// <param name="RequiredApprovalCount">步骤通过所需的同意票数。</param>
/// <param name="ApprovedCount">当前已同意票数。</param>
/// <param name="RejectedCount">当前已驳回票数。</param>
/// <param name="PendingCount">当前仍待处理票数。</param>
internal sealed record WorkflowInstanceApprovalProgressRecord(
    string NodeKey,
    string ApprovalModeKey,
    int RequiredApprovalCount,
    int ApprovedCount,
    int RejectedCount,
    int PendingCount);

/// <summary>数据库按步骤聚合的多人审批权威票数。</summary>
/// <param name="ApprovedCount">已赞成票数。</param>
/// <param name="RejectedCount">已反对票数。</param>
/// <param name="PendingCount">仍未决定的票数。</param>
internal sealed record WorkflowApprovalTallyRecord(
    int ApprovedCount,
    int RejectedCount,
    int PendingCount);

/// <summary>审批退回使用的当前有效执行链历史目标。</summary>
/// <param name="StepId">历史步骤标识。</param>
/// <param name="NodeKey">稳定节点键。</param>
/// <param name="AssigneeUserId">历史办理人快照。</param>
/// <param name="ExecutionSequence">实例内单调执行序号，用于可靠失效目标及其后的旧执行链。</param>
/// <param name="StartedAtUtc">步骤开始时间。</param>
/// <param name="CompletedAtUtc">步骤完成时间。</param>
internal sealed record WorkflowTodoReturnTargetRecord(
    Guid StepId,
    string NodeKey,
    Guid AssigneeUserId,
    long ExecutionSequence,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset CompletedAtUtc);

/// <summary>启动实例所需的同作用域不可变定义和表单版本。</summary>
internal sealed record WorkflowRuntimeAssetRecord(
    Guid DefinitionVersionId,
    Guid FormVersionId,
    string CanonicalJson,
    string FormSchemaJson);

/// <summary>实例表单当前提交快照。</summary>
internal sealed record WorkflowFormSubmissionRecord(
    Guid Id,
    Guid InstanceId,
    Guid FormVersionId,
    string SubmissionJson,
    string DataClassificationSummary,
    long Revision,
    Guid UpdatedById,
    DateTimeOffset UpdatedAtUtc);

/// <summary>用于确定性重放的已提交动作摘要。</summary>
internal sealed record WorkflowActionReceiptRecord(
    string ActionKey,
    Guid ActorUserId,
    long InstanceRevision,
    string IdempotencyKey,
    string? RequestHash,
    Guid? ResultTodoId,
    string? ResultStatusKey = null);

/// <summary>实例执行轨迹的追加式投影。</summary>
internal sealed record WorkflowExecutionLogRecord(
    Guid Id,
    Guid InstanceId,
    Guid? StepId,
    string TransitionKey,
    string? FromStatusKey,
    string ToStatusKey,
    string? IdempotencyKey,
    string? Summary,
    DateTimeOffset CreatedAtUtc);

/// <summary>“我的抄送”列表和本人已读动作使用的租户内持久化投影。</summary>
internal sealed record WorkflowCcRecord(
    Guid Id,
    Guid InstanceId,
    Guid? StepId,
    string NodeKey,
    Guid RecipientUserId,
    string BusinessType,
    string BusinessId,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? ReadAtUtc);

/// <summary>恢复任务投影；列顺序必须与 WorkflowRecoverySql 的显式 SELECT 一致。</summary>
/// <param name="Id">恢复任务标识。</param>
/// <param name="TenantId">租户标识；宿主作用域为空。</param>
/// <param name="ScopeKey">作用域键，host 或 tenant。</param>
/// <param name="TenantScopeKey">可信租户作用域键。</param>
/// <param name="InstanceId">关联工作流实例标识。</param>
/// <param name="StepId">关联步骤标识；实例级任务为空。</param>
/// <param name="KindKey">恢复种类键。</param>
/// <param name="StatusKey">恢复任务状态键。</param>
/// <param name="AttemptCount">已尝试次数。</param>
/// <param name="Revision">任务修订号。</param>
/// <param name="LeaseOwnerKey">当前租约持有者；空闲时为空。</param>
/// <param name="LeaseExpiresAtUtc">租约过期时间。</param>
/// <param name="LeaseGeneration">租约世代。</param>
/// <param name="NextAttemptAtUtc">下次允许领取时间。</param>
/// <param name="LastError">最后错误摘要。</param>
/// <param name="CreatedAtUtc">创建时间。</param>
/// <param name="UpdatedAtUtc">最近更新时间。</param>
internal sealed record WorkflowRecoveryTaskRecord(
    Guid Id,
    Guid? TenantId,
    string ScopeKey,
    string TenantScopeKey,
    Guid InstanceId,
    Guid? StepId,
    string KindKey,
    string StatusKey,
    int AttemptCount,
    long Revision,
    string? LeaseOwnerKey,
    DateTimeOffset? LeaseExpiresAtUtc,
    int LeaseGeneration,
    DateTimeOffset? NextAttemptAtUtc,
    string? LastError,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

/// <summary>扫描器候选；Worker 按种类补齐未关闭恢复任务。</summary>
/// <param name="TenantId">租户标识；宿主作用域为空。</param>
/// <param name="ScopeKey">作用域键，host 或 tenant。</param>
/// <param name="TenantScopeKey">可信租户作用域键。</param>
/// <param name="InstanceId">关联工作流实例标识。</param>
/// <param name="StepId">未完成步骤标识；实例级扫描为空。</param>
internal sealed record WorkflowRecoveryScanCandidate(
    Guid? TenantId,
    string ScopeKey,
    string TenantScopeKey,
    Guid InstanceId,
    Guid? StepId);

/// <summary>加签链持久化投影。</summary>
internal sealed record WorkflowCountersignChainRecord(
    Guid Id,
    Guid InstanceId,
    Guid StepId,
    Guid OriginTodoId,
    string DirectionKey,
    string StatusKey,
    Guid CreatedByUserId,
    DateTimeOffset CreatedAtUtc);

/// <summary>加签项列表投影。</summary>
internal sealed record WorkflowCountersignItemRecord(
    Guid Id,
    Guid ChainId,
    int SequenceNo,
    Guid AssigneeUserId,
    Guid? TodoId,
    string StatusKey);

/// <summary>办理动作使用的加签项上下文。</summary>
internal sealed record WorkflowCountersignItemContextRecord(
    Guid Id,
    Guid ChainId,
    int SequenceNo,
    Guid AssigneeUserId,
    Guid? TodoId,
    string StatusKey,
    string DirectionKey,
    Guid OriginTodoId,
    Guid InstanceId,
    Guid StepId,
    string ChainStatusKey);
