using Full.NET.Abstractions.Results;

namespace Full.NET.Modules.Files.Contracts;

/// <summary>窄用例：将上传流写入 Files 状态机并返回不透明引用，供其他模块在事务内绑定。</summary>
public interface IHostFileUploadWriter
{
    /// <summary>将上传流写入 Files 状态机并返回不透明文件引用；大文件由实现方分块持久化。</summary>
    /// <param name="createdByUserId">发起上传的可信用户标识，用于审计与归属。</param>
    /// <param name="originalFileName">原始文件名，用于下载时 Content-Disposition。</param>
    /// <param name="contentType">文件 MIME 类型。</param>
    /// <param name="content">上传内容可读流；调用方负责释放，实现方不得缓存引用。</param>
    /// <param name="contentLength">声明内容长度（字节）；用于预校验与配额判断。</param>
    /// <param name="cancellationToken">用于取消上传操作的令牌。</param>
    /// <returns>成功时携带不透明文件引用与安全元数据。</returns>
    Task<Result<HostFileUploadResult>> UploadAsync(
        Guid createdByUserId,
        string originalFileName,
        string contentType,
        Stream content,
        long contentLength,
        CancellationToken cancellationToken = default);
}

/// <summary>上传成功后的不透明文件引用与 Document 绑定所需的安全元数据。</summary>
/// <remarks>字段顺序发布后不可调整；新增字段只能追加到末尾，以保持线格式兼容。</remarks>
/// <param name="FileId">不透明文件引用标识；其他模块据此在事务内绑定 Document。</param>
/// <param name="SizeBytes">实际持久化的字节数；用于配额统计。</param>
/// <param name="ContentHash">内容哈希；用于去重与完整性校验，可空表示实现方未计算。</param>
public sealed record HostFileUploadResult(
    Guid FileId,
    long SizeBytes,
    string? ContentHash);