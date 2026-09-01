namespace Full.NET.Modules.Notifications.Contracts;

/// <summary>
/// 站内信相关操作的稳定权限码，不可本地化且作为服务端授权与客户端可见性的共同权威。
/// </summary>
public static class InboxPermissions
{
    /// <summary>允许读取自己的站内信列表、详情与未读计数。</summary>
    public const string Read = "notifications.inbox.read";

    /// <summary>允许向其他用户发送站内信；普通用户缺省通常无该权限。</summary>
    public const string Send = "notifications.inbox.send";

    /// <summary>允许将单条站内信标记为已读。</summary>
    public const string MarkRead = "notifications.inbox.mark_read";

    /// <summary>允许一键将全部未读站内信批量标记为已读。</summary>
    public const string MarkAllRead = "notifications.inbox.mark_all_read";
}

/// <summary>
/// 站内信读取状态机值，持久化与协议字段共享同一稳定字符串。
/// </summary>
public static class InboxMessageStatuses
{
    /// <summary>未读状态；在未读计数与徽标中计入。</summary>
    public const string Unread = "unread";

    /// <summary>已读状态；用户已明确打开消息或通过批量标记为已读。</summary>
    public const string Read = "read";
}

/// <summary>站内信响应契约，面向收件箱列表与详情接口。</summary>
/// <param name="Id">站内信消息标识。</param>
/// <param name="Title">消息标题。</param>
/// <param name="Content">消息正文；支持富文本格式由发送端决定。</param>
/// <param name="Status">读取状态稳定机器码，取值自 InboxMessageStatuses。</param>
/// <param name="ReadAtUtc">首次阅读时间（UTC），未读时为 null。</param>
/// <param name="CreatedAtUtc">消息发送时间（UTC）。</param>
/// <param name="CreatedByUserId">发送者用户标识；系统消息时为 null。</param>
public sealed record InboxMessageResponse(
    Guid Id,
    string Title,
    string Content,
    string Status,
    DateTimeOffset? ReadAtUtc,
    DateTimeOffset CreatedAtUtc,
    Guid? CreatedByUserId);

/// <summary>当前用户未读站内信数量，用作实时徽标的权威值。</summary>
/// <param name="UnreadCount">当前状态为 Unread 的站内信条数，最小值为 0。</param>
public sealed record InboxUnreadCountResponse(int UnreadCount);

/// <summary>Host 管理员发送站内信的请求契约，收件人由管理员指定。</summary>
/// <param name="RecipientUserId">接收者用户标识；必须属于当前租户或 Host 域。</param>
/// <param name="Title">消息标题，建议不超过 256 字符。</param>
/// <param name="Content">消息正文，支持富文本由前端渲染。</param>
public sealed record SendHostInboxMessageRequest(
    Guid RecipientUserId,
    string Title,
    string Content);

/// <summary>当前租户管理员发送站内信的请求契约；TenantId 只能来自受信会话。</summary>
/// <param name="RecipientUserId">接收者用户标识；必须属于当前租户。</param>
/// <param name="Title">消息标题，建议不超过 256 字符。</param>
/// <param name="Content">消息正文，支持富文本由前端渲染。</param>
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
