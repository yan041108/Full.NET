namespace Full.NET.Modules.Tenancy.Contracts;

/// <summary>
/// 表示宿主管理员可以选择的活动租户摘要。
/// </summary>
/// <param name="Id">租户标识。</param>
/// <param name="Identifier">稳定租户编码。</param>
/// <param name="Name">租户名称。</param>
/// <param name="Domain">租户主域名。</param>
public sealed record TenantContextSummary(
    Guid Id,
    string Identifier,
    string Name,
    string Domain);

/// <summary>
/// 表示进入租户或返回 Host 的上下文切换请求。
/// </summary>
/// <param name="TenantId">目标租户标识；空值表示返回 Host。</param>
public sealed record ChangeTenantContextRequest(Guid? TenantId);
