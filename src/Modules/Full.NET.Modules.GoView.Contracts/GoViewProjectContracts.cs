namespace Full.NET.Modules.GoView.Contracts;

/// <summary>GoView 大屏项目响应。</summary>
public sealed record GoViewProjectResponse(
    Guid Id,
    string ProjectKey,
    string Name,
    string CanvasJson,
    int LatestPublishedVersionNumber,
    bool IsEnabled,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc,
    int Version);

/// <summary>创建 GoView 大屏项目请求。</summary>
public sealed record CreateGoViewProjectRequest(
    string ProjectKey,
    string Name,
    string? CanvasJson,
    bool IsEnabled);

/// <summary>更新 GoView 大屏项目草稿请求。</summary>
public sealed record UpdateGoViewProjectRequest(
    string Name,
    string CanvasJson,
    bool IsEnabled,
    int Version);

/// <summary>发布 GoView 大屏项目版本请求。</summary>
public sealed record PublishGoViewProjectRequest(
    string? ChangeNote,
    int Version);

/// <summary>GoView 大屏项目发布版本响应。</summary>
public sealed record GoViewProjectVersionResponse(
    Guid Id,
    Guid ProjectId,
    int VersionNumber,
    string CanvasJson,
    string? ChangeNote,
    Guid PublishedByUserId,
    DateTimeOffset PublishedAtUtc);

/// <summary>GoView 大屏项目预览请求。</summary>
public sealed record PreviewGoViewProjectRequest(
    int? VersionNumber);

/// <summary>GoView 大屏项目只读预览响应；仅返回已发布快照，不包含数据源查询能力。</summary>
public sealed record GoViewProjectPreviewResponse(
    Guid ProjectId,
    string ProjectKey,
    string ProjectName,
    int VersionNumber,
    string CanvasJson,
    DateTimeOffset GeneratedAtUtc);
