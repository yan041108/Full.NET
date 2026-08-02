using Full.NET.Abstractions.Results;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Document.Contracts;
using Full.NET.Modules.Document.Persistence;

namespace Full.NET.Modules.Document.Features.ManageHostDocumentTags;

internal sealed class HostDocumentTagQueryService(IQueryExecutor queryExecutor)
{
    public async Task<Result<IReadOnlyList<HostDocumentTagResponse>>> ListAsync(
        CancellationToken cancellationToken = default)
    {
        var rows = await queryExecutor
            .QueryAsync<DocumentTagRecord>(DocumentTagSql.ListActive, null, cancellationToken)
            .ConfigureAwait(false);
        return Result<IReadOnlyList<HostDocumentTagResponse>>.Success(
            rows.Select(Map).ToArray());
    }

    public async Task<Result<HostDocumentTagResponse>> GetByIdAsync(
        Guid tagId,
        CancellationToken cancellationToken = default)
    {
        var record = await queryExecutor
            .QuerySingleOrDefaultAsync<DocumentTagRecord>(
                DocumentTagSql.FindActiveById,
                new { Id = tagId },
                cancellationToken)
            .ConfigureAwait(false);
        return record is null ? NotFound() : Result<HostDocumentTagResponse>.Success(Map(record));
    }

    internal static HostDocumentTagResponse Map(DocumentTagRecord record) =>
        new(
            record.Id,
            record.Name,
            record.CreatedAtUtc,
            record.UpdatedAtUtc,
            record.Version);

    private static Result<HostDocumentTagResponse> NotFound() =>
        Result<HostDocumentTagResponse>.Failure(
            new Error(
                DocumentErrorCodes.TagNotFound,
                "Document tag was not found.",
                ErrorType.NotFound));
}
