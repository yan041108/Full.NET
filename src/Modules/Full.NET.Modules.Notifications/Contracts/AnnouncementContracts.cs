namespace Full.NET.Modules.Notifications.Contracts;

/// <summary>
/// Host 公告生命周期的状态机值，持久化与协议字段共享同一稳定字符串。
/// </summary>
/// <remarks>
/// 仅 <c>Draft</c> 可被更新；<c>Published</c> 可撤回为 <c>Retracted</c>；
/// 状态推进由 CAS 守卫，重复 publish/retract 在版本匹配时幂等返回当前事实。
/// 常量字符串发布后不可改名或删除；新增常量只能追加。
/// </remarks>
public static class AnnouncementStatuses
{
    /// <summary>草稿状态，可编辑；尚未对任何受众可见。</summary>
    public const string Draft = "draft";

    /// <summary>已发布状态，对目标受众可见；可撤回为 <c>Retracted</c>。</summary>
    public const string Published = "published";

    /// <summary>已撤回状态，不再对新受众展示；已读记录保留。</summary>
    public const string Retracted = "retracted";
}

/// <summary>Host 公告类型稳定机器码。</summary>
/// <remarks>常量字符串发布后不可改名或删除；新增常量只能追加。</remarks>
public static class AnnouncementKinds
{
    /// <summary>通知类型，偏轻量提示，通常无需正式公告流程。</summary>
    public const string Notice = "notice";

    /// <summary>公告类型，用于正式发布需受众知晓的信息。</summary>
    public const string Announcement = "announcement";
}

/// <summary>Host 公告受众范围稳定机器码。</summary>
/// <remarks>常量字符串发布后不可改名或删除；新增常量只能追加。</remarks>
public static class AnnouncementAudienceKinds
{
    /// <summary>全体受众，对所有可见用户送达。</summary>
    public const string All = "all";

    /// <summary>指定用户受众，仅对 TargetUserIds 中的用户送达。</summary>
    public const string Users = "users";

    /// <summary>指定机构受众，对 TargetOrganizations 中机构单元下的用户送达。</summary>
    public const string Organizations = "organizations";
}

/// <summary>机构受众目标；租户与机构单元标识由服务端通过 Organization 契约校验归属。</summary>
/// <param name="TenantId">目标租户标识。</param>
/// <param name="OrganizationUnitId">目标机构单元标识。</param>
public sealed record HostAnnouncementTargetOrganization(
    Guid TenantId,
    Guid OrganizationUnitId);

/// <summary>Host 公告响应契约，包含类型、受众、状态与乐观版本号。</summary>
/// <remarks>字段顺序发布后不可调整；新增字段只能追加到末尾，以保持线格式兼容。 Kind、AudienceKind、Status 为稳定机器码，发布后不得改名或删除。</remarks>
/// <param name="Id">公告标识（UUID v7）。</param>
/// <param name="Title">公告标题。</param>
/// <param name="Content">公告正文内容。</param>
/// <param name="Kind">公告类型稳定机器码。</param>
/// <param name="AudienceKind">受众范围稳定机器码。</param>
/// <param name="Status">公告当前状态稳定机器码。</param>
/// <param name="PublishedAtUtc">发布时间（UTC）；草稿状态为 <see langword="null"/>。</param>
/// <param name="PublishedByUserId">发布人用户标识；草稿状态为 <see langword="null"/>。</param>
/// <param name="RetractedAtUtc">撤回时间（UTC）；未撤回为 <see langword="null"/>。</param>
/// <param name="RetractedByUserId">撤回人用户标识；未撤回为 <see langword="null"/>。</param>
/// <param name="TargetUserIds">指定用户受众列表；非 Users 受众时为空集合。</param>
/// <param name="TargetOrganizations">机构受众列表；非 Organizations 受众时为空集合。</param>
/// <param name="CreatedAtUtc">创建时间（UTC）。</param>
/// <param name="UpdatedAtUtc">最近更新时间（UTC）；未更新为 <see langword="null"/>。</param>
/// <param name="Version">乐观并发版本号，用于 CAS 守卫。</param>
public sealed record HostAnnouncementResponse(
    Guid Id,
    string Title,
    string Content,
    string Kind,
    string AudienceKind,
    string Status,
    DateTimeOffset? PublishedAtUtc,
    Guid? PublishedByUserId,
    DateTimeOffset? RetractedAtUtc,
    Guid? RetractedByUserId,
    IReadOnlyList<Guid> TargetUserIds,
    IReadOnlyList<HostAnnouncementTargetOrganization> TargetOrganizations,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc,
    int Version);

