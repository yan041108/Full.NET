using Full.NET.Abstractions.Results;

namespace Full.NET.Modules.Files.Contracts;

/// <summary>窄用例：按文件引用 ID 打开已就绪内容流，不暴露存储键或物理路径。</summary>
public interface IHostFileContentReader
{
    Task<Result<HostFileContent>> OpenReadyContentAsync(
        Guid fileId,
        CancellationToken cancellationToken = default);
}

/// <summary>已就绪文件的可下载内容；调用方在响应结束后负责释放 <see cref="Content"/>。</summary>
public sealed record HostFileContent(
    Stream Content,
    string ContentType,
    string OriginalFileName);