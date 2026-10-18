using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Messaging;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Document.Contracts;
using Full.NET.Modules.Document.Persistence;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Document.Features.ManageHostDocumentPermissions;

/// <summary>
/// Host 文档细粒度权限只读查询服务。保留列表接口，编辑使用带文档修订号的一致性快照，
/// 不提供单条查询以避免被未授权用户枚举；权限判定在 Endpoint 授权层完成，本服务只负责展示。
/// </summary>
internal sealed class HostDocumentPermissionQueryService(
    IQueryExecutor queryExecutor,
    ICommandTransaction transaction,
    IOptions<DatabaseOptions> databaseOptions)
{
    public Task<Result<HostDocumentPermissionSnapshotResponse>> GetSnapshotAsync(
        Guid documentId,
        CancellationToken cancellationToken = default) =>
        transaction.ExecuteResultAsync(async token =>
        {
            var statement = databaseOptions.Value.Provider switch
            {
                DatabaseProvider.SqlServer => DocumentPermissionSql.LockDocumentSqlServer,
                DatabaseProvider.MySql => DocumentPermissionSql.LockDocumentMySql,
                _ => throw new InvalidOperationException("The configured database provider is not supported."),
            };
            var document = await queryExecutor.QuerySingleOrDefaultAsync<DocumentPermissionRevisionRecord>(
                statement, DocumentSqlParameters.Create(("Id", documentId)), token).ConfigureAwait(false);
            if (document is null)
            {
                return Result<HostDocumentPermissionSnapshotResponse>.Failure(new Error(
                    DocumentErrorCodes.PermissionDocumentNotFound,
                    "The document for permissions was not found.", ErrorType.NotFound));
            }

            var permissions = await ListByDocumentAsync(documentId, token).ConfigureAwait(false);
            return Result<HostDocumentPermissionSnapshotResponse>.Success(new(
                documentId, document.Version, permissions.Value!));
        }, cancellationToken);

    public async Task<Result<IReadOnlyList<HostDocumentPermissionResponse>>> ListByDocumentAsync(
        Guid documentId,
        CancellationToken cancellationToken = default)
    {
        var rows = await queryExecutor
            .QueryAsync<DocumentPermissionRecord>(
                DocumentPermissionSql.ListByDocument,
                DocumentSqlParameters.Create(("DocumentId", documentId)),
                cancellationToken)
            .ConfigureAwait(false);

        return Result<IReadOnlyList<HostDocumentPermissionResponse>>.Success(
            rows.Select(Map).ToArray());
    }

    internal static HostDocumentPermissionResponse Map(DocumentPermissionRecord record) =>
        new(
            record.Id,
            record.DocumentId,
            record.UserId,
            record.PermissionLevel,
            record.CreatedAtUtc);
}
