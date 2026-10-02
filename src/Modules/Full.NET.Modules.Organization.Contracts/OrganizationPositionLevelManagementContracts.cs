namespace Full.NET.Modules.Organization.Contracts;

/// <summary>
/// 租户职级管理 API 的权限契约。
/// </summary>
public static class OrganizationPositionLevelManagementPermissions
{
    /// <summary>分页查询租户职级列表与详情。</summary>
    public const string Read = "organization.position_levels.read";

    /// <summary>创建租户职级。</summary>
    public const string Create = "organization.position_levels.create";

    /// <summary>更新租户职级。</summary>
    public const string Update = "organization.position_levels.update";

    /// <summary>禁用租户职级。</summary>
    public const string Disable = "organization.position_levels.disable";

    /// <summary>迁移 064 前遗留的粗粒度写权限；不再进入可分配目录。</summary>
    public const string Write = "organization.position_levels.write";
}

/// <summary>创建租户职级请求。</summary>
/// <remarks>字段顺序发布后不可调整；新增字段只能追加到末尾，以保持线格式兼容。Code 为租户内唯一稳定键，发布后不可改名。</remarks>
/// <param name="Code">租户内唯一的职级代码；发布后不可改名。</param>
/// <param name="Name">职级展示名称。</param>
/// <param name="DisplayOrder">展示排序；数值越小越靠前。</param>
public sealed record CreateOrganizationPositionLevelRequest(
    string Code,
    string Name,
    int DisplayOrder);

/// <summary>更新租户职级请求。</summary>
/// <remarks>字段顺序发布后不可调整；新增字段只能追加到末尾，以保持线格式兼容。不允许修改 Code，仅可修改名称与排序。</remarks>
/// <param name="Name">更新后的职级展示名称。</param>
/// <param name="DisplayOrder">更新后的展示排序；数值越小越靠前。</param>
/// <param name="Version">预期版本号；用于 CAS 乐观并发，防止覆盖他人更新。</param>
public sealed record UpdateOrganizationPositionLevelRequest(
    string Name,
    int DisplayOrder,
    int Version);

/// <summary>租户职级列表项与详情响应。</summary>
/// <remarks>字段顺序发布后不可调整；新增字段只能追加到末尾，以保持线格式兼容。</remarks>
/// <param name="Id">职级稳定标识。</param>
/// <param name="Code">租户内唯一的职级代码；发布后不可改名。</param>
/// <param name="Name">职级展示名称。</param>
/// <param name="DisplayOrder">展示排序；数值越小越靠前。</param>
/// <param name="IsActive">职级是否启用；禁用后不再出现在可选列表中。</param>
/// <param name="CreatedAtUtc">职级创建时间（UTC）。</param>
/// <param name="UpdatedAtUtc">职级最近更新时间（UTC）；从未更新时为 <see langword="null"/>。</param>
/// <param name="Version">职级当前版本号；每次变更递增。</param>
public sealed record OrganizationPositionLevelResponse(
    Guid Id,
    string Code,
    string Name,
    int DisplayOrder,
    bool IsActive,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc,
    int Version);
