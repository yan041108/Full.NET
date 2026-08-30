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
    string? RequestHash);

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