/// <summary>创建 Host 公告的请求契约，新建公告初始为草稿状态。</summary>
/// <remarks>字段顺序发布后不可调整；新增字段只能追加到末尾，以保持线格式兼容。 Kind 与 AudienceKind 缺省时由服务端填充默认值。</remarks>
/// <param name="Title">公告标题。</param>
/// <param name="Content">公告正文内容。</param>
/// <param name="Kind">公告类型稳定机器码；缺省时由服务端决定。</param>
/// <param name="AudienceKind">受众范围稳定机器码；缺省时由服务端决定。</param>
/// <param name="TargetUserIds">指定用户受众列表；AudienceKind 非 Users 时忽略。</param>
/// <param name="TargetOrganizations">机构受众列表；AudienceKind 非 Organizations 时忽略。</param>
public sealed record CreateHostAnnouncementRequest(
    string Title,
    string Content,
    string? Kind = null,
    string? AudienceKind = null,
    IReadOnlyList<Guid>? TargetUserIds = null,
    IReadOnlyList<HostAnnouncementTargetOrganization>? TargetOrganizations = null);

/// <summary>更新草稿公告的请求契约，<c>Version</c> 用作 CAS 并发守卫的期望值。</summary>
/// <remarks>字段顺序发布后不可调整；新增字段只能追加到末尾，以保持线格式兼容。仅 Draft 状态可更新。</remarks>
/// <param name="Title">公告标题。</param>
/// <param name="Content">公告正文内容。</param>
/// <param name="Version">期望的当前版本号，不匹配时返回 CAS 冲突错误。</param>
/// <param name="Kind">公告类型稳定机器码；缺省时保持原值。</param>
/// <param name="AudienceKind">受众范围稳定机器码；缺省时保持原值。</param>
/// <param name="TargetUserIds">指定用户受众列表；AudienceKind 非 Users 时忽略。</param>
/// <param name="TargetOrganizations">机构受众列表；AudienceKind 非 Organizations 时忽略。</param>
public sealed record UpdateHostAnnouncementRequest(
    string Title,
    string Content,
    int Version,
    string? Kind = null,
    string? AudienceKind = null,
    IReadOnlyList<Guid>? TargetUserIds = null,
    IReadOnlyList<HostAnnouncementTargetOrganization>? TargetOrganizations = null);

/// <summary>发布草稿公告的请求契约，<c>Version</c> 用作 CAS 并发守卫的期望值。</summary>
/// <remarks>字段顺序发布后不可调整；新增字段只能追加到末尾，以保持线格式兼容。</remarks>
/// <param name="Version">期望的当前版本号，不匹配时返回 CAS 冲突错误。</param>
public sealed record PublishHostAnnouncementRequest(int Version);

/// <summary>撤回已发布公告的请求契约，<c>Version</c> 用作 CAS 并发守卫的期望值。</summary>
/// <remarks>字段顺序发布后不可调整；新增字段只能追加到末尾，以保持线格式兼容。</remarks>
/// <param name="Version">期望的当前版本号，不匹配时返回 CAS 冲突错误。</param>
public sealed record RetractHostAnnouncementRequest(int Version);

/// <summary>Host 公告列表查询过滤条件。</summary>
/// <remarks>字段顺序发布后不可调整；新增字段只能追加到末尾，以保持线格式兼容。 Status、Kind、AudienceKind 为稳定机器码。</remarks>
/// <param name="Title">标题模糊匹配关键字；缺省时不过滤。</param>
/// <param name="Status">按公告状态稳定机器码精确过滤；缺省时不过滤。</param>
/// <param name="Kind">按公告类型稳定机器码精确过滤；缺省时不过滤。</param>
/// <param name="AudienceKind">按受众范围稳定机器码精确过滤；缺省时不过滤。</param>
public sealed record HostAnnouncementListFilter(
    string? Title = null,
    string? Status = null,
    string? Kind = null,
    string? AudienceKind = null);

