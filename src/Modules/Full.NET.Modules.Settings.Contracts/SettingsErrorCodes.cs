namespace Full.NET.Modules.Settings.Contracts;

/// <summary>
/// Settings 模块对外返回的稳定错误码。
/// </summary>
public static class SettingsErrorCodes
{
    /// <summary>Settings 错误码前缀。</summary>
    public const string Prefix = "settings.";

    /// <summary>字典类型编码在 Host 全局已存在。</summary>
    public const string DictTypeCodeExists = "settings.dict_type.code_exists";

    /// <summary>目标字典类型不存在。</summary>
    public const string DictTypeNotFound = "settings.dict_type.not_found";

    /// <summary>字典类型记录版本冲突。</summary>
    public const string DictTypeVersionConflict = "settings.dict_type.version_conflict";

    /// <summary>禁用字典类型时仍存在启用中的字典项。</summary>
    public const string DictTypeHasActiveItems = "settings.dict_type.items_active";

    /// <summary>字典项稳定值在类型内已存在。</summary>
    public const string DictItemValueExists = "settings.dict_item.value_exists";

    /// <summary>目标字典项不存在。</summary>
    public const string DictItemNotFound = "settings.dict_item.not_found";

    /// <summary>字典项记录版本冲突。</summary>
    public const string DictItemVersionConflict = "settings.dict_item.version_conflict";

    /// <summary>配置键在 Host 全局已存在。</summary>
    public const string ConfigEntryKeyExists = "settings.config_entry.key_exists";

    /// <summary>目标配置项不存在。</summary>
    public const string ConfigEntryNotFound = "settings.config_entry.not_found";

    /// <summary>配置项记录版本冲突。</summary>
    public const string ConfigEntryVersionConflict = "settings.config_entry.version_conflict";

    /// <summary>目标枚举/常量目录不存在。</summary>
    public const string EnumCatalogNotFound = "settings.enum_catalog.not_found";

    /// <summary>
    /// 获取当前目录中的全部稳定错误码。
    /// </summary>
    public static IReadOnlyList<string> All { get; } = Array.AsReadOnly(
    [
        DictTypeCodeExists,
        DictTypeNotFound,
        DictTypeVersionConflict,
        DictTypeHasActiveItems,
        DictItemValueExists,
        DictItemNotFound,
        DictItemVersionConflict,
        ConfigEntryKeyExists,
        ConfigEntryNotFound,
        ConfigEntryVersionConflict,
        EnumCatalogNotFound,
    ]);
}
