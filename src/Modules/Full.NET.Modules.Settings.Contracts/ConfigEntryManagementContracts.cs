namespace Full.NET.Modules.Settings.Contracts;

/// <summary>
/// Host 作用域系统配置项 API 的权限与契约。
/// </summary>
public static class ConfigEntryManagementPermissions
{
    /// <summary>分页查询配置项列表与详情。</summary>
    public const string Read = "settings.config.read";

    /// <summary>创建配置项。</summary>
    public const string Create = "settings.config.create";

    /// <summary>更新配置项。</summary>
    public const string Update = "settings.config.update";

    /// <summary>禁用配置项。</summary>
    public const string Disable = "settings.config.disable";

    /// <summary>硬删除已禁用的配置项。</summary>
    public const string Delete = "settings.config.delete";

    /// <summary>迁移 069 前遗留的粗粒度写权限；不再进入可分配目录。</summary>
    public const string Write = "settings.config.write";
}

/// <summary>配置值类型稳定机器码。</summary>
public static class ConfigValueKinds
{
    public const string String = "string";
    public const string Boolean = "boolean";
    public const string Integer = "integer";
    public const string Decimal = "decimal";
    public const string Json = "json";

    public static IReadOnlyList<string> All { get; } = Array.AsReadOnly(
    [
        String,
        Boolean,
        Integer,
        Decimal,
        Json,
    ]);
}

/// <summary>系统配置项列表项与详情响应。</summary>
public sealed record ConfigEntryResponse(
    Guid Id,
    string ConfigKey,
    string DisplayName,
    string? Description,
    string? GroupName,
    string ValueKind,
    string Value,
    int DisplayOrder,
    bool IsActive,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc,
    int Version);

/// <summary>创建 Host 系统配置项请求。</summary>
public sealed record CreateConfigEntryRequest(
    string ConfigKey,
    string DisplayName,
    string? Description,
    string? GroupName,
    string ValueKind,
    string Value,
    int DisplayOrder);

/// <summary>更新 Host 系统配置项请求；ConfigKey 与 ValueKind 创建后不可变。</summary>
public sealed record UpdateConfigEntryRequest(
    string DisplayName,
    string? Description,
    string? GroupName,
    string Value,
    int DisplayOrder,
    int Version);

/// <summary>硬删除配置项请求；携带乐观锁版本用于并发控制。</summary>
public sealed record DeleteConfigEntryRequest(int Version);

/// <summary>批量硬删除配置项请求；仅删除已禁用项，任一项未禁用则整体拒绝。</summary>
public sealed record BatchDeleteConfigEntriesRequest(IReadOnlyCollection<Guid> Ids);

/// <summary>批量更新配置项值请求；按 ConfigKey 定位并校验值类型后更新。</summary>
public sealed record BatchUpdateConfigValuesRequest(IReadOnlyCollection<ConfigValueUpdate> Updates);

/// <summary>单个配置项值更新项。</summary>
public sealed record ConfigValueUpdate(string ConfigKey, string Value);
