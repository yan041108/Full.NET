namespace Full.NET.Modules.Notifications.Persistence;

/// <summary>收件端点受保护读取投影；仅用于可信验证边界，禁止进入 HTTP 响应。</summary>
internal sealed record NotificationRecipientEndpointProtectedRecord(
    Guid Id,
    Guid UserId,
    Guid ProviderProfileVersionId,
    string EndpointKindKey,
    string ProtectedValue,
    string VerificationStatusKey);

/// <summary>收件端点验证码挑战持久化投影。</summary>
internal sealed record NotificationRecipientEndpointChallengeRecord(
    Guid Id,
    Guid RecipientEndpointId,
    string TenantScopeKey,
    Guid UserId,
    string CodeHash,
    int AttemptCount,
    int MaxAttempts,
    DateTimeOffset ExpiresAtUtc,
    DateTimeOffset? ConsumedAtUtc,
    DateTimeOffset CreatedAtUtc);
