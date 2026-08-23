using Full.NET.Abstractions.Results;
using Full.NET.Modules.Files.Contracts;
using Full.NET.Modules.Files.Features.ManageHostFiles;

namespace Full.NET.Modules.Files.Features.HostFileReferences;

/// <summary>把跨模块上传请求委托给 <see cref="ManageHostFiles.HostFileManagementService"/>，对外暴露 <see cref="IHostFileUploadWriter"/> 契约。</summary>
internal sealed class HostFileUploadWriter(HostFileManagementService fileManagementService)
    : IHostFileUploadWriter
{
    public async Task<Result<HostFileUploadResult>> UploadAsync(
        Guid createdByUserId,
        string originalFileName,
        string contentType,
        Stream content,
        long contentLength,
        CancellationToken cancellationToken = default)
    {
        var uploadResult = await fileManagementService.UploadAsync(
                createdByUserId,
                originalFileName,
                contentType,
                content,
                contentLength,
                cancellationToken)
            .ConfigureAwait(false);
        if (!uploadResult.IsSuccess)
        {
            return Result<HostFileUploadResult>.Failure(uploadResult.Error!);
        }

        var uploaded = uploadResult.Value!;
        return Result<HostFileUploadResult>.Success(
            new HostFileUploadResult(uploaded.Id, uploaded.SizeBytes, uploaded.ContentHash));
    }
}