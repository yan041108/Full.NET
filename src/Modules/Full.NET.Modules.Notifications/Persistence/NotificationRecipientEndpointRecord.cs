namespace Full.NET.Modules.Notifications.Persistence;

/// <summary>收件端点对外投影，永不包含 ProtectedValue。</summary>
internal sealed record NotificationRecipientEndpointRecord(
    Guid Id,
    Guid? TenantId,
    string ScopeKey,
    string TenantScopeKey,
    Guid UserId,
    Guid ProviderProfileVersionId,
    string EndpointKindKey,
    string MaskedValue,
    string VerificationStatusKey,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc);
