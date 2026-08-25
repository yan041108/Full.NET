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

/// <summary>Host 文件上传与软删除；上传通过持久化状态机隔离数据库提交不确定性与对象发布。</summary>
internal sealed class HostFileManagementService(
    ICommandExecutor commandExecutor,
    ICommandTransaction transaction,
    HostFileQueryService fileQueries,
    IHostFileReferenceClaimService hostFileReferenceClaimService,
    FileStorageProviderRegistry storageProviders,
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
        var storageProvider = storageProviders.DefaultProvider;

        var buffered = await BufferContentAsync(
                content,
                storageOptions.Value.MaxUploadBytes,
                cancellationToken)
            .ConfigureAwait(false);
        if (buffered is null)
        {
            return Result<HostFileResponse>.Failure(new Error(
                FilesErrorCodes.FileTooLarge,
                "The uploaded file exceeds the allowed size.",
                ErrorType.Validation));
        }

        await using (buffered)
        {
            if (buffered.Length == 0)
            {
                return InvalidUpload("File content must not be empty.");
            }

            // 元数据必须以已读取的真实字节数为准，声明长度只参与请求前快速拒绝。
            var actualContentLength = buffered.Length;
            var contentHash = await ComputeSha256HexAsync(buffered, cancellationToken)
                .ConfigureAwait(false);
            buffered.Position = 0;
            IReadOnlyDictionary<string, object?> insertParameters =
                new Dictionary<string, object?>
                {
                    ["Id"] = fileId,
                    ["OriginalFileName"] = normalizedName,
                    ["ContentType"] = normalizedContentType,
                    ["SizeBytes"] = actualContentLength,
                    ["ProviderKey"] = storageProvider.ProviderKey,
                    ["StorageKey"] = storageKey,
                    ["ContentHash"] = contentHash,
                    ["CreatedAtUtc"] = now,
                    ["CreatedByUserId"] = createdByUserId,
                };

            // 必须先提交不可见的 pending 元数据；若提交结果不确定，此时尚未发布 Blob。
            await transaction.ExecuteAsync(
                    async token =>
                    {
                        var affectedRows = await commandExecutor.ExecuteAsync(
                                HostFileSql.Insert,
                                insertParameters,
                                token)
                            .ConfigureAwait(false);
                        if (affectedRows != 1)
                        {
                            throw new InvalidOperationException(
                                "Files pending upload insert must affect exactly one row.");
                        }

                        return true;
                    },
                    cancellationToken)
                .ConfigureAwait(false);

            await transaction.ExecuteAsync(
                    async token =>
                    {
                        IReadOnlyDictionary<string, object?> claimParameters =
                            new Dictionary<string, object?>
                            {
                                ["FileId"] = fileId,
                                ["ProviderKey"] = storageProvider.ProviderKey,
                                ["StorageKey"] = storageKey,
                            };
                        var affectedRows = await commandExecutor.ExecuteAsync(
                                HostFileSql.ClaimPublication,
                                claimParameters,
                                token)
                            .ConfigureAwait(false);
                        if (affectedRows != 1)
                        {
                            throw new InvalidOperationException(
                                "Files upload publication claim must affect exactly one row.");
                        }

                        return true;
                    },
                    cancellationToken)
                .ConfigureAwait(false);

            try
            {
                await storageProvider.SaveAsync(storageKey, buffered, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // 请求取消属于控制流，不能降级成上传内容校验错误。
                throw;
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
                            IReadOnlyDictionary<string, object?> readyParameters =
                                new Dictionary<string, object?>
                                {
                                    ["FileId"] = fileId,
                                    ["ProviderKey"] = storageProvider.ProviderKey,
                                    ["StorageKey"] = storageKey,
                                };
                            var affectedRows = await commandExecutor.ExecuteAsync(
                                    HostFileSql.MarkReady,
                                    readyParameters,
                                    token)
                                .ConfigureAwait(false);
                            // 0 可能表示对账器已凭同一对象证据并发完成 ready；负数或多行仍必须 fail-closed。
                            if (affectedRows is < 0 or > 1)
                            {
                                throw new InvalidOperationException(
                                    "Files upload ready transition affected rows outside the expected boundary.");
                            }

                            if (affectedRows == 0)
                            {
                                var concurrentReadBack = await fileQueries
                                    .GetDetailByIdAsync(fileId, token)
                                    .ConfigureAwait(false);
                                // 0 行只能接受同一 Provider/StorageKey 已 ready 的精确读回，禁止把删除或串线误判为并发成功。
                                if (!concurrentReadBack.IsSuccess
                                    || !string.Equals(
                                        concurrentReadBack.Value!.ProviderKey,
                                        storageProvider.ProviderKey,
                                        StringComparison.Ordinal)
                                    || !string.Equals(
                                        concurrentReadBack.Value.StorageKey,
                                        storageKey,
                                        StringComparison.Ordinal))
                                {
                                    throw new InvalidOperationException(
                                        "Files concurrently completed upload could not be read back.");
                                }

                                return Result<HostFileResponse>.Success(
                                    HostFileQueryService.Map(concurrentReadBack.Value));
                            }

                            var readBack = await fileQueries.GetByIdAsync(fileId, token)
                                .ConfigureAwait(false);
                            if (!readBack.IsSuccess)
                            {
                                throw new InvalidOperationException(
                                    "Files ready upload could not be read back.");
                            }

                            return readBack;
                        },
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            catch
            {
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
        if (outcome.Result.IsSuccess
            && outcome.StorageProvider is not null
            && outcome.StorageKey is not null)
        {
            await TryDeleteBlobAsync(
                    outcome.StorageProvider,
                    outcome.StorageKey,
                    CancellationToken.None)
                .ConfigureAwait(false);
        }

        return outcome.Result;
    }

    private async Task<DeleteOutcome> DeleteCoreAsync(
        Guid fileId,
        CancellationToken cancellationToken)
    {
        if (!await fileQueries.TryAcquireHostFileRowLockAsync(fileId, cancellationToken)
            .ConfigureAwait(false))
        {
            return new DeleteOutcome(
                Result<HostFileResponse>.Failure(new Error(
                    FilesErrorCodes.FileNotFound,
                    "The file was not found.",
                    ErrorType.NotFound)),
                null,
                null);
        }

        if (await hostFileReferenceClaimService
                .HasOpenClaimsAsync(fileId, cancellationToken)
                .ConfigureAwait(false))
        {
            return ReferencedDeleteOutcome();
        }

        var detailResult = await fileQueries.GetDetailByIdAsync(fileId, cancellationToken)
            .ConfigureAwait(false);
        if (!detailResult.IsSuccess)
        {
            return new DeleteOutcome(
                Result<HostFileResponse>.Failure(detailResult.Error!),
                null,
                null);
        }

        var detail = detailResult.Value!;

        // Provider 必须在数据库写入前按持久化机器码解析；未知值不得回退到当前默认 Provider。
        var storageProvider = storageProviders.Resolve(detail.ProviderKey);
        var now = clock.UtcNow;
        IReadOnlyDictionary<string, object?> deleteParameters =
            new Dictionary<string, object?>
            {
                ["FileId"] = fileId,
                ["DeletedAtUtc"] = now,
                ["PendingState"] = HostFileReferenceClaimStates.Pending,
                ["ActiveState"] = HostFileReferenceClaimStates.Active,
            };
        var affected = await commandExecutor.ExecuteAsync(
                HostFileSql.SoftDelete,
                deleteParameters,
                cancellationToken)
            .ConfigureAwait(false);
        // 单文件删除最多只能影响一行；异常计数必须回滚，避免错误提交后继续删除 blob。
        if (affected is < 0 or > 1)
        {
            throw new InvalidOperationException(
                "Files delete affected rows outside the expected file boundary.");
        }

        if (affected == 0)
        {
            if (await hostFileReferenceClaimService
                    .HasOpenClaimsAsync(fileId, cancellationToken)
                    .ConfigureAwait(false))
            {
                return ReferencedDeleteOutcome();
            }

            return new DeleteOutcome(
                Result<HostFileResponse>.Failure(new Error(
                    FilesErrorCodes.FileNotFound,
                    "The file was not found.",
                    ErrorType.NotFound)),
                null,
                null);
        }

        return new DeleteOutcome(
            Result<HostFileResponse>.Success(HostFileQueryService.Map(detail)),
            storageProvider,
            detail.StorageKey);
    }

    private static DeleteOutcome ReferencedDeleteOutcome() =>
        new(
            Result<HostFileResponse>.Failure(new Error(
                FilesErrorCodes.FileReferenced,
                "The file is referenced by a pending or active claim.",
                ErrorType.Conflict)),
            null,
            null);

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
        FormattableString.Invariant(
            $"host/{createdAtUtc:yyyy}/{createdAtUtc:MM}/{fileId:N}");

    private static async Task<MemoryStream?> BufferContentAsync(
        Stream content,
        long maxBytes,
        CancellationToken cancellationToken)
    {
        var buffer = new MemoryStream();
        var copyBuffer = new byte[81920];
        try
        {
            while (true)
            {
                var read = await content.ReadAsync(copyBuffer, cancellationToken)
                    .ConfigureAwait(false);
                if (read == 0)
                {
                    buffer.Position = 0;
                    return buffer;
                }

                // 声明长度只用于快速拒绝；真实读取仍必须限流，防止低报长度造成无界缓冲。
                if (buffer.Length > maxBytes - read)
                {
                    await buffer.DisposeAsync().ConfigureAwait(false);
                    return null;
                }

                await buffer.WriteAsync(
                        copyBuffer.AsMemory(0, read),
                        cancellationToken)
                    .ConfigureAwait(false);
            }
        }
        catch
        {
            await buffer.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private static async Task<string> ComputeSha256HexAsync(
        Stream content,
        CancellationToken cancellationToken)
    {
        var hash = await SHA256.HashDataAsync(content, cancellationToken).ConfigureAwait(false);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private async Task TryDeleteBlobAsync(
        IFileStorageProvider storageProvider,
        string storageKey,
        CancellationToken cancellationToken)
    {
        try
        {
            await storageProvider.DeleteAsync(storageKey, cancellationToken)
                .ConfigureAwait(false);
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
        IFileStorageProvider? StorageProvider,
        string? StorageKey);
}
