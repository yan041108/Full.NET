namespace Full.NET.Modules.Files.Contracts;

/// <summary>
/// Host 运维链路读取租户已发布资源文件存储字节总和，供 Tenancy 存储配额 UsedValue 基线对账。
/// </summary>
public interface ITenantResourceFileStorageUsagePort
{
    /// <summary>
    /// 统计指定租户 <c>fn_files_tenant_resource_file</c> 中 StatusKey 为 ready 的 SizeBytes 之和。
    /// </summary>
    /// <param name="tenantId">目标租户标识。</param>
    /// <param name="cancellationToken">用于取消数据库查询的令牌。</param>
    /// <returns>异步结果；返回已就绪资源文件的字节总和，无记录时返回 0。</returns>
    Task<long> SumReadyStorageBytesAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default);
}
