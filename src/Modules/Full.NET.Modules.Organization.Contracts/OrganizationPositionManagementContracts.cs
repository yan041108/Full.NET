namespace Full.NET.Modules.Organization.Contracts;

/// <summary>
/// 租户职位管理 API 的请求与响应契约。
/// </summary>
public static class OrganizationPositionManagementPermissions
{
    /// <summary>分页查询租户职位列表与详情。</summary>
    public const string Read = "organization.positions.read";

    /// <summary>创建租户职位。</summary>
    public const string Create = "organization.positions.create";

    /// <summary>更新租户职位。</summary>
    public const string Update = "organization.positions.update";

    /// <summary>禁用租户职位。</summary>
    public const string Disable = "organization.positions.disable";

    /// <summary>绑定或解绑职位所属机构。</summary>
    public const string AssignUnit = "organization.positions.assign_unit";

    /// <summary>绑定或解绑职位所属职级。</summary>
    public const string AssignPositionLevel = "organization.positions.assign_position_level";

    /// <summary>导出当前租户职位目录。</summary>
    public const string Export = "organization.positions.export";

    /// <summary>按固定模板批量导入租户职位。</summary>
    public const string Import = "organization.positions.import";

    /// <summary>迁移 063 前遗留的粗粒度写权限；不再进入可分配目录。</summary>
    public const string Write = "organization.positions.write";
}

/// <summary>创建租户职位请求。</summary>
/// <remarks>字段顺序发布后不可调整；新增字段只能追加到末尾，以保持线格式兼容。</remarks>
/// <param name="Code">租户内唯一职位编码；发布后不可改名。</param>
/// <param name="Name">职位显示名称。</param>
/// <param name="DisplayOrder">列表排序权重；值小在前。</param>
public sealed record CreateOrganizationPositionRequest(
    string Code,
    string Name,
    int DisplayOrder);

/// <summary>更新租户职位请求。</summary>
/// <remarks>字段顺序发布后不可调整；新增字段只能追加到末尾，以保持线格式兼容。</remarks>
/// <param name="Name">职位显示名称。</param>
/// <param name="DisplayOrder">列表排序权重；值小在前。</param>
/// <param name="Version">乐观并发版本；必须与当前持久化版本一致。</param>
public sealed record UpdateOrganizationPositionRequest(
    string Name,
    int DisplayOrder,
    int Version);

/// <summary>绑定或解绑职位所属机构；空机构标识表示解除现有绑定。</summary>
/// <remarks>字段顺序发布后不可调整；新增字段只能追加到末尾，以保持线格式兼容。</remarks>
/// <param name="UnitId">目标机构标识；为空表示解除现有机构绑定。</param>
/// <param name="Version">乐观并发版本；必须与当前持久化版本一致。</param>
public sealed record AssignOrganizationPositionUnitRequest(
    Guid? UnitId,
    int Version);

/// <summary>绑定或解绑职位所属职级；空职级标识表示解除现有绑定。</summary>
/// <remarks>字段顺序发布后不可调整；新增字段只能追加到末尾，以保持线格式兼容。</remarks>
/// <param name="PositionLevelId">目标职级标识；为空表示解除现有职级绑定。</param>
/// <param name="Version">乐观并发版本；必须与当前持久化版本一致。</param>
public sealed record AssignOrganizationPositionLevelRequest(
    Guid? PositionLevelId,
    int Version);

/// <summary>租户职位列表项与详情响应。</summary>
/// <remarks>字段顺序发布后不可调整；新增字段只能追加到末尾，以保持线格式兼容。 Code、UnitCode、PositionLevelCode 等稳定键发布后不可改名或删除。</remarks>
/// <param name="Id">职位标识。</param>
/// <param name="Code">租户内唯一职位编码。</param>
/// <param name="Name">职位显示名称。</param>
/// <param name="UnitId">所属机构标识；为空表示未绑定。</param>
/// <param name="UnitCode">所属机构编码；为空表示未绑定。</param>
/// <param name="UnitName">所属机构名称；为空表示未绑定。</param>
/// <param name="PositionLevelId">所属职级标识；为空表示未绑定。</param>
/// <param name="PositionLevelCode">所属职级编码；为空表示未绑定。</param>
/// <param name="PositionLevelName">所属职级名称；为空表示未绑定。</param>
/// <param name="DisplayOrder">列表排序权重；值小在前。</param>
/// <param name="IsActive">是否启用；false 表示已被禁用。</param>
/// <param name="CreatedAtUtc">创建时间（UTC）。</param>
/// <param name="UpdatedAtUtc">最近更新时间（UTC）；为空表示从未更新。</param>
/// <param name="Version">乐观并发版本；用于后续更新校验。</param>
public sealed record OrganizationPositionResponse(
    Guid Id,
    string Code,
    string Name,
    Guid? UnitId,
    string? UnitCode,
    string? UnitName,
    Guid? PositionLevelId,
    string? PositionLevelCode,
    string? PositionLevelName,
    int DisplayOrder,
    bool IsActive,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc,
    int Version);

/// <summary>职位导入单行载荷。</summary>
/// <param name="Code">租户内唯一职位编码。</param>
/// <param name="Name">职位显示名称。</param>
/// <param name="DisplayOrder">列表排序权重。</param>
/// <param name="UnitCode">可选机构编码；提供时要求导入方具备机构绑定权限。</param>
/// <param name="PositionLevelCode">可选职级编码；提供时要求导入方具备职级绑定权限。</param>
public sealed record ImportOrganizationPositionRow(
    string Code,
    string Name,
    int DisplayOrder,
    string? UnitCode,
    string? PositionLevelCode);

/// <summary>批量导入租户职位请求。</summary>
/// <param name="Rows">按工作簿行顺序提交的导入行。</param>
public sealed record ImportOrganizationPositionsRequest(
    IReadOnlyList<ImportOrganizationPositionRow> Rows);

/// <summary>职位导入单行结果。</summary>
/// <param name="Line">原始工作簿行号（从 1 开始）。</param>
/// <param name="Succeeded">本行是否导入成功。</param>
/// <param name="PositionId">成功时返回职位标识。</param>
/// <param name="ErrorCode">失败时返回稳定错误码。</param>
/// <param name="Message">失败时的可读说明。</param>
public sealed record ImportOrganizationPositionRowResult(
    int Line,
    bool Succeeded,
    Guid? PositionId,
    string? ErrorCode,
    string? Message);

/// <summary>职位导入汇总。</summary>
/// <param name="SucceededCount">成功导入行数。</param>
/// <param name="Results">逐行结果，顺序与请求一致。</param>
public sealed record ImportOrganizationPositionsResponse(
    int SucceededCount,
    IReadOnlyList<ImportOrganizationPositionRowResult> Results);
