using Full.NET.Abstractions.Results;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Files.Persistence;
using Full.NET.Modules.Files.Contracts;
using Full.NET.Modules.Files.Features.ManageHostFiles;

namespace Full.NET.Modules.Files.Features.HostFileReferences;

/// <summary>把跨模块上传请求委托给 <see cref="ManageHostFiles.HostFileManagementService"/>，对外暴露 <see cref="IHostFileUploadWriter"/> 契约。</summary>
internal sealed class HostFileUploadWriter(HostFileManagementService fileManagementService, IQueryExecutor queries)
    : IHostFileUploadWriter
{
    public async Task<Result<HostFileUploadResult>> UploadIdempotentAsync(string operationKey, Guid createdByUserId,
        string originalFileName, string contentType, Stream content, long contentLength, CancellationToken cancellationToken = default, Guid? reservedFileId = null)
    {
        var result = await fileManagementService.UploadAsync(createdByUserId, originalFileName, contentType, content,
            contentLength, null, cancellationToken, operationKey, reservedFileId).ConfigureAwait(false);
        return result.IsSuccess ? Result<HostFileUploadResult>.Success(new HostFileUploadResult(
            result.Value!.Id, result.Value.SizeBytes, result.Value.ContentHash)) : Result<HostFileUploadResult>.Failure(result.Error!);
    }
    public async Task<Result<HostFileUploadResult?>> FindUploadAsync(string operationKey, Guid createdByUserId, CancellationToken cancellationToken = default)
    {
        var receipt = await queries.QuerySingleOrDefaultAsync<HostFileUploadOperationRecord>(HostFileUploadOperationSql.Find,
            new Dictionary<string, object?> { ["UploadOperationKey"] = operationKey }, cancellationToken).ConfigureAwait(false);
        if (receipt is null) return Result<HostFileUploadResult?>.Success(null);
        if (receipt.CreatedByUserId != createdByUserId || receipt.IsDeleted || receipt.StorageState != "ready")
            return Result<HostFileUploadResult?>.Failure(new Error("files.upload.operation_conflict", "Upload operation is unavailable or awaiting reconciliation.", ErrorType.Conflict));
        return Result<HostFileUploadResult?>.Success(new HostFileUploadResult(receipt.Id, receipt.SizeBytes, receipt.ContentHash));
    }

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
                folderId: null,
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
