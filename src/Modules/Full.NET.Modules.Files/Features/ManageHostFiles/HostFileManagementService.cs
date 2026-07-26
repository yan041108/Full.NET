using System.Security.Cryptography;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Files.Contracts;
using Full.NET.Modules.Files.Persistence;
using Full.NET.Modules.Files.Storage;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Files.Features.ManageHostFiles;

/// <summary>Host 文件上传与软删除；物理写入在数据库事务外完成，失败时尽力回滚已落盘对象。</summary>
internal sealed class HostFileManagementService(
    ICommandExecutor commandExecutor,
    ICommandTransaction transaction,
    HostFileQueryService fileQueries,
    IHostFileBlobStorage blobStorage,
    IClock clock,
    IIdGenerator idGenerator,
    IOptions<LocalFileStorageOptions> storageOptions)
{
    public async Task<Result<HostFileResponse>> UploadAsync(
        Guid createdByUserId,
        string originalFileName,
        string contentType,
        Stream content,
        long contentLength,
        CancellationToken cancellationToken = default)
    {
        var normalizedName = NormalizeFileName(originalFileName);
        if (normalizedName.Length == 0)
        {
            return InvalidUpload("File name is required.");
        }

        var normalizedContentType = NormalizeContentType(contentType);
        if (normalizedContentType.Length == 0)
        {
            return InvalidUpload("Content type is required.");
        }

        if (contentLength <= 0)
        {
            return InvalidUpload("File content must not be empty.");
        }

        if (contentLength > storageOptions.Value.MaxUploadBytes)
        {
            return Result<HostFileResponse>.Failure(new Error(
                FilesErrorCodes.FileTooLarge,
                "The uploaded file exceeds the allowed size.",
                ErrorType.Validation));
        }

        var fileId = idGenerator.NewId();
        var now = clock.UtcNow;
        var storageKey = BuildStorageKey(fileId, now);

        var buffered = await BufferContentAsync(content, cancellationToken)
            .ConfigureAwait(false);
        await using (buffered)
        {
            var contentHash = await ComputeSha256HexAsync(buffered, cancellationToken)
                .ConfigureAwait(false);
            buffered.Position = 0;

            try
            {
                await blobStorage.SaveAsync(storageKey, buffered, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception)
            {
                return InvalidUpload("Failed to persist uploaded content.");
            }

            try
            {
                return await transaction.ExecuteAsync(
                        async token =>
                        {
                            await commandExecutor.ExecuteAsync(
                                    HostFileSql.Insert,
                                    new
                                    {
                                        Id = fileId,
                                        OriginalFileName = normalizedName,
                                        ContentType = normalizedContentType,
                                        SizeBytes = contentLength,
                                        StorageKey = storageKey,
                                        ContentHash = contentHash,
                                        CreatedAtUtc = now,
                                        CreatedByUserId = createdByUserId,
                                    },
                                    token)
                                .ConfigureAwait(false);
                            return await fileQueries.GetByIdAsync(fileId, token)
                                .ConfigureAwait(false);
                        },
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            catch
            {
                await TryDeleteBlobAsync(storageKey, cancellationToken).ConfigureAwait(false);
                throw;
            }
        }
    }

    public async Task<Result<HostFileResponse>> DeleteAsync(
        Guid fileId,
        CancellationToken cancellationToken = default)
    {
        var outcome = await transaction.ExecuteAsync(
                token => DeleteCoreAsync(fileId, token),
                cancellationToken)
            .ConfigureAwait(false);
        if (outcome.Result.IsSuccess && outcome.StorageKey is not null)
        {
            await TryDeleteBlobAsync(outcome.StorageKey, CancellationToken.None)
                .ConfigureAwait(false);
        }

        return outcome.Result;
    }

    private async Task<DeleteOutcome> DeleteCoreAsync(
        Guid fileId,
        CancellationToken cancellationToken)
    {
        var detailResult = await fileQueries.GetDetailByIdAsync(fileId, cancellationToken)
            .ConfigureAwait(false);
        if (!detailResult.IsSuccess)
        {
            return new DeleteOutcome(
                Result<HostFileResponse>.Failure(detailResult.Error!),
                null);
        }

        var detail = detailResult.Value!;
        var now = clock.UtcNow;
        var affected = await commandExecutor.ExecuteAsync(
                HostFileSql.SoftDelete,
                new { FileId = fileId, DeletedAtUtc = now },
                cancellationToken)
            .ConfigureAwait(false);
        if (affected == 0)
        {
            return new DeleteOutcome(
                Result<HostFileResponse>.Failure(new Error(
                    FilesErrorCodes.FileNotFound,
                    "The file was not found.",
                    ErrorType.NotFound)),
                null);
        }

        return new DeleteOutcome(
            Result<HostFileResponse>.Success(HostFileQueryService.Map(detail)),
            detail.StorageKey);
    }

    private static string NormalizeFileName(string? originalFileName)
    {
        var trimmed = originalFileName?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
        {
            return string.Empty;
        }

        var fileName = Path.GetFileName(trimmed);
        return fileName.Length is > 0 and <= 260 ? fileName : string.Empty;
    }

    private static string NormalizeContentType(string? contentType)
    {
        var trimmed = contentType?.Trim() ?? string.Empty;
        return trimmed.Length is > 0 and <= 128 ? trimmed : string.Empty;
    }

    private static string BuildStorageKey(Guid fileId, DateTimeOffset createdAtUtc) =>
        $"host/{createdAtUtc:yyyy/MM}/{fileId:N}";

    private static async Task<MemoryStream> BufferContentAsync(
        Stream content,
        CancellationToken cancellationToken)
    {
        var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
        buffer.Position = 0;
        return buffer;
    }

    private static async Task<string> ComputeSha256HexAsync(
        Stream content,
        CancellationToken cancellationToken)
    {
        var hash = await SHA256.HashDataAsync(content, cancellationToken).ConfigureAwait(false);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private async Task TryDeleteBlobAsync(
        string storageKey,
        CancellationToken cancellationToken)
    {
        try
        {
            await blobStorage.DeleteAsync(storageKey, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // 元数据已回滚或软删除时，孤立 blob 由后续清理任务处理。
        }
    }

    private static Result<HostFileResponse> InvalidUpload(string message) =>
        Result<HostFileResponse>.Failure(new Error(
            FilesErrorCodes.InvalidUpload,
            message,
            ErrorType.Validation));

    private sealed record DeleteOutcome(
        Result<HostFileResponse> Result,
        string? StorageKey);
}
