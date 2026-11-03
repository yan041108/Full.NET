using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.ImportExport.Contracts;
using Full.NET.Modules.ImportExport.Persistence;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.ImportExport.Features.ManageImportTasks;

/// <summary>导入任务只读查询。</summary>
internal sealed class ImportExportTaskQueryService(
    IQueryExecutor queryExecutor,
    IMultiResultQueryExecutor multiResultQueryExecutor,
    IOptions<DatabaseOptions> databaseOptions,
    ICurrentTenant currentTenant)
{
    public async Task<Result<PagedResult<ImportExportTaskResponse>>> ListAsync(
        int page,
        int pageSize,
        string? schemaKey,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var offset = ((long)page - 1) * pageSize;
        var statement = databaseOptions.Value.Provider switch
        {
            DatabaseProvider.SqlServer => ImportExportTaskSql.PageSqlServer,
            DatabaseProvider.MySql => ImportExportTaskSql.PageMySql,
            _ => throw new InvalidOperationException("The configured database provider is not supported."),
        };

        var pageResult = await multiResultQueryExecutor
            .QueryMultipleAsync(
                statement,
                ImportExportSqlParameters.Create(
                    ("SchemaKey", schemaKey),
                    ("Offset", offset),
                    ("PageSize", pageSize)),
                async (reader, _) =>
                {
                    var total = await reader.ReadSingleOrDefaultAsync<long>().ConfigureAwait(false);
                    var rows = await reader.ReadAsync<ImportExportTaskRecord>().ConfigureAwait(false);
                    return (Total: total, Rows: rows);
                },
                cancellationToken)
            .ConfigureAwait(false);

        var items = pageResult.Rows.Select(ImportExportTaskMapper.MapSummary).ToArray();
        return Result<PagedResult<ImportExportTaskResponse>>.Success(
            new PagedResult<ImportExportTaskResponse>(items, page, pageSize, pageResult.Total));
    }

    public async Task<Result<ImportExportTaskDetailResponse>> GetAsync(
        Guid taskId,
        CancellationToken cancellationToken = default)
    {
        var record = await queryExecutor
            .QuerySingleOrDefaultAsync<ImportExportTaskRecord>(
                ImportExportTaskSql.FindById,
                ImportExportSqlParameters.Create(("Id", taskId)),
                cancellationToken)
            .ConfigureAwait(false);
        return record is null
            ? Result<ImportExportTaskDetailResponse>.Failure(TaskNotFoundError())
            : Result<ImportExportTaskDetailResponse>.Success(ImportExportTaskMapper.MapDetail(record));
    }

    private static Error TaskNotFoundError() =>
        new(ImportExportErrorCodes.TaskNotFound, "The import task was not found.", ErrorType.NotFound);

    /// <summary>按可信主体读取本人摘要；固定页上限和稳定游标，不先读取其他用户再在内存过滤。</summary>
    internal async Task<Result<CursorPage<ImportExportTaskResponse>>> ListMineAsync(Guid requestedByUserId, int pageSize,
        string? statusKey, string? cursor, CancellationToken cancellationToken = default)
    {
        if (!currentTenant.IsAvailable || currentTenant.IsHost || currentTenant.Id is not { } tenantId || requestedByUserId == Guid.Empty)
            return Result<CursorPage<ImportExportTaskResponse>>.Failure(new(CommonErrorCodes.PermissionDenied, "Tenant context and an authenticated user are required.", ErrorType.Forbidden));
        var status = string.IsNullOrWhiteSpace(statusKey) ? null : statusKey.Trim();
        if (status is not null && !ImportExportTaskStatusKeys.All.Contains(status, StringComparer.Ordinal))
            return Result<CursorPage<ImportExportTaskResponse>>.Failure(InvalidQuery());
        var context = $"import_export.mine.v1|{tenantId:N}|{requestedByUserId:N}|{status}";
        TimeIdQueryPosition? before = null;
        if (!string.IsNullOrEmpty(cursor))
        {
            if (!TimeIdQueryCursor.TryDecode(cursor, context, out var position))
                return Result<CursorPage<ImportExportTaskResponse>>.Failure(InvalidQuery());
            before = position;
        }
        pageSize = Math.Clamp(pageSize, 1, 100);
        var rows = await queryExecutor.QueryAsync<ImportExportTaskListRecord>(databaseOptions.Value.Provider switch
            {
                DatabaseProvider.MySql => before is null ? ImportExportTaskSql.MineFirstMySql : ImportExportTaskSql.MineAfterMySql,
                DatabaseProvider.SqlServer => before is null ? ImportExportTaskSql.MineFirstSqlServer : ImportExportTaskSql.MineAfterSqlServer,
                _ => throw new InvalidOperationException("Unsupported database provider."),
            },
            ImportExportSqlParameters.Create(("RequestedByUserId", requestedByUserId), ("StatusKey", status), ("Take", pageSize + 1),
                ("BeforeCreatedAtUtc", before?.CreatedAtUtc.UtcDateTime), ("BeforeId", before?.Id)), cancellationToken).ConfigureAwait(false);
        var hasMore = rows.Count > pageSize;
        var items = rows.Take(pageSize).Select(ImportExportTaskMapper.MapSummary).ToArray();
        var next = hasMore ? TimeIdQueryCursor.Encode(new(items[^1].CreatedAtUtc, items[^1].Id), context) : null;
        return Result<CursorPage<ImportExportTaskResponse>>.Success(new(items, next, hasMore));
    }

    private static Error InvalidQuery() => new(CommonErrorCodes.RequestInvalid, "The task query or cursor is invalid.", ErrorType.Validation);
}
