using Full.NET.Abstractions.Results;

namespace Full.NET.Modules.Identity.Contracts;

/// <summary>
/// 管理受保护超级管理员关系；调用方仍须在入口执行重新认证和高风险确认。
/// </summary>
public interface ISuperAdministratorService
{
    /// <summary>
    /// 将现有有效 Host 账号授予超级管理员系统角色。
    /// </summary>
    /// <param name="operatorUserId">执行高风险变更的活动超级管理员用户标识。</param>
    /// <param name="targetUserId">待授予角色的活动 Host 用户标识。</param>
    /// <param name="cancellationToken">用于取消数据库事务的令牌。</param>
    /// <returns>包含是否发生实际变更的结果；前置条件不满足时返回稳定失败码。</returns>
    Task<Result<SuperAdministratorChangeResponse>> GrantAsync(
        Guid operatorUserId,
        Guid targetUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 撤销超级管理员关系，同时保证系统至少保留一名有效超级管理员。
    /// </summary>
    /// <param name="operatorUserId">执行高风险变更的活动超级管理员用户标识。</param>
    /// <param name="targetUserId">待撤销角色的用户标识。</param>
    /// <param name="cancellationToken">用于取消数据库事务的令牌。</param>
    /// <returns>包含是否发生实际变更的结果；最后一名保护或操作人失效时返回稳定失败码。</returns>
    Task<Result<SuperAdministratorChangeResponse>> RevokeAsync(
        Guid operatorUserId,
        Guid targetUserId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// 描述一次超级管理员关系变更的幂等结果。
/// </summary>
/// <param name="TargetUserId">本次变更指向的用户标识。</param>
/// <param name="Changed">是否实际新增或删除了角色关系。</param>
public sealed record SuperAdministratorChangeResponse(
    Guid TargetUserId,
    bool Changed);
