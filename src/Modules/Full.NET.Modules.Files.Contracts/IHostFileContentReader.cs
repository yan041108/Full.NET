using Full.NET.Abstractions.Results;

namespace Full.NET.Modules.Files.Contracts;

/// <summary>窄用例：按文件引用 ID 打开已就绪内容流，不暴露存储键或物理路径。</summary>
public interface IHostFileContentReader
{
    /// <summary>打开已就绪文件的内容流；文件未就绪或不存在时返回失败结果。</summary>
    /// <param name="fileId">文件引用标识；必须来自已完成上传的文件。</param>
    /// <param name="cancellationToken">用于取消打开操作的令牌。</param>
    /// <returns>成功时携带可下载内容；调用方在响应结束后负责释放 Content 流。</returns>
    Task<Result<HostFileContent>> OpenReadyContentAsync(
        Guid fileId,
        CancellationToken cancellationToken = default);
}

/// <summary>已就绪文件的可下载内容；调用方在响应结束后负责释放 <see cref="Content"/>。</summary>
/// <remarks>字段顺序发布后不可调整；新增字段只能追加到末尾，以保持线格式兼容。</remarks>
/// <param name="Content">文件内容可读流；调用方负责释放，不得缓存或跨请求复用。</param>
/// <param name="ContentType">文件 MIME 类型，用于响应头 Content-Type。</param>
/// <param name="OriginalFileName">原始文件名，用于 Content-Disposition 下载提示。</param>
public sealed record HostFileContent(
    Stream Content,
    string ContentType,
    string OriginalFileName);