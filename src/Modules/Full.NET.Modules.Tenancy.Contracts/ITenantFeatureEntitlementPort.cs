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
    /// <param name="tenantId">待校验的租户标识。</param>
    /// <param name="entitlementCode">权益稳定编码。</param>
    /// <param name="cancellationToken">用于取消查询的令牌。</param>
    /// <returns>异步结果；成功时 Value 为是否授予，失败结果表示权益解析或租户查询错误。</returns>
    Task<Result<bool>> IsFeatureGrantedAsync(
        Guid tenantId,
        string entitlementCode,
        CancellationToken cancellationToken = default);
}
