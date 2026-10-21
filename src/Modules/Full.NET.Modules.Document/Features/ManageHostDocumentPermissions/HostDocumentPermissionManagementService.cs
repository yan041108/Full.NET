using System.Text.Json;
using Microsoft.Extensions.Options;
using Full.NET.Modules.Document.Serialization;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Document.Contracts;
using Full.NET.Modules.Document.Persistence;

namespace Full.NET.Modules.Document.Features.ManageHostDocumentPermissions;

/// <summary>
/// Host 文档细粒度权限管理服务。整文档权限重写采用"先全删再批量插"的原子语义，
/// 必须在单一事务内完成；权限记录无 Version 字段，禁止在内存层做 diff 后部分更新，
/// 否则会引入权限残留或权限真空窗口。每条记录的 Id 由外部生成 UUID v7。
/// </summary>
internal sealed class HostDocumentPermissionManagementService(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    ICommandTransaction transaction,
    HostDocumentPermissionQueryService queries,
    IClock clock,
    IIdGenerator idGenerator, IOptions<DatabaseOptions>? databaseOptions = null)
{
    public Task<Result<IReadOnlyList<HostDocumentPermissionResponse>>> SetPermissionsAsync(
        SetHostDocumentPermissionsRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.DocumentId == Guid.Empty || request.Permissions is null || request.Permissions.Count > 100)
        {
            return Task.FromResult(Invalid());
        }

        foreach (var perm in request.Permissions)
        {
            if (perm.UserId == Guid.Empty || perm.PermissionLevel is not ("read" or "write" or "delete" or "share" or "manage"))
            {
                return Task.FromResult(Invalid());
            }
        }

        return transaction.ExecuteResultAsync(
            token => SetPermissionsCoreAsync(request, token),
            cancellationToken);
    }

    private async Task<Result<IReadOnlyList<HostDocumentPermissionResponse>>> SetPermissionsCoreAsync(
        SetHostDocumentPermissionsRequest request,
        CancellationToken cancellationToken)
    {
        var document = await queryExecutor
            .QuerySingleOrDefaultAsync<DocumentItemRecord>(
                DocumentItemSql.FindActiveById,
                DocumentSqlParameters.Create(("Id", request.DocumentId)),
                cancellationToken)
            .ConfigureAwait(false);

        if (document is null)
        {
            return DocumentNotFound();
        }

        await commandExecutor.ExecuteAsync(
                DocumentPermissionSql.DeleteByDocument,
                DocumentSqlParameters.Create(("DocumentId", request.DocumentId)),
                cancellationToken)
            .ConfigureAwait(false);

        var now = clock.UtcNow;
        if (request.Permissions.Count > 0)
        {
            var rows = request.Permissions.Select(perm =>
                new DocumentPermissionInsertRow(idGenerator.NewId(), perm.UserId, perm.PermissionLevel.Trim())).ToArray();
            await commandExecutor.ExecuteAsync(
                databaseOptions?.Value.Provider == DatabaseProvider.MySql
                    ? DocumentPermissionSql.InsertBatchMySql : DocumentPermissionSql.InsertBatchSqlServer,
                DocumentSqlParameters.Create(("DocumentId", request.DocumentId), ("CreatedAtUtc", now),
                    ("Rows", JsonSerializer.Serialize(rows, DocumentJsonSerializerContext.Default.DocumentPermissionInsertRowArray))),
                cancellationToken).ConfigureAwait(false);
        }

        return await queries.ListByDocumentAsync(request.DocumentId, cancellationToken)
            .ConfigureAwait(false);
    }

    private static Result<IReadOnlyList<HostDocumentPermissionResponse>> Invalid() =>
        Result<IReadOnlyList<HostDocumentPermissionResponse>>.Failure(InvalidError());

    private static Result<IReadOnlyList<HostDocumentPermissionResponse>> DocumentNotFound() =>
        Result<IReadOnlyList<HostDocumentPermissionResponse>>.Failure(DocumentNotFoundError());

    private static Error InvalidError() =>
        new(DocumentErrorCodes.PermissionInvalid, "The permission request is invalid.", ErrorType.Validation);

    private static Error DocumentNotFoundError() =>
        new(DocumentErrorCodes.PermissionDocumentNotFound, "The document for permissions was not found.", ErrorType.NotFound);
}

/// <summary>有界 ACL 替换的数据库输入；标识由应用生成 UUID v7。</summary>
internal sealed record DocumentPermissionInsertRow(Guid Id, Guid UserId, string PermissionLevel);
