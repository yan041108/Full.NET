using Full.NET.Abstractions.Results;

namespace Full.NET.Modules.Files.Contracts;

/// <summary>将上传流写入 Files 状态机并返回不透明引用；调用方在本地事务之外调用。</summary>
public interface IHostFileUploadWriter
{
    /// <summary>同一操作重试只接受相同调用者与内容；未知发布结果保留状态并拒绝盲目重传。</summary>
    /// <param name="reservedFileId">可选的服务端预留 UUID v7，先持久化消费方引用意图后再上传；不能来自不可信 HTTP 输入。</param>
    /// <param name="operationKey">调用方冻结的操作键，最长 128 字符；所有重试保持相同。</param>
    /// <param name="createdByUserId">可信上传用户，参与重试身份校验。</param>
    /// <param name="originalFileName">原始文件名，参与输入一致性校验。</param>
    /// <param name="contentType">文件 MIME 类型，参与输入一致性校验。</param>
    /// <param name="content">可读上传流，由调用方释放。</param>
    /// <param name="contentLength">声明字节数，必须与实际内容一致。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>就绪文件或冲突；冲突不允许调用方换键盲目重试未知结果。</returns>
    Task<Result<HostFileUploadResult>> UploadIdempotentAsync(string operationKey, Guid createdByUserId,
        string originalFileName, string contentType, Stream content, long contentLength,
        CancellationToken cancellationToken = default, Guid? reservedFileId = null);

    /// <summary>按操作键和可信所有者恢复上传收据；不存在返回空，未就绪返回冲突，禁止换键重传。</summary>
    /// <param name="reservedFileId">可选的服务端预留 UUID v7，先持久化消费方引用意图后再上传；不能来自不可信 HTTP 输入。</param>
    /// <param name="operationKey">预先持久化的上传操作键。</param>
    /// <param name="createdByUserId">操作绑定的可信用户。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task<Result<HostFileUploadResult?>> FindUploadAsync(string operationKey, Guid createdByUserId, CancellationToken cancellationToken = default);
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
