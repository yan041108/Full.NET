namespace Full.NET.Modules.Files.Persistence;

/// <summary>Files私有能力记录；原文件身份与存储定位不得越过接收方契约。</summary>
internal sealed record WorkflowFileReadCapabilityRecord(Guid Id, Guid CallId, Guid? IssuerTenantId, string IssuerScopeKey,
    Guid? RecipientTenantId, string RecipientScopeKey, string DirectionKey, string FieldKey, Guid SourceFileId,
    Guid OwnerReferenceId, string OriginalFileName, string ContentType, long SizeBytes, string ContentHash,
    string StatusKey, DateTimeOffset ExpiresAtUtc, DateTimeOffset CreatedAtUtc, DateTimeOffset? RevokedAtUtc, long Revision,
    string? ProviderKey, string? StorageKey);
