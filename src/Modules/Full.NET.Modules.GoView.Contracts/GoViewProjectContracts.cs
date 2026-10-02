namespace Full.NET.Modules.GoView.Contracts;

/// <summary>GoView 大屏项目响应。</summary>
/// <remarks>字段顺序发布后不可调整；新增字段只能追加到末尾，以保持线格式兼容。ProjectKey 稳定键发布后不得改名或删除。</remarks>
/// <param name="Id">项目唯一标识。</param>
/// <param name="ProjectKey">稳定项目键，前端与外部系统据此引用项目。</param>
/// <param name="Name">项目展示名称。</param>
/// <param name="CanvasJson">草稿画布 JSON 文本；可包含未发布变更。</param>
/// <param name="LatestPublishedVersionNumber">最近发布版本号；从未发布时为 0。</param>
/// <param name="IsEnabled">是否启用；停用后预览仍可访问但禁止发布。</param>
/// <param name="CreatedAtUtc">项目创建时间（UTC）。</param>
/// <param name="UpdatedAtUtc">项目最近更新时间（UTC）；未更新时为 <see langword="null"/>。</param>
/// <param name="Version">CAS 乐观并发期望值；草稿与发布均递增。</param>
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
/// <remarks>字段顺序发布后不可调整；新增字段只能追加到末尾，以保持线格式兼容。</remarks>
/// <param name="ProjectKey">稳定项目键，发布后不可改名或删除；建议使用稳定域名前缀。</param>
/// <param name="Name">项目展示名称。</param>
/// <param name="CanvasJson">初始草稿画布 JSON；为 <see langword="null"/> 时使用空白画布。</param>
/// <param name="IsEnabled">是否启用；为 <see langword="false"/> 时仅创建不暴露预览入口。</param>
public sealed record CreateGoViewProjectRequest(
    string ProjectKey,
    string Name,
    string? CanvasJson,
    bool IsEnabled);

/// <summary>更新 GoView 大屏项目草稿请求。</summary>
/// <remarks>字段顺序发布后不可调整；新增字段只能追加到末尾，以保持线格式兼容。</remarks>
/// <param name="Name">项目展示名称。</param>
/// <param name="CanvasJson">草稿画布 JSON 文本；非空时整体覆盖现值。</param>
/// <param name="IsEnabled">是否启用；停用后禁止发布。</param>
/// <param name="Version">CAS 乐观并发期望值；不匹配当前草稿 Version 时拒绝。</param>
public sealed record UpdateGoViewProjectRequest(
    string Name,
    string CanvasJson,
    bool IsEnabled,
    int Version);

/// <summary>发布 GoView 大屏项目版本请求。</summary>
/// <remarks>字段顺序发布后不可调整；新增字段只能追加到末尾，以保持线格式兼容。发布后版本号不可回收，已发布快照不可修改。</remarks>
/// <param name="ChangeNote">发布说明，仅接受短稳定文本；为 <see langword="null"/> 时不记录说明。</param>
/// <param name="Version">CAS 乐观并发期望值；不匹配当前草稿 Version 时拒绝发布。</param>
public sealed record PublishGoViewProjectRequest(
    string? ChangeNote,
    int Version);

/// <summary>GoView 大屏项目发布版本响应。</summary>
/// <remarks>字段顺序发布后不可调整；新增字段只能追加到末尾，以保持线格式兼容。已发布快照不可修改或回收。</remarks>
/// <param name="Id">发布版本记录唯一标识。</param>
/// <param name="ProjectId">所属项目 Id。</param>
/// <param name="VersionNumber">发布版本号，单调递增，不可回收。</param>
/// <param name="CanvasJson">发布快照画布 JSON 文本；为发布时刻冻结副本。</param>
/// <param name="ChangeNote">发布说明；未记录时为 <see langword="null"/>。</param>
/// <param name="PublishedByUserId">发布操作人用户 Id，用于审计与归属。</param>
/// <param name="PublishedAtUtc">发布时间（UTC）。</param>
public sealed record GoViewProjectVersionResponse(
    Guid Id,
    Guid ProjectId,
    int VersionNumber,
    string CanvasJson,
    string? ChangeNote,
    Guid PublishedByUserId,
    DateTimeOffset PublishedAtUtc);

/// <summary>GoView 大屏项目预览请求。</summary>
/// <remarks>字段顺序发布后不可调整；新增字段只能追加到末尾，以保持线格式兼容。</remarks>
/// <param name="VersionNumber">指定预览的已发布版本号；为 <see langword="null"/> 时回退到最近发布版本。</param>
public sealed record PreviewGoViewProjectRequest(
    int? VersionNumber);

/// <summary>GoView 大屏项目只读预览响应；仅返回已发布快照，不包含数据源查询能力。</summary>
/// <remarks>字段顺序发布后不可调整；新增字段只能追加到末尾，以保持线格式兼容。ProjectKey 稳定键发布后不得改名。</remarks>
/// <param name="ProjectId">所属项目 Id。</param>
/// <param name="ProjectKey">稳定项目键，前端据此识别项目。</param>
/// <param name="ProjectName">项目展示名称快照。</param>
/// <param name="VersionNumber">预览对应的已发布版本号。</param>
/// <param name="CanvasJson">预览画布 JSON 文本；为发布快照，不含数据源查询。</param>
/// <param name="GeneratedAtUtc">预览生成时间（UTC）。</param>
public sealed record GoViewProjectPreviewResponse(
    Guid ProjectId,
    string ProjectKey,
    string ProjectName,
    int VersionNumber,
    string CanvasJson,
    DateTimeOffset GeneratedAtUtc);
