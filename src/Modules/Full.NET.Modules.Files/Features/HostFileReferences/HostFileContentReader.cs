using Full.NET.Abstractions.Results;
using Full.NET.Modules.Files.Contracts;
using Full.NET.Modules.Files.Features.ManageHostFiles;
using Full.NET.Modules.Files.Storage;

namespace Full.NET.Modules.Files.Features.HostFileReferences;

/// <summary>按 fileId 解析 Host 文件元数据后从对应 <see cref="IFileStorageProvider"/> 打开只读内容流。</summary>
/// <remarks>仅允许打开 <c>ready</c> 且未软删除的文件；调用方负责释放返回的 <see cref="Stream"/>。</remarks>
internal sealed class HostFileContentReader(
    HostFileQueryService fileQueries,
    FileStorageProviderRegistry storageProviders) : IHostFileContentReader
{
    public async Task<Result<HostFileContent>> OpenReadyContentAsync(
        Guid fileId,
        CancellationToken cancellationToken = default)
    {
        var detailResult = await fileQueries.GetDetailByIdAsync(fileId, cancellationToken)
            .ConfigureAwait(false);
        if (!detailResult.IsSuccess)
        {
            return Result<HostFileContent>.Failure(detailResult.Error!);
        }

        var detail = detailResult.Value!;
        var storageProvider = storageProviders.Resolve(detail.ProviderKey);
        var stream = await storageProvider.OpenReadAsync(detail.StorageKey, cancellationToken)
            .ConfigureAwait(false);
        return Result<HostFileContent>.Success(
            new HostFileContent(stream, detail.ContentType, detail.OriginalFileName));
    }
}