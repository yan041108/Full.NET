using Full.NET.Abstractions.Results;

namespace Full.NET.Modules.Files.Contracts;

/// <summary>
/// 为 Files 租户资源上传提供存储字节配额预留/确认/释放，由 Tenancy 模块实现。
/// </summary>
public interface ITenantFileStorageQuotaPort
{
    Task<Result<bool>> TryReserveAsync(
        Guid tenantId,
        string operationId,
        long byteCount,
        CancellationToken cancellationToken = default);

    Task<Result<bool>> ConfirmAsync(
        Guid tenantId,
        string operationId,
        CancellationToken cancellationToken = default);

    Task<Result<bool>> ReleaseAsync(
        Guid tenantId,
        string operationId,
        CancellationToken cancellationToken = default);
}
