namespace Full.NET.Abstractions.Tenancy;

/// <summary>
/// 跨模块只读端口：判断租户是否仍处于活动状态（未暂停/关闭）。
/// </summary>
public interface ITenantActivityReadPort
{
    Task<bool> IsActiveTenantAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default);
}
