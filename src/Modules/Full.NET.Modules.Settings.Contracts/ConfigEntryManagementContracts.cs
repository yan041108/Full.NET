namespace Full.NET.Modules.Settings.Contracts;

/// <summary>
/// Host 作用域系统配置项 API 的权限与契约。
/// </summary>
public static class ConfigEntryManagementPermissions
{
    /// <summary>分页查询配置项列表与详情。</summary>
    public const string Read = "settings.config.read";

    /// <summary>创建、更新与禁用配置项。</summary>
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
    string ValueKind,
    string Value,
    int DisplayOrder);

/// <summary>更新 Host 系统配置项请求；ConfigKey 与 ValueKind 创建后不可变。</summary>
public sealed record UpdateConfigEntryRequest(
    string DisplayName,
    string? Description,
    string Value,
    int DisplayOrder,
    int Version);
