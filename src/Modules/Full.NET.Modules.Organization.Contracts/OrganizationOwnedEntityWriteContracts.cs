using Full.NET.Abstractions.Results;

namespace Full.NET.Modules.Organization.Contracts;

/// <summary>
/// 为组织归属实体写入提供可信授权边界；生成 Feature 在变更前必须调用。
/// </summary>
public interface IOrganizationOwnedEntityWriteAuthorizer
{
    /// <summary>
    /// 校验 actor 是否可向指定租户下的机构单元写入组织归属实体。
    /// </summary>
    /// <param name="tenantId">目标租户标识。</param>
    /// <param name="organizationUnitId">目标机构单元标识。</param>
    /// <param name="actorUserId">发起写入的当前用户标识。</param>
    /// <param name="cancellationToken">用于取消校验的令牌。</param>
    /// <returns>异步结果；成功时 Value 为是否允许写入，失败结果表示授权解析或租户边界错误。</returns>
    Task<Result<bool>> EnsureCanWriteAsync(
        Guid tenantId,
        Guid organizationUnitId,
        Guid actorUserId,
        CancellationToken cancellationToken = default);
}