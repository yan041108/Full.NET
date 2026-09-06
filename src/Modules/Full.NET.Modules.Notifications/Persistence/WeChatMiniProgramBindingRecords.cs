namespace Full.NET.Modules.Notifications.Persistence;

/// <summary>微信小程序 OpenId 绑定记录。</summary>
internal sealed class WeChatMiniProgramBindingRecord
{
    public Guid Id { get; init; }
    public string TenantScopeKey { get; init; } = string.Empty;
    public Guid UserId { get; init; }
    public string AppId { get; init; } = string.Empty;
    public Guid ProviderProfileVersionId { get; init; }
    public string OpenIdProtected { get; init; } = string.Empty;
    public string OpenIdMask { get; init; } = string.Empty;
    public string OpenIdSha256Hex { get; init; } = string.Empty;
    public string? UnionIdProtected { get; init; }
    public string? UnionIdMask { get; init; }
    public string VerificationStatusKey { get; init; } = string.Empty;
    public Guid? RecipientEndpointId { get; init; }
    public DateTimeOffset CreatedAtUtc { get; init; }
    public DateTimeOffset? UpdatedAtUtc { get; init; }
}

/// <summary>订阅消息模板授权记录。</summary>
internal sealed class WeChatMiniProgramSubscriptionRecord
{
    public Guid BindingId { get; init; }
    public string TemplateId { get; init; } = string.Empty;
    public string StatusKey { get; init; } = string.Empty;
    public DateTimeOffset AuthorizedAtUtc { get; init; }
}
