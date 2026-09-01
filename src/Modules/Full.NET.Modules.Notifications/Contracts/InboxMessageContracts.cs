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

/// <summary>当前用户登记收件端点的请求；服务端始终以待验证状态保存。</summary>
/// <param name="ProviderProfileVersionId">当前作用域内已发布并启用的渠道配置版本标识。</param>
/// <param name="EndpointKindKey">渠道 Adapter 声明的稳定端点类型键。</param>
/// <param name="RawValue">只允许在请求和受保护写入边界短暂存在的端点原值。</param>
public sealed record CreateMyRecipientEndpointRequest(
    Guid ProviderProfileVersionId,
    string EndpointKindKey,
    string RawValue);

/// <summary>收件端点对外响应，只返回掩码与验证状态。</summary>
/// <param name="Id">收件端点标识。</param>
/// <param name="UserId">端点所属用户标识。</param>
/// <param name="ProviderProfileVersionId">端点绑定的不可变渠道配置版本标识。</param>
/// <param name="EndpointKindKey">端点类型稳定键。</param>
/// <param name="MaskedValue">可供界面展示的脱敏值。</param>
/// <param name="VerificationStatusKey">服务端维护的验证状态稳定键。</param>
/// <param name="CreatedAtUtc">端点首次登记时间。</param>
public sealed record RecipientEndpointResponse(
    Guid Id,
    Guid UserId,
    Guid ProviderProfileVersionId,
    string EndpointKindKey,
    string MaskedValue,
    string VerificationStatusKey,
    DateTimeOffset CreatedAtUtc);
