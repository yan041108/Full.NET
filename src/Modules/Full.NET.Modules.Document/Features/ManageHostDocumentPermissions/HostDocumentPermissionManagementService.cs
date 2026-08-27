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
    IIdGenerator idGenerator)
{
    public Task<Result<IReadOnlyList<HostDocumentPermissionResponse>>> SetPermissionsAsync(
        SetHostDocumentPermissionsRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.DocumentId == Guid.Empty || request.Permissions is null)
        {
            return Task.FromResult(Invalid());
        }

        foreach (var perm in request.Permissions)
        {
            if (perm.UserId == Guid.Empty || string.IsNullOrWhiteSpace(perm.PermissionLevel))
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
        foreach (var perm in request.Permissions)
        {
            var id = idGenerator.NewId();
            await commandExecutor.ExecuteAsync(
                    DocumentPermissionSql.Insert,
                    DocumentSqlParameters.Create(("Id", id), ("DocumentId", request.DocumentId), ("UserId", perm.UserId), ("PermissionLevel", perm.PermissionLevel.Trim()), ("CreatedAtUtc", now)),
                    cancellationToken)
                .ConfigureAwait(false);
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
