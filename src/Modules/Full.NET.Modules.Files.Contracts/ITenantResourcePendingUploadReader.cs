namespace Full.NET.Modules.Files.Contracts;

/// <summary>按可信租户和资源所有权查询待确认上传意图，供业务任务恢复判断是否可以重新生成。</summary>
public interface ITenantResourcePendingUploadReader
{
    /// <summary>探测所属资源是否还有 Files 尚未确认的上传意图。</summary>
    /// <param name="ownerModuleKey">受信所属模块键。</param>
    /// <param name="resourceId">调用模块已授权的资源标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>存在 pending 意图时返回 true。</returns>
    /// <exception cref="TenantResourceFileUnavailableException">租户停用或可信上下文改变，调用方须保留原状态。</exception>
    /// <exception cref="InvalidOperationException">调用方持有业务事务时禁止跨模块复核。</exception>
    Task<bool> HasPendingUploadAsync(string ownerModuleKey, Guid resourceId,
        CancellationToken cancellationToken = default);
}
