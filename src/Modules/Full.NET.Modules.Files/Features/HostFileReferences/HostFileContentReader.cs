using Full.NET.Abstractions.Results;
using Full.NET.Modules.Files.Contracts;
using Full.NET.Modules.Files.Features.ManageHostFiles;
using Full.NET.Modules.Files.Storage;

namespace Full.NET.Modules.Files.Features.HostFileReferences;

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