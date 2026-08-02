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

    /// <summary>迁移 067 前遗留的粗粒度写权限；不再进入可分配目录。</summary>
    public const string Write = "settings.dict_types.write";
}

/// <summary>字典类型列表项与详情响应。</summary>
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
public sealed record CreateDictTypeRequest(
    string Code,
    string Name,
    string? Description,
    int DisplayOrder);

/// <summary>更新 Host 字典类型请求；编码创建后不可变。</summary>
public sealed record UpdateDictTypeRequest(
    string Name,
    string? Description,
    int DisplayOrder,
    int Version);
