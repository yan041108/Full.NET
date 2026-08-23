using Full.NET.Abstractions.Results;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Document.Contracts;
using Full.NET.Modules.Document.Persistence;

namespace Full.NET.Modules.Document.Features.ManageHostDocumentPermissions;

/// <summary>
/// Host 文档细粒度权限只读查询服务。仅暴露按文档列出权限目录的列表能力，
/// 不提供单条查询以避免被未授权用户枚举；权限判定在 Endpoint 授权层完成，本服务只负责展示。
/// </summary>
internal sealed class HostDocumentPermissionQueryService(IQueryExecutor queryExecutor)
{
    public async Task<Result<IReadOnlyList<HostDocumentPermissionResponse>>> ListByDocumentAsync(
        Guid documentId,
        CancellationToken cancellationToken = default)
    {
        var rows = await queryExecutor
            .QueryAsync<DocumentPermissionRecord>(
                DocumentPermissionSql.ListByDocument,
                new { DocumentId = documentId },
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
