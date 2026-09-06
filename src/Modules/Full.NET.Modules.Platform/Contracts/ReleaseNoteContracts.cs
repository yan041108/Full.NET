namespace Full.NET.Modules.Platform.Contracts;

/// <summary>
/// 更新日志生命周期的状态机值，持久化与协议字段共享同一稳定字符串。
/// </summary>
public static class ReleaseNoteStatuses
{
    /// <summary>草稿，可编辑或删除。</summary>
    public const string Draft = "draft";

    /// <summary>已发布，对用户可见。</summary>
    public const string Published = "published";

    /// <summary>已撤回，不再对用户展示。</summary>
    public const string Retracted = "retracted";
}

/// <summary>Host 更新日志响应契约，包含版本标签、排序键与乐观版本号。</summary>
/// <param name="Id">更新日志标识。</param>
/// <param name="VersionLabel">展示用版本标签，如 1.2.3。</param>
/// <param name="VersionSortKey">内部排序键，由版本标签解析得到。</param>
/// <param name="Title">标题。</param>
/// <param name="Content">正文内容。</param>
/// <param name="Status">生命周期状态。</param>
/// <param name="PublishedAtUtc">发布时间（UTC）。</param>
/// <param name="PublishedByUserId">发布人用户标识。</param>
/// <param name="RetractedAtUtc">撤回时间（UTC）。</param>
/// <param name="RetractedByUserId">撤回人用户标识。</param>
/// <param name="CreatedAtUtc">创建时间（UTC）。</param>
/// <param name="UpdatedAtUtc">更新时间（UTC）。</param>
/// <param name="Version">乐观并发版本号。</param>
public sealed record HostReleaseNoteResponse(
    Guid Id,
    string VersionLabel,
    long VersionSortKey,
    string Title,
    string Content,
    string Status,
    DateTimeOffset? PublishedAtUtc,
    Guid? PublishedByUserId,
    DateTimeOffset? RetractedAtUtc,
    Guid? RetractedByUserId,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc,
    int Version);

/// <summary>当前用户可见的已发布更新日志响应，附带已读状态。</summary>
/// <param name="Id">更新日志标识。</param>
/// <param name="VersionLabel">展示用版本标签。</param>
/// <param name="VersionSortKey">内部排序键。</param>
/// <param name="Title">标题。</param>
/// <param name="Content">正文内容。</param>
/// <param name="PublishedAtUtc">发布时间（UTC）。</param>
/// <param name="IsRead">当前用户是否已读。</param>
/// <param name="ReadAtUtc">当前用户标记已读时间（UTC）；未读时为 null。</param>
public sealed record MyReleaseNoteResponse(
    Guid Id,
    string VersionLabel,
    long VersionSortKey,
    string Title,
    string Content,
    DateTimeOffset PublishedAtUtc,
    bool IsRead,
    DateTimeOffset? ReadAtUtc);

/// <summary>创建 Host 更新日志草稿的请求契约。</summary>
/// <param name="VersionLabel">版本标签，必须符合 semver 风格解析规则。</param>
/// <param name="Title">标题。</param>
/// <param name="Content">正文内容。</param>
public sealed record CreateHostReleaseNoteRequest(
    string VersionLabel,
    string Title,
    string Content);

/// <summary>更新草稿更新日志的请求契约，<c>Version</c> 用作 CAS 并发守卫的期望值。</summary>
/// <param name="VersionLabel">版本标签。</param>
/// <param name="Title">标题。</param>
/// <param name="Content">正文内容。</param>
/// <param name="Version">客户端感知的乐观并发版本号。</param>
public sealed record UpdateHostReleaseNoteRequest(
    string VersionLabel,
    string Title,
    string Content,
    int Version);

/// <summary>发布草稿更新日志的请求契约。</summary>
/// <param name="Version">客户端感知的乐观并发版本号。</param>
public sealed record PublishHostReleaseNoteRequest(int Version);

/// <summary>撤回已发布更新日志的请求契约。</summary>
/// <param name="Version">客户端感知的乐观并发版本号。</param>
public sealed record RetractHostReleaseNoteRequest(int Version);

/// <summary>删除草稿更新日志的请求契约。</summary>
/// <param name="Version">客户端感知的乐观并发版本号。</param>
public sealed record DeleteHostReleaseNoteRequest(int Version);

/// <summary>Host 更新日志列表查询过滤条件。</summary>
/// <param name="Title">按标题模糊匹配；为空时不限制。</param>
/// <param name="Status">按状态精确匹配；为空时不限制。</param>
/// <param name="VersionLabel">按版本标签模糊匹配；为空时不限制。</param>
public sealed record HostReleaseNoteListFilter(
    string? Title = null,
    string? Status = null,
    string? VersionLabel = null);
