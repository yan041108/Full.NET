using Full.NET.Abstractions.Results;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.ImportExport.Contracts;
using Full.NET.Modules.ImportExport.Persistence;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.ImportExport.Features.ManageImportTasks;

/// <summary>导入任务只读查询。</summary>
internal sealed class ImportExportTaskQueryService(
    IQueryExecutor queryExecutor,
    IMultiResultQueryExecutor multiResultQueryExecutor,
    IOptions<DatabaseOptions> databaseOptions)
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
}
