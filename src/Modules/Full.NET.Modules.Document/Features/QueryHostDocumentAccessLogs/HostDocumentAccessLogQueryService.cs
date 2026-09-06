using Full.NET.Abstractions.Results;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Document.Contracts;
using Full.NET.Modules.Document.Features;
using Full.NET.Modules.Document.Persistence;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Document.Features.QueryHostDocumentAccessLogs;

/// <summary>Host 文档访问日志分页只读查询。</summary>
internal sealed class HostDocumentAccessLogQueryService(
    IMultiResultQueryExecutor multiResultQueryExecutor,
    IOptions<DatabaseOptions> databaseOptions)
{
    public async Task<Result<PagedResult<HostDocumentAccessLogResponse>>> ListAsync(
        int page,
        int pageSize,
        Guid? documentItemId,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var offset = ((long)page - 1) * pageSize;
        var statement = databaseOptions.Value.Provider switch
        {
            DatabaseProvider.SqlServer => DocumentAccessLogSql.PageSqlServer,
            DatabaseProvider.MySql => DocumentAccessLogSql.PageMySql,
            _ => throw new InvalidOperationException("The configured database provider is not supported."),
        };

        var pageResult = await multiResultQueryExecutor
            .QueryMultipleAsync(
                statement,
                DocumentSqlParameters.Create(
                    ("DocumentItemId", documentItemId),
                    ("Offset", offset),
                    ("PageSize", pageSize)),
                async (reader, _) =>
                {
                    var total = await reader.ReadSingleOrDefaultAsync<long>().ConfigureAwait(false);
                    var rows = await reader.ReadAsync<DocumentAccessLogRecord>().ConfigureAwait(false);
                    return (Total: total, Rows: rows);
                },
                cancellationToken)
            .ConfigureAwait(false);

        var items = pageResult.Rows
            .Select(Map)
            .ToArray();
        return Result<PagedResult<HostDocumentAccessLogResponse>>.Success(
            new PagedResult<HostDocumentAccessLogResponse>(items, page, pageSize, pageResult.Total));
    }

    private static HostDocumentAccessLogResponse Map(DocumentAccessLogRecord record) =>
        new(
            record.Id,
            record.DocumentItemId,
            record.DocumentTitle,
            record.AccessTypeKey,
            record.SourceKey,
            record.ActorUserId,
            record.OccurredAtUtc,
            record.ClientIpFingerprint);
}
