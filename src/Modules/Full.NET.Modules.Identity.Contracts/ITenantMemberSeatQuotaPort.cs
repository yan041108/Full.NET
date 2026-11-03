using Full.NET.Abstractions.Results;

namespace Full.NET.Modules.Identity.Contracts;

/// <summary>席位配额端口的稳定失败码；缺少原指标与不存在业务操作必须区分。</summary>
public static class TenantMemberSeatQuotaErrorCodes
{
    /// <summary>指定业务操作没有任何预留或消费记录。</summary>
    public const string OperationNotFound = "identity.seat_quota.operation_not_found";
}

/// <summary>
/// 为 Identity 成员激活提供租户席位配额预留/确认/释放，由 Tenancy 模块实现。
/// </summary>
public interface ITenantMemberSeatQuotaPort
{
    /// <summary>成员未能激活或已终止时，按原消费身份幂等归还已确认席位。</summary>
    /// <param name="tenantId">可信租户标识。</param>
    /// <param name="operationId">原始席位操作标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>消费已撤销时返回成功；重复执行不重复归还。</returns>
    Task<Result<bool>> CancelConsumptionAsync(Guid tenantId, string operationId, CancellationToken cancellationToken = default);

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
    /// 成员仍为 Pending 时确认预留，将配额计入已用；确认成功后才允许本地激活。
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
