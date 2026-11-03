namespace Full.NET.Modules.Workflow.Persistence;

/// <summary>两方共同使用的最小准入事实，不包含源私有表单，不代替任一方业务根。</summary>
internal sealed record WorkflowCollaborationAdmissionRecord(Guid Id, string SourceTenantScopeKey, string TargetTenantScopeKey,
    Guid DeliveryId, Guid GrantId, string BindingJson, string BindingHash, string ProjectionJson, string ProjectionHash,
    string StatusKey, Guid? TargetInstanceId, long Revision, DateTimeOffset CreatedAtUtc, DateTimeOffset UpdatedAtUtc);

/// <summary>每个目标及后代在第一次激活前固定同一来源，深度仍按实际执行链累加。</summary>
internal sealed record WorkflowCollaborationOriginRecord(Guid Id, Guid CallId, Guid RootInstanceId, string TenantScopeKey, int Depth);

/// <summary>Relay扫描只取耐久身份，正文及授权在独立作用域中重新核对。</summary>
internal sealed record WorkflowCollaborationRelayCandidate(Guid Id, string SourceTenantScopeKey, string TargetTenantScopeKey, DateTimeOffset UpdatedAtUtc);

/// <summary>列表只读取授权绑定头，不能把这个类型交给目标启动或结果执行；不包含业务投影正文。</summary>
internal sealed record WorkflowCollaborationListCandidate(Guid Id, string SourceTenantScopeKey, string TargetTenantScopeKey,
    Guid DeliveryId, Guid GrantId, string BindingJson, string BindingHash, string ProjectionHash,
    string StatusKey, Guid? TargetInstanceId, long Revision);
