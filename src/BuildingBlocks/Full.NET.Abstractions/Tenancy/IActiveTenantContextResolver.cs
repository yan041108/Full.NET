namespace Full.NET.Abstractions.Tenancy;

/// <summary>
/// 按稳定租户标识解析活动租户上下文，供跨模块用例建立受控租户作用域。
/// </summary>
public interface IActiveTenantContextResolver
{
    Task<TenantContext?> ResolveActiveByIdAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default);
}
