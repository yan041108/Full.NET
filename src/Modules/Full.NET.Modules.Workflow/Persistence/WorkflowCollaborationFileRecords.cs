namespace Full.NET.Modules.Workflow.Persistence;

/// <summary>只保存本地引用和不透明能力，不保存外租户FileId。</summary>
internal sealed record WorkflowRemoteFileRecord(Guid InstanceId, Guid CallId, string TenantScopeKey, Guid CapabilityId,
    string DirectionKey, string CapabilityFieldKey, string OriginalFileName, string ContentType, long SizeBytes, string ContentHash, DateTimeOffset ExpiresAtUtc);

/// <summary>源签发只能使用精确字段的真实历史引用。</summary>
internal sealed record WorkflowFileIssuingReference(Guid Id, string FieldKey, Guid? OwnerReferenceId);
