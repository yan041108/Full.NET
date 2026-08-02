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
    Task<Result<bool>> EnsureCanWriteAsync(
        Guid tenantId,
        Guid organizationUnitId,
        Guid actorUserId,
        CancellationToken cancellationToken = default);
}