using Full.NET.Abstractions.Results;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Document.Contracts;
using Full.NET.Modules.Document.Persistence;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Document.Features.ManageHostDocumentShares;

internal sealed class HostDocumentShareQueryService(
    IMultiResultQueryExecutor multiResultQueryExecutor,
    IQueryExecutor queryExecutor,
    IOptions<DatabaseOptions> databaseOptions)
{
    public async Task<Result<PagedResult<HostDocumentShareResponse>>> PageAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var offset = ((long)page - 1) * pageSize;

        var statement = databaseOptions.Value.Provider switch
        {
            DatabaseProvider.SqlServer => DocumentShareSql.PageSqlServer,
            DatabaseProvider.MySql => DocumentShareSql.PageMySql,
            _ => throw new InvalidOperationException("The configured database provider is not supported."),
        };

        var pageResult = await multiResultQueryExecutor.QueryMultipleAsync(
                statement,
                new { Offset = offset, PageSize = pageSize },
                async (reader, _) =>
                {
                    var total = await reader.ReadSingleOrDefaultAsync<long>().ConfigureAwait(false);
                    var rows = await reader.ReadAsync<DocumentShareRecord>().ConfigureAwait(false);
                    return (Total: total, Rows: rows);
                },
                cancellationToken)
            .ConfigureAwait(false);

        return Result<PagedResult<HostDocumentShareResponse>>.Success(
            new PagedResult<HostDocumentShareResponse>(
                pageResult.Rows.Select(Map).ToArray(),
                page,
                pageSize,
                pageResult.Total));
    }

    public async Task<Result<HostDocumentShareResponse>> GetByIdAsync(
        Guid shareId,
        CancellationToken cancellationToken = default)
    {
        var record = await queryExecutor
            .QuerySingleOrDefaultAsync<DocumentShareRecord>(
                DocumentShareSql.FindById,
                new { Id = shareId },
                cancellationToken)
            .ConfigureAwait(false);

        return record is null
            ? NotFound()
            : Result<HostDocumentShareResponse>.Success(Map(record));
    }

    public async Task<Result<HostDocumentShareResponse>> GetByCodeAsync(
        string shareCode,
        CancellationToken cancellationToken = default)
    {
        var record = await queryExecutor
            .QuerySingleOrDefaultAsync<DocumentShareRecord>(
                DocumentShareSql.FindByCode,
                new { ShareCode = shareCode },
                cancellationToken)
            .ConfigureAwait(false);

        return record is null
            ? CodeNotFound()
            : Result<HostDocumentShareResponse>.Success(Map(record));
    }

    internal static HostDocumentShareResponse Map(DocumentShareRecord record) =>
        new(
            record.Id,
            record.DocumentId,
            record.ShareCode,
            record.CreatedAtUtc,
            record.ExpireTime,
            record.Password,
            record.MaxAccessCount,
            record.AccessCount,
            record.IsEnabled,
            record.Version);

    private static Result<HostDocumentShareResponse> NotFound() =>
        Result<HostDocumentShareResponse>.Failure(NotFoundError());

    private static Result<HostDocumentShareResponse> CodeNotFound() =>
        Result<HostDocumentShareResponse>.Failure(CodeNotFoundError());

    private static Error NotFoundError() =>
        new(DocumentErrorCodes.ShareNotFound, "Document share was not found.", ErrorType.NotFound);

    private static Error CodeNotFoundError() =>
        new(DocumentErrorCodes.ShareCodeNotFound, "Share code was not found.", ErrorType.NotFound);
}
