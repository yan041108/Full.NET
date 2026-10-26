namespace Full.NET.Modules.Files.Contracts;

/// <summary>只读取当前租户中精确模块与资源所有的就绪文件安全元数据，不暴露物理位置。</summary>
public interface ITenantResourceFileDescriptorReader
{
    /// <summary>调用方先授权资源；Files 再核对当前租户及不可由客户端覆盖的模块、资源身份。</summary>
    /// <param name="ownerModuleKey">受信所属模块键。</param>
    /// <param name="resourceId">已授权资源标识。</param>
    /// <param name="fileId">文件标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>就绪文件描述；越权、未就绪或不存在均返回空。</returns>
    Task<TenantResourceFileDescriptor?> GetReadyDescriptorAsync(string ownerModuleKey, Guid resourceId,
        Guid fileId, CancellationToken cancellationToken = default);
}

/// <summary>租户资源文件的安全描述，上传人用于消费模块再次核对办理主体。</summary>
/// <param name="FileId">文件标识。</param>
/// <param name="OriginalFileName">下载文件名。</param>
/// <param name="ContentType">声明内容类型。</param>
/// <param name="SizeBytes">实际持久化长度。</param>
/// <param name="ContentHash">内容摘要。</param>
/// <param name="CreatedByUserId">可信上传人。</param>
public sealed record TenantResourceFileDescriptor(Guid FileId, string OriginalFileName, string ContentType,
    long SizeBytes, string ContentHash, Guid CreatedByUserId);
