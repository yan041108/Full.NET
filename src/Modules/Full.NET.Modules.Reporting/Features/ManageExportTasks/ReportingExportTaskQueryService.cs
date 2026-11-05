using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Reporting.Contracts;
using Full.NET.Modules.Reporting.Persistence;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Reporting.Features.ManageExportTasks;

/// <summary>报表导出任务只读查询。</summary>
internal sealed class ReportingExportTaskQueryService(
    IQueryExecutor queryExecutor,
    IMultiResultQueryExecutor multiResultQueryExecutor,
    IOptions<DatabaseOptions> databaseOptions,
    ICurrentTenant currentTenant)
{
    /// <summary>分页查询导出任务。</summary>
    public async Task<Result<PagedResult<ReportingExportTaskResponse>>> ListAsync(
        int page,
        int pageSize,
        Guid? definitionId,
        string? statusKey = null,
        CancellationToken cancellationToken = default)
    {
        var status = string.IsNullOrWhiteSpace(statusKey) ? null : statusKey.Trim();
        if (status is not null && !KnownStatuses.Contains(status, StringComparer.Ordinal))
            return Result<PagedResult<ReportingExportTaskResponse>>.Failure(InvalidQuery());
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
                    ("StatusKey", status),
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
                ReportingExportTaskSql.FindByIdFor(databaseOptions.Value.Provider),
                ReportingSqlParameters.Create(("Id", taskId)),
                cancellationToken)
            .ConfigureAwait(false);
        return record is null
            ? Result<ReportingExportTaskDetailResponse>.Failure(TaskNotFoundError())
            : Result<ReportingExportTaskDetailResponse>.Success(ReportingExportTaskMapper.MapDetail(record));
    }

    private static Error TaskNotFoundError() =>
        new(ReportingErrorCodes.ExportTaskNotFound, "The reporting export task was not found.", ErrorType.NotFound);

    private static readonly string[] KnownStatuses = [ReportingExportTaskStatusKeys.Queued, ReportingExportTaskStatusKeys.Processing,
        ReportingExportTaskStatusKeys.Succeeded, ReportingExportTaskStatusKeys.Failed, ReportingExportTaskStatusKeys.Canceled];

    /// <summary>按可信主体读取本人摘要；固定页上限和稳定游标，不先读取其他用户再在内存过滤。</summary>
    internal async Task<Result<CursorPage<ReportingExportTaskResponse>>> ListMineAsync(Guid requestedByUserId, int pageSize,
        string? statusKey, string? cursor, CancellationToken cancellationToken = default)
    {
        if (!currentTenant.IsAvailable || currentTenant.IsHost || currentTenant.Id is not { } tenantId || requestedByUserId == Guid.Empty)
            return Result<CursorPage<ReportingExportTaskResponse>>.Failure(new(CommonErrorCodes.PermissionDenied, "Tenant context and an authenticated user are required.", ErrorType.Forbidden));
        var status = string.IsNullOrWhiteSpace(statusKey) ? null : statusKey.Trim();
        if (status is not null && !KnownStatuses.Contains(status, StringComparer.Ordinal))
            return Result<CursorPage<ReportingExportTaskResponse>>.Failure(InvalidQuery());
        var context = $"reporting.mine.v1|{tenantId:N}|{requestedByUserId:N}|{status}";
        TimeIdQueryPosition? before = null;
        if (!string.IsNullOrEmpty(cursor))
        {
            if (!TimeIdQueryCursor.TryDecode(cursor, context, out var position))
                return Result<CursorPage<ReportingExportTaskResponse>>.Failure(InvalidQuery());
            before = position;
        }
        pageSize = Math.Clamp(pageSize, 1, 100);
        var rows = await queryExecutor.QueryAsync<ReportingExportTaskListRecord>(databaseOptions.Value.Provider switch
            {
                DatabaseProvider.MySql => before is null ? ReportingExportTaskSql.MineFirstMySql : ReportingExportTaskSql.MineAfterMySql,
                DatabaseProvider.SqlServer => before is null ? ReportingExportTaskSql.MineFirstSqlServer : ReportingExportTaskSql.MineAfterSqlServer,
                _ => throw new InvalidOperationException("Unsupported database provider."),
            },
            ReportingSqlParameters.Create(("RequestedByUserId", requestedByUserId), ("StatusKey", status), ("Take", pageSize + 1),
                ("BeforeCreatedAtUtc", before?.CreatedAtUtc.UtcDateTime), ("BeforeId", before?.Id)), cancellationToken).ConfigureAwait(false);
        var hasMore = rows.Count > pageSize;
        var items = rows.Take(pageSize).Select(ReportingExportTaskMapper.MapSummary).ToArray();
        var next = hasMore ? TimeIdQueryCursor.Encode(new(items[^1].CreatedAtUtc, items[^1].Id), context) : null;
        return Result<CursorPage<ReportingExportTaskResponse>>.Success(new(items, next, hasMore));
    }

    private static Error InvalidQuery() => new(CommonErrorCodes.RequestInvalid, "The task query or cursor is invalid.", ErrorType.Validation);
}
