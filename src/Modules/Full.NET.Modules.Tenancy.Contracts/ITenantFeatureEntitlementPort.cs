using Full.NET.Abstractions.Results;

namespace Full.NET.Modules.Tenancy.Contracts;

/// <summary>
/// 跨模块只读端口：按全局执行阶段判断租户是否拥有指定功能权益。
/// </summary>
public interface ITenantFeatureEntitlementPort
{
    /// <summary>
    /// 判断租户在指定权益编码下是否允许执行对应功能；Compatibility/Shadow 阶段不拒绝历史能力。
    /// </summary>
    Task<Result<bool>> IsFeatureGrantedAsync(
        Guid tenantId,
        string entitlementCode,
        CancellationToken cancellationToken = default);
}
