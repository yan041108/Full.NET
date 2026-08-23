namespace Full.NET.Modules.Notifications.Contracts;

/// <summary>
/// Host 公告生命周期的状态机值，持久化与协议字段共享同一稳定字符串。
/// </summary>
/// <remarks>
/// 仅 <c>Draft</c> 可被更新或发布；<c>Published</c> 不可再编辑，状态推进由 CAS 守卫。
/// </remarks>
public static class AnnouncementStatuses
{
    public const string Draft = "draft";

    public const string Published = "published";
}

/// <summary>Host 公告响应契约，包含状态与乐观版本号。</summary>
public sealed record HostAnnouncementResponse(
    Guid Id,
    string Title,
    string Content,
    string Status,
    DateTimeOffset? PublishedAtUtc,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc,
    int Version);

/// <summary>创建 Host 公告的请求契约，新建公告初始为草稿状态。</summary>
public sealed record CreateHostAnnouncementRequest(
    string Title,
    string Content);

/// <summary>更新草稿公告的请求契约，<c>Version</c> 用作 CAS 并发守卫的期望值。</summary>
public sealed record UpdateHostAnnouncementRequest(
    string Title,
    string Content,
    int Version);

/// <summary>发布草稿公告的请求契约，<c>Version</c> 用作 CAS 并发守卫的期望值。</summary>
public sealed record PublishHostAnnouncementRequest(int Version);
