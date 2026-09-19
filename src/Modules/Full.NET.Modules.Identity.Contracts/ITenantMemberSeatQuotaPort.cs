using Full.NET.Abstractions.Results;

namespace Full.NET.Modules.Identity.Contracts;

/// <summary>
/// 为 Identity 成员激活提供租户席位配额预留/确认/释放，由 Tenancy 模块实现。
/// </summary>
public interface ITenantMemberSeatQuotaPort
{
    /// <summary>
    /// 为即将激活的成员预留 1 个席位配额。
    /// </summary>
    /// <param name="tenantId">目标租户标识。</param>
    /// <param name="operationId">幂等操作标识，建议使用邀请 Id。</param>
    /// <param name="cancellationToken">请求取消令牌。</param>
    Task<Result<bool>> TryReserveAsync(
        Guid tenantId,
        string operationId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 成员激活成功后确认预留，将配额计入已用。
    /// </summary>
    Task<Result<bool>> ConfirmAsync(
        Guid tenantId,
        string operationId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 成员激活失败时释放预留配额。
    /// </summary>
    Task<Result<bool>> ReleaseAsync(
        Guid tenantId,
        string operationId,
        CancellationToken cancellationToken = default);
}