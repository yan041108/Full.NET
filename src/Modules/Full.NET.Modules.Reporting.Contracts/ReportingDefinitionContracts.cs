namespace Full.NET.Modules.Reporting.Contracts;

/// <summary>报表定义响应（草稿工作副本）。</summary>
/// <param name="Id">定义标识。</param>
/// <param name="GroupId">所属分组标识。</param>
/// <param name="DataSourceId">绑定的数据源标识。</param>
/// <param name="DefinitionKey">稳定定义键。</param>
/// <param name="Name">显示名称。</param>
/// <param name="Description">说明。</param>
/// <param name="QueryPortKey">静态 Query Port 键。</param>
/// <param name="ParameterSchema">参数 Schema。</param>
/// <param name="LayoutConfigJson">布局配置 JSON；执行层在后续切片消费。</param>
/// <param name="LatestPublishedVersionNumber">最近发布版本号；未发布时为 0。</param>
/// <param name="IsEnabled">是否启用。</param>
/// <param name="CreatedAtUtc">创建时间（UTC）。</param>
/// <param name="UpdatedAtUtc">最近更新时间（UTC）。</param>
/// <param name="Version">乐观并发版本号。</param>
public sealed record ReportingDefinitionResponse(
    Guid Id,
    Guid GroupId,
    Guid DataSourceId,
    string DefinitionKey,
    string Name,
    string? Description,
    string QueryPortKey,
    IReadOnlyList<ReportingParameterSchemaEntry> ParameterSchema,
    string LayoutConfigJson,
    int LatestPublishedVersionNumber,
    bool IsEnabled,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc,
    int Version);

/// <summary>报表定义发布版本响应。</summary>
/// <param name="Id">版本标识。</param>
/// <param name="DefinitionId">所属定义标识。</param>
/// <param name="VersionNumber">版本号，从 1 递增。</param>
/// <param name="DataSourceId">发布时绑定的数据源标识。</param>
/// <param name="QueryPortKey">发布时绑定的 Query Port 键。</param>
/// <param name="ParameterSchema">发布时参数 Schema 快照。</param>
/// <param name="LayoutConfigJson">发布时布局配置快照。</param>
/// <param name="ChangeNote">变更说明。</param>
/// <param name="PublishedByUserId">发布人用户标识。</param>
/// <param name="PublishedAtUtc">发布时间（UTC）。</param>
public sealed record ReportingDefinitionVersionResponse(
    Guid Id,
    Guid DefinitionId,
    int VersionNumber,
    Guid DataSourceId,
    string QueryPortKey,
    IReadOnlyList<ReportingParameterSchemaEntry> ParameterSchema,
    string LayoutConfigJson,
    string? ChangeNote,
    Guid PublishedByUserId,
    DateTimeOffset PublishedAtUtc);

/// <summary>创建报表定义请求。</summary>
/// <param name="GroupId">所属分组标识。</param>
/// <param name="DataSourceId">绑定的数据源标识。</param>
/// <param name="DefinitionKey">稳定定义键；发布后不可改名。</param>
/// <param name="Name">显示名称。</param>
/// <param name="Description">说明；可为空。</param>
/// <param name="QueryPortKey">静态 Query Port 键；服务端审查实现。</param>
/// <param name="ParameterSchema">参数 Schema；覆盖 Query Port 默认展示与默认值。</param>
/// <param name="LayoutConfigJson">布局配置 JSON；草稿阶段可空，执行层在后续切片消费。</param>
/// <param name="IsEnabled">是否启用；禁用定义不出现在执行入口。</param>
public sealed record CreateReportingDefinitionRequest(
    Guid GroupId,
    Guid DataSourceId,
    string DefinitionKey,
    string Name,
    string? Description,
    string QueryPortKey,
    IReadOnlyList<ReportingParameterSchemaEntry> ParameterSchema,
    string? LayoutConfigJson,
    bool IsEnabled);

/// <summary>更新报表定义草稿请求。</summary>
/// <param name="GroupId">所属分组标识。</param>
/// <param name="DataSourceId">绑定的数据源标识。</param>
/// <param name="Name">显示名称。</param>
/// <param name="Description">说明；可为空。</param>
/// <param name="QueryPortKey">静态 Query Port 键。</param>
/// <param name="ParameterSchema">参数 Schema；整体覆盖草稿 Schema。</param>
/// <param name="LayoutConfigJson">布局配置 JSON；可为空。</param>
/// <param name="IsEnabled">是否启用。</param>
/// <param name="Version">客户端感知的草稿乐观并发版本号。</param>
public sealed record UpdateReportingDefinitionRequest(
    Guid GroupId,
    Guid DataSourceId,
    string Name,
    string? Description,
    string QueryPortKey,
    IReadOnlyList<ReportingParameterSchemaEntry> ParameterSchema,
    string? LayoutConfigJson,
    bool IsEnabled,
    int Version);

/// <summary>发布报表定义请求。</summary>
/// <param name="ChangeNote">变更说明。</param>
/// <param name="Version">客户端感知的草稿乐观并发版本号。</param>
public sealed record PublishReportingDefinitionRequest(
    string? ChangeNote,
    int Version);
