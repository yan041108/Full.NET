using Full.NET.Abstractions.OpenApi;

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
    /// <summary>字符串类型。</summary>
    public const string String = "string";

    /// <summary>布尔类型。</summary>
    public const string Boolean = "boolean";

    /// <summary>整数类型。</summary>
    public const string Integer = "integer";

    /// <summary>十进制数类型。</summary>
    public const string Decimal = "decimal";

    /// <summary>JSON 文档类型。</summary>
    public const string Json = "json";

    /// <summary>敏感密钥；读 API 脱敏，跨模块明文仅经 <see cref="ISettingsSecretValueResolver"/> 解析。</summary>
    public const string Secret = "secret";

    /// <summary>已发布的全部配置值类型稳定机器码集合。</summary>
    public static IReadOnlyList<string> All { get; } = Array.AsReadOnly(
    [
        String,
        Boolean,
        Integer,
        Decimal,
        Json,
        Secret,
    ]);
}

/// <summary>系统配置项列表项与详情响应。</summary>
/// <remarks>字段顺序发布后不可调整；新增字段只能追加到末尾，以保持线格式兼容。ValueKind 为稳定机器码，发布后不可改名或删除。</remarks>
/// <param name="Id">配置项标识。</param>
/// <param name="ConfigKey">稳定机器码；发布后不可改名或删除。</param>
/// <param name="DisplayName">展示名称。</param>
/// <param name="Description">描述；可为空。</param>
/// <param name="GroupName">分组名；可为空。</param>
/// <param name="ValueKind">值类型稳定机器码，取 <see cref="ConfigValueKinds"/> 集合。</param>
/// <param name="Value">值文本；secret 类型在此脱敏，明文经 <see cref="ISettingsSecretValueResolver"/> 解析。</param>
/// <param name="HasValue">是否已设置值；与 Value 是否为空无关。</param>
/// <param name="DisplayOrder">展示顺序，值小者靠前。</param>
/// <param name="IsActive">是否启用；禁用项不出现在可读列表。</param>
/// <param name="CreatedAtUtc">创建时间（UTC）。</param>
/// <param name="UpdatedAtUtc">最近更新时间（UTC）；可为空。</param>
/// <param name="Version">乐观锁版本号。</param>
public sealed record ConfigEntryResponse(
    Guid Id,
    string ConfigKey,
    string DisplayName,
    string? Description,
    string? GroupName,
    [property: FullNetOpenApiStringEnum(
        ConfigValueKinds.String,
        ConfigValueKinds.Boolean,
        ConfigValueKinds.Integer,
        ConfigValueKinds.Decimal,
        ConfigValueKinds.Json,
        ConfigValueKinds.Secret)]
    string ValueKind,
    string Value,
    bool HasValue,
    int DisplayOrder,
    bool IsActive,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc,
    int Version);

/// <summary>创建 Host 系统配置项请求。</summary>
/// <remarks>字段顺序发布后不可调整；新增字段只能追加到末尾，以保持线格式兼容。ConfigKey 与 ValueKind 创建后不可变。</remarks>
/// <param name="ConfigKey">稳定机器码；发布后不可改名或删除。</param>
/// <param name="DisplayName">展示名称。</param>
/// <param name="Description">描述；可为空。</param>
/// <param name="GroupName">分组名；可为空。</param>
/// <param name="ValueKind">值类型稳定机器码，取 <see cref="ConfigValueKinds"/> 集合。</param>
/// <param name="Value">值文本；secret 类型仅写入不回显明文。</param>
/// <param name="DisplayOrder">展示顺序，值小者靠前。</param>
public sealed record CreateConfigEntryRequest(
    string ConfigKey,
    string DisplayName,
    string? Description,
    string? GroupName,
    [property: FullNetOpenApiStringEnum(
        ConfigValueKinds.String,
        ConfigValueKinds.Boolean,
        ConfigValueKinds.Integer,
        ConfigValueKinds.Decimal,
        ConfigValueKinds.Json,
        ConfigValueKinds.Secret)]
    string ValueKind,
    string Value,
    int DisplayOrder);

/// <summary>更新 Host 系统配置项请求；ConfigKey 与 ValueKind 创建后不可变。</summary>
/// <remarks>字段顺序发布后不可调整；新增字段只能追加到末尾，以保持线格式兼容。</remarks>
/// <param name="DisplayName">展示名称。</param>
/// <param name="Description">描述；可为空。</param>
/// <param name="GroupName">分组名；可为空。</param>
/// <param name="Value">值文本；secret 类型仅写入不回显明文。</param>
/// <param name="DisplayOrder">展示顺序，值小者靠前。</param>
/// <param name="Version">乐观锁版本号，用于 CAS 并发控制。</param>
public sealed record UpdateConfigEntryRequest(
    string DisplayName,
    string? Description,
    string? GroupName,
    string Value,
    int DisplayOrder,
    int Version);

/// <summary>硬删除配置项请求；携带乐观锁版本用于并发控制。</summary>
/// <param name="Version">乐观锁版本号，用于 CAS 并发控制。</param>
public sealed record DeleteConfigEntryRequest(int Version);

/// <summary>批量硬删除配置项请求；仅删除已禁用项，任一项未禁用则整体拒绝。</summary>
/// <param name="Ids">待删除配置项标识集合；调用方需确保全部已禁用。</param>
public sealed record BatchDeleteConfigEntriesRequest(IReadOnlyCollection<Guid> Ids);

/// <summary>批量更新配置项值请求；按 ConfigKey 定位并校验值类型后更新。</summary>
/// <param name="Updates">批量更新项集合；按 ConfigKey 定位。</param>
public sealed record BatchUpdateConfigValuesRequest(IReadOnlyCollection<ConfigValueUpdate> Updates);

/// <summary>单个配置项值更新项。</summary>
/// <param name="ConfigKey">稳定机器码；按此键定位配置项。</param>
/// <param name="Value">新值文本；必须匹配目标项的 ValueKind 类型。</param>
public sealed record ConfigValueUpdate(string ConfigKey, string Value);
