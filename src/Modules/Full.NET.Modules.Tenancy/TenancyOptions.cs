namespace Full.NET.Modules.Tenancy;

/// <summary>
/// Tenancy 模块运行时配置选项。控制宿主域白名单与解析不变量：
/// 命中 HostDomains 的请求将进入 Host Scope（无租户上下文），否则
/// 尝试用 Host 做域解析进入对应租户；Development 场景可配置本地 "localhost"
/// 作为 HostDomain 以便使用默认开发租户。
/// </summary>
internal sealed class TenancyOptions
{
    public const string SectionName = "Tenancy";

    /// <summary>
    /// 视为宿主管理端的 Host 集合（大小写不敏感，精确匹配）；匹配时跳过按域解析租户。
    /// </summary>
    public string[] HostDomains { get; set; } = [];
}
