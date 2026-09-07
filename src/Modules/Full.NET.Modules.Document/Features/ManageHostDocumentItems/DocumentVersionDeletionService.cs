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
/// 在本地事务内写入删除审计并删除版本行；Files Claim 在事务提交后释放，
/// 与新增版本“先 Claim、事务外 Confirm/Release”的边界一致。
/// </summary>
/// <param name="queryExecutor">文档模块查询执行器。</param>
/// <param name="commandExecutor">文档模块写入执行器。</param>
/// <param name="transaction">版本删除短事务，不得包含 Files 合同调用。</param>
/// <param name="hostFileReferenceClaimService">Files 引用 Claim 端口；仅在事务提交后释放。</param>
/// <param name="retentionOptions">版本保留策略选项。</param>
/// <param name="clock">删除审计时钟。</param>
/// <param name="idGenerator">删除审计标识生成器。</param>
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
    /// <param name="itemId">文档项标识。</param>
    /// <param name="versionId">待删除版本标识。</param>
    /// <param name="actorUserId">操作者用户标识。</param>
    /// <param name="request">含文档项乐观版本的删除请求。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>删除后的文档项或稳定业务错误。</returns>
    public async Task<Result<HostDocumentItemResponse>> DeleteVersionManuallyAsync(
        Guid itemId,
        Guid versionId,
        Guid actorUserId,
        DeleteHostDocumentVersionRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.Version < 1)
        {
            return Invalid();
        }

        var deleteResult = await transaction.ExecuteResultAsync(
                token => DeleteVersionCoreAsync(
                    itemId,
                    versionId,
                    actorUserId,
                    HostDocumentVersionDeletionSourceKeys.Manual,
                    request.Version,
                    token),
                cancellationToken)
            .ConfigureAwait(false);
        if (!deleteResult.IsSuccess)
        {
            return Result<HostDocumentItemResponse>.Failure(deleteResult.Error!);
        }

        await ReleaseDocumentVersionClaimAsync(versionId, cancellationToken).ConfigureAwait(false);
        return await ReloadActiveAsync(itemId, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>保留策略裁剪删除；无用户并发令牌，失败时返回 false 以便 Worker 继续处理其他版本。</summary>
    /// <param name="itemId">文档项标识。</param>
    /// <param name="versionId">待删除版本标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>本地删除是否成功；Claim 释放失败不回滚已提交的版本删除。</returns>
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
        if (!result.IsSuccess)
        {
            return false;
        }

        await ReleaseDocumentVersionClaimAsync(versionId, cancellationToken).ConfigureAwait(false);
        return true;
    }

    /// <summary>在本地事务中校验约束、写入删除审计并删除版本行。</summary>
    /// <param name="itemId">文档项标识。</param>
    /// <param name="versionId">待删除版本标识。</param>
    /// <param name="deletedByUserId">操作者；保留策略删除时为空。</param>
    /// <param name="deletedBySourceKey">删除来源键。</param>
    /// <param name="expectedItemVersion">手动删除时的文档项乐观版本。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>删除是否成功。</returns>
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

        return Result<bool>.Success(true);
    }

    /// <summary>本地版本删除提交后再释放 Files Claim，避免跨模块共享本地事务。</summary>
    /// <param name="versionId">已删除的文档版本标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    private Task ReleaseDocumentVersionClaimAsync(
        Guid versionId,
        CancellationToken cancellationToken) =>
        hostFileReferenceClaimService.ReleaseAsync(
            HostFileReferenceClaimIdempotencyKeys.DocumentVersion(versionId),
            cancellationToken);

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
