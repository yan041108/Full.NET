using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Document.Configuration;
using Full.NET.Modules.Document.Contracts;
using Full.NET.Modules.Document.Domain;
using Full.NET.Modules.Document.Features;
using Full.NET.Modules.Document.Persistence;
using Full.NET.Modules.Files.Contracts;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Document.Features.ManageHostDocumentItems;

/// <summary>
/// 文档历史版本删除协调器：统一处理授权删除与保留策略裁剪，
/// 在事务内写入删除审计、删除版本行并释放 Files Claim，保证当前版本与最小保留数约束。
/// </summary>
internal sealed class DocumentVersionDeletionService(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    ICommandTransaction transaction,
    IHostFileReferenceClaimService hostFileReferenceClaimService,
    IOptionsMonitor<DocumentVersionRetentionOptions> retentionOptions,
    IClock clock,
    IIdGenerator idGenerator)
{
    /// <summary>管理员授权删除单个历史版本，并要求文档项乐观并发版本匹配。</summary>
    public Task<Result<HostDocumentItemResponse>> DeleteVersionManuallyAsync(
        Guid itemId,
        Guid versionId,
        Guid actorUserId,
        DeleteHostDocumentVersionRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.Version < 1)
        {
            return Task.FromResult(Invalid());
        }

        return transaction.ExecuteResultAsync(
            async token =>
            {
                var deleteResult = await DeleteVersionCoreAsync(
                        itemId,
                        versionId,
                        actorUserId,
                        HostDocumentVersionDeletionSourceKeys.Manual,
                        request.Version,
                        token)
                    .ConfigureAwait(false);
                if (!deleteResult.IsSuccess)
                {
                    return Result<HostDocumentItemResponse>.Failure(deleteResult.Error!);
                }

                return await ReloadActiveAsync(itemId, token).ConfigureAwait(false);
            },
            cancellationToken);
    }

    /// <summary>保留策略裁剪删除；无用户并发令牌，失败时返回 false 以便 Worker 继续处理其他版本。</summary>
    public async Task<bool> TryDeleteVersionForRetentionAsync(
        Guid itemId,
        Guid versionId,
        CancellationToken cancellationToken = default)
    {
        var result = await transaction.ExecuteResultAsync(
                token => DeleteVersionCoreAsync(
                    itemId,
                    versionId,
                    deletedByUserId: null,
                    HostDocumentVersionDeletionSourceKeys.Retention,
                    expectedItemVersion: null,
                    token),
                cancellationToken)
            .ConfigureAwait(false);
        return result.IsSuccess;
    }

    private async Task<Result<bool>> DeleteVersionCoreAsync(
        Guid itemId,
        Guid versionId,
        Guid? deletedByUserId,
        string deletedBySourceKey,
        long? expectedItemVersion,
        CancellationToken cancellationToken)
    {
        var item = await queryExecutor
            .QuerySingleOrDefaultAsync<DocumentItemDetailRecord>(
                DocumentItemSql.FindActiveById,
                DocumentSqlParameters.Create(("Id", itemId)),
                cancellationToken)
            .ConfigureAwait(false);
        if (item is null)
        {
            return NotFoundBool();
        }

        if (item.CurrentVersionId == versionId)
        {
            return VersionAlreadyCurrentBool();
        }

        var targetVersion = await queryExecutor
            .QuerySingleOrDefaultAsync<DocumentVersionRecord>(
                DocumentItemSql.FindVersionById,
                DocumentSqlParameters.Create(
                    ("VersionId", versionId),
                    ("DocumentItemId", itemId)),
                cancellationToken)
            .ConfigureAwait(false);
        if (targetVersion is null)
        {
            return VersionNotFoundBool();
        }

        var totalVersionCount = await queryExecutor
            .QuerySingleOrDefaultAsync<int>(
                DocumentVersionRetentionSql.CountVersionsByItemId,
                DocumentSqlParameters.Create(("DocumentItemId", itemId)),
                cancellationToken)
            .ConfigureAwait(false);
        var options = retentionOptions.CurrentValue;
        if (!DocumentVersionRetentionPolicy.CanDeleteVersion(
                totalVersionCount,
                options.MinimumRetainedVersionsPerItem))
        {
            return VersionMinimumRetainedBool();
        }

        if (expectedItemVersion is not null)
        {
            var now = clock.UtcNow;
            var touched = await commandExecutor.ExecuteAsync(
                    DocumentItemSql.TouchActiveItem,
                    DocumentSqlParameters.Create(
                        ("Id", itemId),
                        ("UpdatedAtUtc", now),
                        ("UpdatedByUserId", deletedByUserId ?? Guid.Empty),
                        ("Version", expectedItemVersion.Value)),
                    cancellationToken)
                .ConfigureAwait(false);
            if (touched != 1)
            {
                return VersionConflictBool();
            }
        }

        var deletedAtUtc = clock.UtcNow;
        await commandExecutor.ExecuteAsync(
                DocumentVersionRetentionSql.InsertDeletionAudit,
                DocumentSqlParameters.Create(
                    ("Id", idGenerator.NewId()),
                    ("DocumentItemId", itemId),
                    ("VersionId", targetVersion.Id),
                    ("VersionNumber", targetVersion.VersionNumber),
                    ("FileId", targetVersion.FileId),
                    ("ContentHash", targetVersion.ContentHash),
                    ("SizeBytes", targetVersion.SizeBytes),
                    ("UploadedByUserId", targetVersion.UploadedByUserId),
                    ("VersionCreatedAtUtc", targetVersion.CreatedAtUtc),
                    ("DeletedAtUtc", deletedAtUtc),
                    ("DeletedByUserId", deletedByUserId),
                    ("DeletedBySourceKey", deletedBySourceKey)),
                cancellationToken)
            .ConfigureAwait(false);

        var affected = await commandExecutor.ExecuteAsync(
                DocumentVersionRetentionSql.DeleteVersionById,
                DocumentSqlParameters.Create(
                    ("VersionId", versionId),
                    ("DocumentItemId", itemId)),
                cancellationToken)
            .ConfigureAwait(false);
        if (affected != 1)
        {
            return VersionNotFoundBool();
        }

        var idempotencyKey = HostFileReferenceClaimIdempotencyKeys.DocumentVersion(versionId);
        _ = await hostFileReferenceClaimService
            .ReleaseAsync(idempotencyKey, cancellationToken)
            .ConfigureAwait(false);

        return Result<bool>.Success(true);
    }

    private Task<DocumentItemDetailRecord?> FindActiveAsync(
        Guid itemId,
        CancellationToken cancellationToken) =>
        queryExecutor.QuerySingleOrDefaultAsync<DocumentItemDetailRecord>(
            DocumentItemSql.FindActiveById,
            DocumentSqlParameters.Create(("Id", itemId)),
            cancellationToken);

    private async Task<Result<HostDocumentItemResponse>> ReloadActiveAsync(
        Guid itemId,
        CancellationToken cancellationToken)
    {
        var record = await FindActiveAsync(itemId, cancellationToken).ConfigureAwait(false);
        return record is null
            ? NotFound()
            : Result<HostDocumentItemResponse>.Success(HostDocumentItemResponseMapper.Map(record));
    }

    private static Result<HostDocumentItemResponse> Invalid() =>
        Result<HostDocumentItemResponse>.Failure(InvalidError());

    private static Result<HostDocumentItemResponse> NotFound() =>
        Result<HostDocumentItemResponse>.Failure(NotFoundError());

    private static Result<bool> NotFoundBool() =>
        Result<bool>.Failure(NotFoundError());

    private static Result<bool> VersionConflictBool() =>
        Result<bool>.Failure(VersionConflictError());

    private static Result<bool> VersionAlreadyCurrentBool() =>
        Result<bool>.Failure(new Error(
            DocumentErrorCodes.VersionAlreadyCurrent,
            "The current document version cannot be deleted.",
            ErrorType.Conflict));

    private static Result<bool> VersionNotFoundBool() =>
        Result<bool>.Failure(new Error(
            DocumentErrorCodes.VersionNotFound,
            "The document version was not found.",
            ErrorType.NotFound));

    private static Result<bool> VersionMinimumRetainedBool() =>
        Result<bool>.Failure(new Error(
            DocumentErrorCodes.VersionMinimumRetained,
            "The document item must retain the minimum number of versions.",
            ErrorType.BusinessRule));

    private static Error InvalidError() =>
        new(DocumentErrorCodes.Invalid, "The document item request is invalid.", ErrorType.Validation);

    private static Error NotFoundError() =>
        new(DocumentErrorCodes.NotFound, "Document item was not found.", ErrorType.NotFound);

    private static Error VersionConflictError() =>
        new(DocumentErrorCodes.VersionConflict, "Document item was updated by another operation.", ErrorType.Conflict);
}
