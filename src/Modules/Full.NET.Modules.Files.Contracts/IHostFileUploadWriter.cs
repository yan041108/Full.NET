using Full.NET.Abstractions.Results;

namespace Full.NET.Modules.Files.Contracts;

/// <summary>窄用例：将上传流写入 Files 状态机并返回不透明引用，供其他模块在事务内绑定。</summary>
public interface IHostFileUploadWriter
{
    Task<Result<HostFileUploadResult>> UploadAsync(
        Guid createdByUserId,
        string originalFileName,
        string contentType,
        Stream content,
        long contentLength,
        CancellationToken cancellationToken = default);
}

/// <summary>上传成功后的不透明文件引用与 Document 绑定所需的安全元数据。</summary>
public sealed record HostFileUploadResult(
    Guid FileId,
    long SizeBytes,
    string? ContentHash);