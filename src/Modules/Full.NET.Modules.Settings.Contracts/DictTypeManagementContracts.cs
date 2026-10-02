namespace Full.NET.Modules.Settings.Contracts;

/// <summary>
/// Host 作用域数据字典类型 API 的权限与契约（纵向切片 Task 1 冻结）。
/// </summary>
public static class DictTypeManagementPermissions
{
    /// <summary>分页查询字典类型列表与详情。</summary>
    public const string Read = "settings.dict_types.read";

    /// <summary>创建字典类型与字典项。</summary>
    public const string Create = "settings.dict_types.create";

    /// <summary>更新字典类型与字典项。</summary>
    public const string Update = "settings.dict_types.update";

    /// <summary>禁用字典类型与字典项。</summary>
    public const string Disable = "settings.dict_types.disable";

    /// <summary>硬删除已禁用且无活跃字典项的字典类型；字典项删除复用该权限。</summary>
    public const string Delete = "settings.dict_types.delete";

    /// <summary>迁移 067 前遗留的粗粒度写权限；不再进入可分配目录。</summary>
    public const string Write = "settings.dict_types.write";
}

/// <summary>硬删除字典类型请求；携带乐观锁版本用于并发控制。</summary>
/// <remarks>字段顺序发布后不可调整；新增字段只能追加到末尾，以保持线格式兼容。</remarks>
/// <param name="Version">当前实体乐观锁版本；与服务端不一致时拒绝删除，防止覆盖并发更新。</param>
public sealed record DeleteDictTypeRequest(int Version);

/// <summary>字典类型列表项与详情响应。</summary>
/// <remarks>字段顺序发布后不可调整；新增字段只能追加到末尾，以保持线格式兼容。</remarks>
/// <param name="Id">字典类型稳定标识。</param>
/// <param name="Code">字典类型编码；创建后不可变，供跨模块引用。</param>
/// <param name="Name">字典类型显示名称。</param>
/// <param name="Description">可选说明文案；无说明时为 <see langword="null"/>。</param>
/// <param name="DisplayOrder">同层级内的排序权重；值越小越靠前。</param>
/// <param name="IsActive">是否启用；禁用后不可再挂载新字典项。</param>
/// <param name="CreatedAtUtc">创建时间（UTC）。</param>
/// <param name="UpdatedAtUtc">最近更新时间（UTC）；从未更新时为 <see langword="null"/>。</param>
/// <param name="Version">当前乐观锁版本；用于并发写控制。</param>
public sealed record DictTypeResponse(
    Guid Id,
    string Code,
    string Name,
    string? Description,
    int DisplayOrder,
    bool IsActive,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc,
    int Version);

/// <summary>创建 Host 字典类型请求。</summary>
/// <remarks>字段顺序发布后不可调整；新增字段只能追加到末尾，以保持线格式兼容。</remarks>
/// <param name="Code">字典类型编码；创建后不可变，需全局唯一。</param>
/// <param name="Name">字典类型显示名称。</param>
/// <param name="Description">可选说明文案。</param>
/// <param name="DisplayOrder">同层级内的排序权重；值越小越靠前。</param>
public sealed record CreateDictTypeRequest(
    string Code,
    string Name,
    string? Description,
    int DisplayOrder);

/// <summary>更新 Host 字典类型请求；编码创建后不可变。</summary>
/// <remarks>字段顺序发布后不可调整；新增字段只能追加到末尾，以保持线格式兼容。</remarks>
/// <param name="Name">字典类型显示名称。</param>
/// <param name="Description">可选说明文案。</param>
/// <param name="DisplayOrder">同层级内的排序权重；值越小越靠前。</param>
/// <param name="Version">当前实体乐观锁版本；与服务端不一致时拒绝更新。</param>
public sealed record UpdateDictTypeRequest(
    string Name,
    string? Description,
    int DisplayOrder,
    int Version);
