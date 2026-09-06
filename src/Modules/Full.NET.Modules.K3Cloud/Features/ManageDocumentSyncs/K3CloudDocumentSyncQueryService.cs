using Full.NET.Abstractions.Results;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.K3Cloud.Contracts;
using Full.NET.Modules.K3Cloud.Persistence;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.K3Cloud.Features.ManageDocumentSyncs;

/// <summary>K3Cloud 单据同步只读查询。</summary>
internal sealed class K3CloudDocumentSyncQueryService(
    IQueryExecutor queryExecutor,
    IOptions<DatabaseOptions> databaseOptions)
{
    public async Task<Result<K3CloudDocumentSyncResponse>> GetByIdAsync(
        Guid syncId,
        CancellationToken cancellationToken = default)
    {
        var row = await queryExecutor.QuerySingleOrDefaultAsync<K3CloudDocumentSyncRecord>(
                K3CloudDocumentSyncSql.FindById,
                K3CloudSqlParameters.Create([("SyncId", syncId)]),
                cancellationToken)
            .ConfigureAwait(false);
        return row is null
            ? NotFound()
            : Result<K3CloudDocumentSyncResponse>.Success(K3CloudDocumentSyncMapper.Map(row));
    }

    public async Task<Result<PagedResult<K3CloudDocumentSyncResponse>>> ListAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var offset = (page - 1) * pageSize;
        var total = await queryExecutor.QuerySingleOrDefaultAsync<long>(
                K3CloudDocumentSyncSql.Count,
                K3CloudSqlParameters.Create(),
                cancellationToken)
            .ConfigureAwait(false);
        var statement = databaseOptions.Value.Provider == DatabaseProvider.MySql
            ? K3CloudDocumentSyncSql.ListMySql
            : K3CloudDocumentSyncSql.ListSqlServer;
        var rows = await queryExecutor.QueryAsync<K3CloudDocumentSyncRecord>(
                statement,
                K3CloudSqlParameters.Create([
                    ("Offset", offset),
                    ("PageSize", pageSize)]),
                cancellationToken)
            .ConfigureAwait(false);
        return Result<PagedResult<K3CloudDocumentSyncResponse>>.Success(
            new PagedResult<K3CloudDocumentSyncResponse>(
                rows.Select(K3CloudDocumentSyncMapper.Map).ToArray(),
                page,
                pageSize,
                total));
    }

    internal async Task<K3CloudDocumentSyncRecord?> FindRecordAsync(
        Guid syncId,
        CancellationToken cancellationToken) =>
        await queryExecutor.QuerySingleOrDefaultAsync<K3CloudDocumentSyncRecord>(
                K3CloudDocumentSyncSql.FindById,
                K3CloudSqlParameters.Create([("SyncId", syncId)]),
                cancellationToken)
            .ConfigureAwait(false);

    private static Result<K3CloudDocumentSyncResponse> NotFound() =>
        Result<K3CloudDocumentSyncResponse>.Failure(new Error(
            K3CloudErrorCodes.DocumentSyncNotFound,
            "The K3Cloud document sync record was not found.",
            ErrorType.NotFound));
}
