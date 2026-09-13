using Full.NET.Abstractions.Results;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Files.Contracts;
using Full.NET.Modules.Files.Persistence;
using Full.NET.Modules.Files.Storage;

namespace Full.NET.Modules.Files.Features.HostFileReferences;

/// <summary>按 fileId 解析 Host 文件元数据后从对应 <see cref="IFileStorageProvider"/> 打开只读内容流。</summary>
/// <remarks>
/// 仅允许打开 <c>ready</c> 且未软删除的文件；调用方负责释放返回的 <see cref="Stream"/>。
/// 元数据查询直接走 <see cref="IQueryExecutor"/>，避免与 <see cref="ManageHostFiles.HostFileQueryService"/> 形成 DI 环。
/// </remarks>
internal sealed class HostFileContentReader(
    IQueryExecutor queryExecutor,
    FileStorageProviderRegistry storageProviders) : IHostFileContentReader
{
    public async Task<Result<HostFileContent>> OpenReadyContentAsync(
        Guid fileId,
        CancellationToken cancellationToken = default)
    {
        var record = await queryExecutor.QuerySingleOrDefaultAsync<HostFileDetailRecord>(
                HostFileSql.FindActiveById,
                new Dictionary<string, object?> { ["FileId"] = fileId },
                cancellationToken)
            .ConfigureAwait(false);
        if (record is null)
        {
            return Result<HostFileContent>.Failure(new Error(
                FilesErrorCodes.FileNotFound,
                "The file was not found.",
                ErrorType.NotFound));
        }

        var storageProvider = storageProviders.Resolve(record.ProviderKey);
        var stream = await storageProvider.OpenReadAsync(record.StorageKey, cancellationToken)
            .ConfigureAwait(false);
        return Result<HostFileContent>.Success(
            new HostFileContent(stream, record.ContentType, record.OriginalFileName));
    }
}