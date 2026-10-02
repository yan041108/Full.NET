using Full.NET.Abstractions.Results;

namespace Full.NET.Modules.Files.Contracts;

/// <summary>
/// 为 Files 租户资源上传提供存储字节配额预留/确认/释放，由 Tenancy 模块实现。
/// </summary>
public interface ITenantFileStorageQuotaPort
{
    /// <summary>
    /// 为指定操作预留存储字节配额；相同 operationId 重复调用返回首次结果。
    /// </summary>
    /// <param name="tenantId">目标租户标识。</param>
    /// <param name="operationId">幂等操作标识，建议使用上传任务 Id。</param>
    /// <param name="byteCount">需预留的字节数。</param>
    /// <param name="cancellationToken">请求取消令牌。</param>
    /// <returns>成功时 true 表示配额已预留；false 表示配额不足或租户不存在。Result 失败表示系统级错误。</returns>
    Task<Result<bool>> TryReserveAsync(
        Guid tenantId,
        string operationId,
        long byteCount,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 上传成功后确认预留配额，将预留转为已用。
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
    /// 上传失败时释放预留配额，不计入已用。
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
