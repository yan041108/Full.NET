namespace Full.NET.Modules.Notifications.Contracts;

/// <summary>
/// 站内信相关操作的稳定权限码，不可本地化且作为服务端授权与客户端可见性的共同权威。
/// </summary>
public static class InboxPermissions
{
    public const string Read = "notifications.inbox.read";

    public const string Send = "notifications.inbox.send";

    public const string MarkRead = "notifications.inbox.mark_read";

    public const string MarkAllRead = "notifications.inbox.mark_all_read";
}

/// <summary>
/// 站内信读取状态机值，持久化与协议字段共享同一稳定字符串。
/// </summary>
public static class InboxMessageStatuses
{
    public const string Unread = "unread";

    public const string Read = "read";
}

/// <summary>站内信响应契约，面向收件箱列表与详情接口。</summary>
public sealed record InboxMessageResponse(
    Guid Id,
    string Title,
    string Content,
    string Status,
    DateTimeOffset? ReadAtUtc,
    DateTimeOffset CreatedAtUtc,
    Guid? CreatedByUserId);

/// <summary>当前用户未读站内信数量，用作实时徽标的权威值。</summary>
public sealed record InboxUnreadCountResponse(int UnreadCount);

/// <summary>Host 管理员发送站内信的请求契约，收件人由管理员指定。</summary>
public sealed record SendHostInboxMessageRequest(
    Guid RecipientUserId,
    string Title,
    string Content);

/// <summary>当前租户管理员发送站内信的请求契约；TenantId 只能来自受信会话。</summary>
public sealed record SendTenantInboxMessageRequest(
    Guid RecipientUserId,
    string Title,
    string Content);

/// <summary>收件端点对外响应，只返回掩码与验证状态。</summary>
public sealed record RecipientEndpointResponse(
    Guid Id,
    Guid UserId,
    Guid ProviderProfileVersionId,
    string EndpointKindKey,
    string MaskedValue,
    string VerificationStatusKey,
    DateTimeOffset CreatedAtUtc);
