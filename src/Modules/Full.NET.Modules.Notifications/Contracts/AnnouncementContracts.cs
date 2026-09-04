namespace Full.NET.Modules.Notifications.Contracts;

/// <summary>
/// Host 公告生命周期的状态机值，持久化与协议字段共享同一稳定字符串。
/// </summary>
/// <remarks>
/// 仅 <c>Draft</c> 可被更新；<c>Published</c> 可撤回为 <c>Retracted</c>；
/// 状态推进由 CAS 守卫，重复 publish/retract 在版本匹配时幂等返回当前事实。
/// </remarks>
public static class AnnouncementStatuses
{
    public const string Draft = "draft";

    public const string Published = "published";

    public const string Retracted = "retracted";
}

/// <summary>Host 公告类型稳定机器码。</summary>
public static class AnnouncementKinds
{
    public const string Notice = "notice";

    public const string Announcement = "announcement";
}

/// <summary>Host 公告受众范围稳定机器码。</summary>
public static class AnnouncementAudienceKinds
{
    public const string All = "all";

    public const string Users = "users";

    public const string Organizations = "organizations";
}

/// <summary>机构受众目标；租户与机构单元标识由服务端通过 Organization 契约校验归属。</summary>
/// <param name="TenantId">目标租户标识。</param>
/// <param name="OrganizationUnitId">目标机构单元标识。</param>
public sealed record HostAnnouncementTargetOrganization(
    Guid TenantId,
    Guid OrganizationUnitId);

/// <summary>Host 公告响应契约，包含类型、受众、状态与乐观版本号。</summary>
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
public sealed record CreateHostAnnouncementRequest(
    string Title,
    string Content,
    string? Kind = null,
    string? AudienceKind = null,
    IReadOnlyList<Guid>? TargetUserIds = null,
    IReadOnlyList<HostAnnouncementTargetOrganization>? TargetOrganizations = null);

/// <summary>更新草稿公告的请求契约，<c>Version</c> 用作 CAS 并发守卫的期望值。</summary>
public sealed record UpdateHostAnnouncementRequest(
    string Title,
    string Content,
    int Version,
    string? Kind = null,
    string? AudienceKind = null,
    IReadOnlyList<Guid>? TargetUserIds = null,
    IReadOnlyList<HostAnnouncementTargetOrganization>? TargetOrganizations = null);

/// <summary>发布草稿公告的请求契约，<c>Version</c> 用作 CAS 并发守卫的期望值。</summary>
public sealed record PublishHostAnnouncementRequest(int Version);

/// <summary>撤回已发布公告的请求契约，<c>Version</c> 用作 CAS 并发守卫的期望值。</summary>
public sealed record RetractHostAnnouncementRequest(int Version);

/// <summary>Host 公告列表查询过滤条件。</summary>
public sealed record HostAnnouncementListFilter(
    string? Title = null,
    string? Status = null,
    string? Kind = null,
    string? AudienceKind = null);