/// <summary>我收到的 Host 公告列表项。</summary>
/// <remarks>字段顺序发布后不可调整；新增字段只能追加到末尾，以保持线格式兼容。 Kind 与 AudienceKind 为稳定机器码。</remarks>
/// <param name="Id">公告标识（UUID v7）。</param>
/// <param name="Title">公告标题。</param>
/// <param name="Kind">公告类型稳定机器码。</param>
/// <param name="AudienceKind">受众范围稳定机器码。</param>
/// <param name="PublishedAtUtc">发布时间（UTC）。</param>
/// <param name="IsRead">当前用户是否已读。</param>
/// <param name="ReadAtUtc">已读时间（UTC）；未读时为 <see langword="null"/>。</param>
public sealed record ReceivedHostAnnouncementListItemResponse(
    Guid Id,
    string Title,
    string Kind,
    string AudienceKind,
    DateTimeOffset PublishedAtUtc,
    bool IsRead,
    DateTimeOffset? ReadAtUtc);

/// <summary>我收到的 Host 公告详情。</summary>
/// <remarks>字段顺序发布后不可调整；新增字段只能追加到末尾，以保持线格式兼容。 Kind 与 AudienceKind 为稳定机器码。</remarks>
/// <param name="Id">公告标识（UUID v7）。</param>
/// <param name="Title">公告标题。</param>
/// <param name="Content">公告正文内容。</param>
/// <param name="Kind">公告类型稳定机器码。</param>
/// <param name="AudienceKind">受众范围稳定机器码。</param>
/// <param name="PublishedAtUtc">发布时间（UTC）。</param>
/// <param name="PublishedByUserId">发布人用户标识；可能为 <see langword="null"/>。</param>
/// <param name="IsRead">当前用户是否已读。</param>
/// <param name="ReadAtUtc">已读时间（UTC）；未读时为 <see langword="null"/>。</param>
public sealed record ReceivedHostAnnouncementDetailResponse(
    Guid Id,
    string Title,
    string Content,
    string Kind,
    string AudienceKind,
    DateTimeOffset PublishedAtUtc,
    Guid? PublishedByUserId,
    bool IsRead,
    DateTimeOffset? ReadAtUtc);

/// <summary>我收到的 Host 公告未读计数。</summary>
/// <remarks>字段顺序发布后不可调整；新增字段只能追加到末尾，以保持线格式兼容。</remarks>
/// <param name="UnreadCount">当前用户未读公告数。</param>
public sealed record HostAnnouncementUnreadCountResponse(int UnreadCount);

/// <summary>我收到的 Host 公告列表筛选条件。</summary>
/// <remarks>字段顺序发布后不可调整；新增字段只能追加到末尾，以保持线格式兼容。</remarks>
/// <param name="Title">标题模糊匹配关键字；缺省时不过滤。</param>
/// <param name="IsRead">按已读状态过滤；缺省时不过滤。</param>
public sealed record ReceivedHostAnnouncementListFilter(
    string? Title = null,
    bool? IsRead = null);

/// <summary>Host 公告阅读统计摘要。</summary>
/// <remarks>字段顺序发布后不可调整；新增字段只能追加到末尾，以保持线格式兼容。</remarks>
/// <param name="EligibleRecipientCount">符合受众条件的应送达接收者总数。</param>
/// <param name="ReadCount">已读接收者数。</param>
/// <param name="UnreadCount">未读接收者数。</param>
public sealed record HostAnnouncementReadStatsResponse(
    long EligibleRecipientCount,
    long ReadCount,
    long UnreadCount);

/// <summary>Host 公告单条已读回执。</summary>
/// <remarks>字段顺序发布后不可调整；新增字段只能追加到末尾，以保持线格式兼容。</remarks>
/// <param name="UserId">已读用户标识。</param>
/// <param name="Username">用户登录名；可能为 <see langword="null"/>。</param>
/// <param name="DisplayName">用户展示名；可能为 <see langword="null"/>。</param>
/// <param name="ReadAtUtc">已读时间（UTC）。</param>
public sealed record HostAnnouncementReadReceiptResponse(
    Guid UserId,
    string? Username,
    string? DisplayName,
    DateTimeOffset ReadAtUtc);
