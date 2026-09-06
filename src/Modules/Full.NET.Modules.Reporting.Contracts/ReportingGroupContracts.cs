namespace Full.NET.Modules.Reporting.Contracts;

/// <summary>报表分组响应。</summary>
/// <param name="Id">分组标识。</param>
/// <param name="ParentId">父分组标识；根分组为 <see langword="null"/>。</param>
/// <param name="Name">显示名称。</param>
/// <param name="SortOrder">排序值。</param>
/// <param name="IsEnabled">是否启用。</param>
/// <param name="CreatedAtUtc">创建时间（UTC）。</param>
/// <param name="UpdatedAtUtc">最近更新时间（UTC）。</param>
/// <param name="Version">乐观并发版本号。</param>
public sealed record ReportingGroupResponse(
    Guid Id,
    Guid? ParentId,
    string Name,
    int SortOrder,
    bool IsEnabled,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc,
    int Version);

/// <summary>创建报表分组请求。</summary>
/// <param name="ParentId">父分组标识。</param>
/// <param name="Name">显示名称。</param>
/// <param name="SortOrder">排序值。</param>
/// <param name="IsEnabled">是否启用。</param>
public sealed record CreateReportingGroupRequest(
    Guid? ParentId,
    string Name,
    int SortOrder,
    bool IsEnabled);

/// <summary>更新报表分组请求。</summary>
/// <param name="ParentId">父分组标识。</param>
/// <param name="Name">显示名称。</param>
/// <param name="SortOrder">排序值。</param>
/// <param name="IsEnabled">是否启用。</param>
/// <param name="Version">乐观并发版本号。</param>
public sealed record UpdateReportingGroupRequest(
    Guid? ParentId,
    string Name,
    int SortOrder,
    bool IsEnabled,
    int Version);
