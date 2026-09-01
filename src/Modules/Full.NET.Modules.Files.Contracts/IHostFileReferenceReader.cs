namespace Full.NET.Modules.Files.Contracts;

/// <summary>
/// 跨模块安全读取就绪文件元数据的只读端口；不暴露文件二进制流或上传能力。
/// </summary>
public interface IHostFileReferenceReader
{
    /// <summary>
    /// 按文件标识读取就绪文件引用；仅当文件已完成上传、未被软删除且至少有一个 claim 或内部保留时返回非 null。
    /// </summary>
    /// <param name="fileId">Files 模块中目标文件标识。</param>
    /// <param name="cancellationToken">用于取消读取的令牌。</param>
    /// <returns>就绪文件元数据；文件不存在、未就绪或被清理时返回 null。</returns>
    Task<HostFileReference?> GetReadyReferenceAsync(Guid fileId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Files 模块对外传递的文件元数据稳定子集；不包含物理存储路径或凭据。
/// </summary>
/// <param name="FileId">文件标识。</param>
/// <param name="SizeBytes">文件字节数。</param>
/// <param name="ContentHash">文件内容摘要，用于消费者校验一致性；可空表示 Files 尚未计算。</param>
public sealed record HostFileReference(Guid FileId, long SizeBytes, string? ContentHash);
