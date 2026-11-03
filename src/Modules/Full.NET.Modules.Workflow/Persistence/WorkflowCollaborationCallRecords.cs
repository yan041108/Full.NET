namespace Full.NET.Modules.Workflow.Persistence;

/// <summary>源作用域不可变调用及固定投影；目标执行尚未创建时也保留原始交付身份。</summary>
internal sealed record WorkflowCollaborationCallRecord(Guid Id, Guid InstanceId, Guid ExecutionEpochId, string TenantScopeKey,
    string NodeKey, string ContinuationNodeKey, Guid GrantId, Guid DeliveryId, string BindingJson, string BindingHash,
    string InputJson, string InputHash, long InputFormRevision, string ProjectionJson, string ProjectionHash,
    int ReservedActivities, string StatusKey, long Revision, DateTimeOffset CreatedAtUtc, DateTimeOffset UpdatedAtUtc);

/// <summary>消费者仅在信封自己的作用域登记收件；Relay不能复用消费者事务切换到目标。</summary>
internal sealed record WorkflowCollaborationDeliveryRecord(Guid Id, Guid CallId, string TenantScopeKey, Guid EnvelopeMessageId,
    string BindingHash, string StatusKey, long Revision, DateTimeOffset ReceivedAtUtc);
