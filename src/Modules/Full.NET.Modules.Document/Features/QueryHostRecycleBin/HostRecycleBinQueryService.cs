using Full.NET.Abstractions.Results;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Document.Contracts;
using Full.NET.Modules.Document.Features;
using Full.NET.Modules.Document.Persistence;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Document.Features.QueryHostRecycleBin;

/// <summary>
/// Host 文档回收站只读查询服务。只列出 IsDeleted = 1 的软删除行，按 DeletedAtUtc 倒序分页，
/// 投影复用活动列表的明细投影以保证响应结构一致；本服务不复活或物理删除任何记录。
/// </summary>
internal sealed class HostRecycleBinQueryService(
    IMultiResultQueryExecutor multiResultQueryExecutor,
    IQueryExecutor queryExecutor,
    IOptions<DatabaseOptions> databaseOptions)
{
    public async Task<Result<PagedResult<HostDocumentItemResponse>>> ListAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var offset = ((long)page - 1) * pageSize;

        var statement = databaseOptions.Value.Provider switch
        {
            DatabaseProvider.SqlServer => DocumentItemSql.RecyclePageSqlServer,
            DatabaseProvider.MySql => DocumentItemSql.RecyclePageMySql,
            _ => throw new InvalidOperationException("The configured database provider is not supported."),
        };

        var pageResult = await multiResultQueryExecutor.QueryMultipleAsync(
                statement,
                DocumentSqlParameters.Create(("Offset", offset), ("PageSize", pageSize)),
                async (reader, _) =>
                {
                    var total = await reader.ReadSingleOrDefaultAsync<long>().ConfigureAwait(false);
                    var rows = await reader.ReadAsync<DocumentItemDetailRecord>().ConfigureAwait(false);
                    return (Total: total, Rows: rows);
                },
                cancellationToken)
            .ConfigureAwait(false);

        return Result<PagedResult<HostDocumentItemResponse>>.Success(
            new PagedResult<HostDocumentItemResponse>(
                pageResult.Rows.Select(Map).ToArray(),
                page,
                pageSize,
                pageResult.Total));
    }

    public async Task<Result<HostDocumentItemResponse>> GetDeletedByIdAsync(
        Guid itemId,
        CancellationToken cancellationToken = default)
    {
        var record = await queryExecutor
            .QuerySingleOrDefaultAsync<DocumentItemDetailRecord>(
                DocumentItemSql.FindDeletedById,
                DocumentSqlParameters.Create(("Id", itemId)),
                cancellationToken)
            .ConfigureAwait(false);

        return record is null
            ? NotFound()
            : Result<HostDocumentItemResponse>.Success(Map(record));
    }

    internal static HostDocumentItemResponse Map(DocumentItemDetailRecord record) =>
        HostDocumentItemResponseMapper.Map(record);

    private static Result<HostDocumentItemResponse> NotFound() =>
        Result<HostDocumentItemResponse>.Failure(
            new Error(
                DocumentErrorCodes.RecycleItemNotFound,
                "Recycle bin item was not found.",
                ErrorType.NotFound));
}
