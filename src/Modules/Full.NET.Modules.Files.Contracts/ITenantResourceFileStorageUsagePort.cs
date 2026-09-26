namespace Full.NET.Modules.Files.Contracts;

/// <summary>
/// Host 运维链路读取租户已发布资源文件存储字节总和，供 Tenancy 存储配额 UsedValue 基线对账。
/// </summary>
public interface ITenantResourceFileStorageUsagePort
{
    /// <summary>
    /// 由 Files 权威边界统计指定租户已发布资源文件的存储字节总和，不向消费者暴露物理表。
    /// </summary>
    Task<long> SumReadyStorageBytesAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default);
}
