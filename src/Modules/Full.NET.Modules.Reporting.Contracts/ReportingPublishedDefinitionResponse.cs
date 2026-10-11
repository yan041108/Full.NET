namespace Full.NET.Modules.Reporting.Contracts;

/// <summary>租户获授发布版本的执行目录，不暴露草稿或数据源连接配置。</summary>
/// <remarks>版本授权独立于执行权限；新发布版本必须由 Host 单独授予。</remarks>
/// <param name="DefinitionId">报表定义标识。</param>
/// <param name="DefinitionKey">稳定定义键。</param>
/// <param name="Name">报表名称。</param>
/// <param name="VersionNumber">明确获授的发布版本号。</param>
/// <param name="QueryPortKey">受控查询端口键。</param>
/// <param name="ParameterSchema">发布时参数结构。</param>
/// <param name="LayoutConfigJson">发布时布局。</param>
public sealed record ReportingPublishedDefinitionResponse(
    Guid DefinitionId, string DefinitionKey, string Name, int VersionNumber,
    string QueryPortKey, IReadOnlyList<ReportingParameterSchemaEntry> ParameterSchema,
    string LayoutConfigJson);
