using System.Text.Json;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Document.Features;
using Full.NET.Modules.Document.Security;
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
    ICommandExecutor commandExecutor,
    ICommandTransaction transaction,
    HostDocumentPermissionQueryService queries,
    IClock clock,
    IIdGenerator idGenerator, DocumentMutationGuard guard, IHostUserBatchSelectionDirectory users, IOptions<DatabaseOptions> databaseOptions,
    IDocumentActorContext actor)
{
    public async Task<Result<IReadOnlyList<HostDocumentPermissionResponse>>> SetPermissionsAsync(
        SetHostDocumentPermissionsRequest request,
        CancellationToken cancellationToken = default)
    {
        if (actor.UserId is null || request.DocumentId == Guid.Empty || request.Permissions is null || request.Permissions.Count > 100 || request.Version < 1 || request.VisibilityMode is not ("host" or "restricted") || request.Permissions.Any(p => p is null) || request.Permissions.Select(p => p.UserId).Distinct().Count() != request.Permissions.Count)
        {
            return Invalid();
        }

        foreach (var perm in request.Permissions)
        {
            if (perm.UserId == Guid.Empty || DocumentPermissionActions.Parse(perm.Actions) == 0)
            {
                return Invalid();
            }
        }

        var found = await users.FindActiveHostUsersAsync(request.Permissions.Select(p => p.UserId).ToArray(), cancellationToken).ConfigureAwait(false);
        if (found.Count != request.Permissions.Count) return Invalid();
        return await transaction.ExecuteResultAsync(
            token => SetPermissionsCoreAsync(request, token),
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<Result<IReadOnlyList<HostDocumentPermissionResponse>>> SetPermissionsCoreAsync(
        SetHostDocumentPermissionsRequest request,
        CancellationToken cancellationToken)
    {
        var document = await guard.LockAsync(request.DocumentId, cancellationToken).ConfigureAwait(false);

        if (document is null)
        {
            return DocumentNotFound();
        }

        if (document.Version != request.Version) return Result<IReadOnlyList<HostDocumentPermissionResponse>>.Failure(
            new Error(DocumentErrorCodes.VersionConflict, "Document was changed.", ErrorType.Conflict));
        var previous = await queries.ListByDocumentAsync(request.DocumentId, cancellationToken).ConfigureAwait(false);
        await commandExecutor.ExecuteAsync(DocumentItemSql.SetVisibility,
            DocumentSqlParameters.Create(("Id", request.DocumentId), ("VisibilityMode", request.VisibilityMode), ("Version", request.Version)), cancellationToken).ConfigureAwait(false);
        await commandExecutor.ExecuteAsync(
                DocumentPermissionSql.DeleteByDocument,
                DocumentSqlParameters.Create(("DocumentId", request.DocumentId)),
                cancellationToken)
            .ConfigureAwait(false);

        var now = clock.UtcNow;
        if (request.Permissions.Count > 0)
        {
            var rows = request.Permissions.Select(perm =>
                new DocumentPermissionInsertRow(idGenerator.NewId(), perm.UserId, DocumentPermissionActions.Parse(perm.Actions))).ToArray();
            await commandExecutor.ExecuteAsync(
                databaseOptions.Value.Provider == DatabaseProvider.MySql
                    ? DocumentPermissionSql.InsertBatchMySql : DocumentPermissionSql.InsertBatchSqlServer,
                DocumentSqlParameters.Create(("DocumentId", request.DocumentId), ("CreatedAtUtc", now),
                    ("Rows", JsonSerializer.Serialize(rows, DocumentJsonSerializerContext.Default.DocumentPermissionInsertRowArray))),
                cancellationToken).ConfigureAwait(false);
        }

        var updated = await queries.ListByDocumentAsync(request.DocumentId, cancellationToken).ConfigureAwait(false);
        await commandExecutor.ExecuteAsync(DocumentPermissionAuditSql.Insert, DocumentSqlParameters.Create(
            ("Id", idGenerator.NewId()), ("DocumentId", request.DocumentId), ("ActorUserId", actor.UserId),
            ("PreviousMode", document.VisibilityMode), ("NewMode", request.VisibilityMode),
            ("BeforeActionsJson", JsonSerializer.Serialize(previous.Value!, DocumentJsonSerializerContext.Default.IReadOnlyListHostDocumentPermissionResponse)),
            ("AfterActionsJson", JsonSerializer.Serialize(updated.Value!, DocumentJsonSerializerContext.Default.IReadOnlyListHostDocumentPermissionResponse)),
            ("CreatedAtUtc", now)), cancellationToken).ConfigureAwait(false);
        return updated;
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
internal sealed record DocumentPermissionInsertRow(Guid Id, Guid UserId, int Actions);
