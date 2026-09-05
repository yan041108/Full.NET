namespace Full.NET.Modules.Files.Contracts;

/// <summary>跨模块读取已就绪 Host 文件描述信息，用于附件约束校验与展示元数据。</summary>
public interface IHostFileDescriptorReader
{
    /// <summary>
    /// 按文件标识读取已上传且未删除的 Host 文件描述；文件不存在或未就绪时返回 <see langword="null"/>。
    /// </summary>
    /// <param name="fileId">Files 模块中目标文件标识。</param>
    /// <param name="cancellationToken">用于取消读取的令牌。</param>
    /// <returns>就绪文件描述；不可用时返回 <see langword="null"/>。</returns>
    Task<HostFileDescriptor?> GetReadyDescriptorAsync(
        Guid fileId,
        CancellationToken cancellationToken = default);
}

/// <summary>跨模块安全传递的 Host 文件描述子集；不包含物理存储路径或凭据。</summary>
/// <param name="FileId">文件标识。</param>
/// <param name="OriginalFileName">原始文件名。</param>
/// <param name="ContentType">内容类型。</param>
/// <param name="SizeBytes">文件字节数。</param>
/// <param name="ContentHash">内容摘要；可空表示 Files 尚未计算。</param>
/// <param name="CreatedByUserId">上传人用户标识。</param>
public sealed record HostFileDescriptor(
    Guid FileId,
    string OriginalFileName,
    string ContentType,
    long SizeBytes,
    string? ContentHash,
    Guid CreatedByUserId);
