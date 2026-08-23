namespace Full.NET.Modules.Settings.Contracts;

/// <summary>
/// Host 枚举/常量元数据目录的权限与契约。
/// </summary>
public static class EnumCatalogPermissions
{
    /// <summary>查询枚举/常量目录列表与详情。</summary>
    public const string Read = "settings.enums.read";
}

/// <summary>模块向 Settings 注册稳定枚举/常量目录的贡献者。</summary>
public interface IEnumCatalogContributor
{
    /// <summary>获取当前模块贡献的稳定枚举/常量目录定义集合。</summary>
    IReadOnlyCollection<EnumCatalogDefinition> Catalogs { get; }
}

/// <summary>一个可查询的稳定枚举/常量目录定义。</summary>
public sealed record EnumCatalogDefinition(
    string Key,
    string DisplayName,
    string? Description,
    IReadOnlyList<EnumCatalogMemberDefinition> Members);

/// <summary>目录内单个稳定成员。</summary>
public sealed record EnumCatalogMemberDefinition(
    string Code,
    string Label,
    int DisplayOrder);

/// <summary>枚举目录列表项。</summary>
public sealed record EnumCatalogSummary(
    string Key,
    string DisplayName,
    string? Description,
    int MemberCount);

/// <summary>枚举目录详情（含成员）。</summary>
public sealed record EnumCatalogDetail(
    string Key,
    string DisplayName,
    string? Description,
    IReadOnlyList<EnumCatalogMember> Members);

/// <summary>枚举目录成员响应。</summary>
public sealed record EnumCatalogMember(
    string Code,
    string Label,
    int DisplayOrder);
