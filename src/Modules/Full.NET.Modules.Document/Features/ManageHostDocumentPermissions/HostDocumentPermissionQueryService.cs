using Full.NET.Abstractions.Results;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Document.Contracts;
using Full.NET.Modules.Document.Persistence;

namespace Full.NET.Modules.Document.Features.ManageHostDocumentPermissions;

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
