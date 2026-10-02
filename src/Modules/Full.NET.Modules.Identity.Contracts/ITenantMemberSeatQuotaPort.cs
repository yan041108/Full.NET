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
    /// <returns>成功时 true 表示席位已预留；false 表示席位不足或租户不存在。Result 失败表示系统级错误。</returns>
    Task<Result<bool>> TryReserveAsync(
        Guid tenantId,
        string operationId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 成员激活成功后确认预留，将配额计入已用。
    /// </summary>
    /// <param name="tenantId">目标租户标识。</param>
    /// <param name="operationId">幂等操作标识，须与预留时一致。</param>
    /// <param name="cancellationToken">请求取消令牌。</param>
    /// <returns>true 表示确认成功；false 表示预留不存在或已过期。Result 失败表示系统级错误。</returns>
    Task<Result<bool>> ConfirmAsync(
        Guid tenantId,
        string operationId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 成员激活失败时释放预留配额。
    /// </summary>
    /// <param name="tenantId">目标租户标识。</param>
    /// <param name="operationId">幂等操作标识，须与预留时一致。</param>
    /// <param name="cancellationToken">请求取消令牌。</param>
    /// <returns>true 表示释放成功；false 表示预留不存在或已确认。Result 失败表示系统级错误。</returns>
    Task<Result<bool>> ReleaseAsync(
        Guid tenantId,
        string operationId,
        CancellationToken cancellationToken = default);
}