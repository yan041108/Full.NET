using Full.NET.Abstractions.Results;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Document.Contracts;
using Full.NET.Modules.Document.Persistence;

namespace Full.NET.Modules.Document.Features.ManageHostDocumentItems;

/// <summary>文档项标签关联的读取与替换；所有写操作须在调用方事务内执行。</summary>
internal sealed class DocumentItemTagAssignmentService(IQueryExecutor queryExecutor, ICommandExecutor commandExecutor)
{
    public async Task<IReadOnlyDictionary<Guid, IReadOnlyList<HostDocumentTagAssignmentResponse>>> ListByDocumentItemIdsAsync(
        IReadOnlyList<Guid> documentItemIds,
        CancellationToken cancellationToken)
    {
        if (documentItemIds.Count == 0)
        {
            return new Dictionary<Guid, IReadOnlyList<HostDocumentTagAssignmentResponse>>();
        }

        var rows = await queryExecutor
            .QueryAsync<DocumentTagAssignmentRow>(
                DocumentTagAssignmentSql.ListByDocumentItemIds,
                DocumentSqlParameters.Create(("DocumentItemIds", documentItemIds)),
                cancellationToken)
            .ConfigureAwait(false);

        return rows
            .GroupBy(row => row.DocumentItemId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<HostDocumentTagAssignmentResponse>)group
                    .Select(row => new HostDocumentTagAssignmentResponse(row.TagId, row.TagName))
                    .ToArray());
    }

    public async Task<Result<bool>> ValidateTagIdsAsync(
        IReadOnlyList<Guid> tagIds,
        CancellationToken cancellationToken)
    {
        if (tagIds.Count == 0)
        {
            return Result<bool>.Success(true);
        }

        var distinct = tagIds.Distinct().ToArray();
        var count = await queryExecutor.QuerySingleOrDefaultAsync<int>(
                DocumentTagAssignmentSql.CountActiveTagsByIds,
                DocumentSqlParameters.Create(("TagIds", distinct)),
                cancellationToken)
            .ConfigureAwait(false);
        return count == distinct.Length
            ? Result<bool>.Success(true)
            : Result<bool>.Failure(InvalidTagError());
    }

    public async Task ReplaceAssignmentsAsync(
        Guid documentItemId,
        IReadOnlyList<Guid> tagIds,
        DateTimeOffset createdAtUtc,
        CancellationToken cancellationToken)
    {
        await commandExecutor.ExecuteAsync(
                DocumentTagAssignmentSql.DeleteByDocumentItemId,
                DocumentSqlParameters.Create(("DocumentItemId", documentItemId)),
                cancellationToken)
            .ConfigureAwait(false);

        foreach (var tagId in tagIds.Distinct())
        {
            await commandExecutor.ExecuteAsync(
                    DocumentTagAssignmentSql.Insert,
                    DocumentSqlParameters.Create(
                        ("DocumentItemId", documentItemId),
                        ("TagId", tagId),
                        ("CreatedAtUtc", createdAtUtc)),
                    cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private static Error InvalidTagError() =>
        new(DocumentErrorCodes.InvalidTag, "One or more document tags are invalid.", ErrorType.Validation);
}
