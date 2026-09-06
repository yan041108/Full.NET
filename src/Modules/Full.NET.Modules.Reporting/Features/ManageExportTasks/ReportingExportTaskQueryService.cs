using Full.NET.Abstractions.Results;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Reporting.Contracts;
using Full.NET.Modules.Reporting.Persistence;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Reporting.Features.ManageExportTasks;

/// <summary>报表导出任务只读查询。</summary>
internal sealed class ReportingExportTaskQueryService(
    IQueryExecutor queryExecutor,
    IMultiResultQueryExecutor multiResultQueryExecutor,
    IOptions<DatabaseOptions> databaseOptions)
{
    /// <summary>分页查询导出任务。</summary>
    public async Task<Result<PagedResult<ReportingExportTaskResponse>>> ListAsync(
        int page,
        int pageSize,
        Guid? definitionId,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var offset = ((long)page - 1) * pageSize;
        var statement = databaseOptions.Value.Provider switch
        {
            DatabaseProvider.SqlServer => ReportingExportTaskSql.PageSqlServer,
            DatabaseProvider.MySql => ReportingExportTaskSql.PageMySql,
            _ => throw new InvalidOperationException("The configured database provider is not supported."),
        };

        var pageResult = await multiResultQueryExecutor
            .QueryMultipleAsync(
                statement,
                ReportingSqlParameters.Create(
                    ("DefinitionId", definitionId),
                    ("Offset", offset),
                    ("PageSize", pageSize)),
                async (reader, _) =>
                {
                    var total = await reader.ReadSingleOrDefaultAsync<long>().ConfigureAwait(false);
                    var rows = await reader.ReadAsync<ReportingExportTaskRecord>().ConfigureAwait(false);
                    return (Total: total, Rows: rows);
                },
                cancellationToken)
            .ConfigureAwait(false);

        var items = pageResult.Rows.Select(ReportingExportTaskMapper.MapSummary).ToArray();
        return Result<PagedResult<ReportingExportTaskResponse>>.Success(
            new PagedResult<ReportingExportTaskResponse>(items, page, pageSize, pageResult.Total));
    }

    /// <summary>按标识读取导出任务详情。</summary>
    public async Task<Result<ReportingExportTaskDetailResponse>> GetAsync(
        Guid taskId,
        CancellationToken cancellationToken = default)
    {
        var record = await queryExecutor
            .QuerySingleOrDefaultAsync<ReportingExportTaskRecord>(
                ReportingExportTaskSql.FindById,
                ReportingSqlParameters.Create(("Id", taskId)),
                cancellationToken)
            .ConfigureAwait(false);
        return record is null
            ? Result<ReportingExportTaskDetailResponse>.Failure(TaskNotFoundError())
            : Result<ReportingExportTaskDetailResponse>.Success(ReportingExportTaskMapper.MapDetail(record));
    }

    private static Error TaskNotFoundError() =>
        new(ReportingErrorCodes.ExportTaskNotFound, "The reporting export task was not found.", ErrorType.NotFound);
}
