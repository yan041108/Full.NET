using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Jobs.Contracts;
using Full.NET.Modules.Jobs.Persistence;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Jobs.Features.ManageHostJobExecutions;

/// <summary>
/// Host 任务执行记录分页查询、按 Id 查找与终态记录清空服务。
/// 列表响应聚合执行耗时（FinishedAtUtc - StartedAtUtc）、状态（Pending/Running/Succeeded/Failed）、
/// 错误消息（ErrorMessage 截断避免前端溢出，ErrorMessage > 2000 字符时由映射层省略尾部）；
/// 清空操作仅删除指定定义下的 succeeded/failed 终态记录，保留 pending/running 以避免丢失运行证据。
/// </summary>
internal sealed class HostJobExecutionQueryService(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    ICommandTransaction transaction,
    IOptions<DatabaseOptions> databaseOptions)
{
    public async Task<Result<HostJobExecutionResponse>> GetByIdAsync(
        Guid executionId,
        CancellationToken cancellationToken = default)
    {
        var record = await queryExecutor.QuerySingleOrDefaultAsync<JobExecutionRecord>(
                JobSql.FindExecutionById,
                new { Id = executionId },
                cancellationToken)
            .ConfigureAwait(false);
        return record is null
            ? Result<HostJobExecutionResponse>.Failure(new Error(
                JobsErrorCodes.ExecutionNotFound,
                "The job execution was not found.",
                ErrorType.NotFound))
            : Result<HostJobExecutionResponse>.Success(MapExecution(record));
    }

    public async Task<Result<PagedResult<HostJobExecutionResponse>>> ListAsync(
        int page,
        int pageSize,
        Guid? jobDefinitionId,
        Guid? jobScheduleId,
        string? status,
        DateTimeOffset? fromUtc,
        DateTimeOffset? toUtc,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var offset = (page - 1) * pageSize;
        var filter = new
        {
            JobDefinitionId = jobDefinitionId,
            JobScheduleId = jobScheduleId,
            Status = string.IsNullOrWhiteSpace(status) ? null : status.Trim(),
            FromUtc = fromUtc,
            ToUtc = toUtc,
        };
        var total = await queryExecutor.QuerySingleOrDefaultAsync<long>(
                JobSql.CountExecutions,
                filter,
                cancellationToken)
            .ConfigureAwait(false);
        var rows = await queryExecutor.QueryAsync<JobExecutionRecord>(
                ResolveListStatement(),
                new
                {
                    Offset = offset,
                    PageSize = pageSize,
                    filter.JobDefinitionId,
                    filter.JobScheduleId,
                    filter.Status,
                    filter.FromUtc,
                    filter.ToUtc,
                },
                cancellationToken)
            .ConfigureAwait(false);
        return Result<PagedResult<HostJobExecutionResponse>>.Success(
            new PagedResult<HostJobExecutionResponse>(
                rows.Select(MapExecution).ToArray(),
                page,
                pageSize,
                total));
    }

    /// <summary>
    /// 清空指定作业定义下的终态执行记录（succeeded/failed），对应 Admin.NET ClearJobTriggerRecord。
    /// 保留 pending/running 记录以避免丢失正在运行的任务证据。
    /// </summary>
    public Task<Result<bool>> ClearAsync(
        Guid jobDefinitionId,
        CancellationToken cancellationToken = default) =>
        transaction.ExecuteResultAsync(
            token => ClearCoreAsync(jobDefinitionId, token),
            cancellationToken);

    private async Task<Result<bool>> ClearCoreAsync(
        Guid jobDefinitionId,
        CancellationToken cancellationToken)
    {
        await commandExecutor.ExecuteAsync(
                JobSql.ClearExecutionsByDefinition,
                new { JobDefinitionId = jobDefinitionId },
                cancellationToken)
            .ConfigureAwait(false);
        return Result<bool>.Success(true);
    }

    internal static HostJobExecutionResponse MapExecution(JobExecutionRecord record) =>
        new(
            record.Id,
            record.JobDefinitionId,
            record.JobScheduleId,
            record.Status,
            record.TriggerKind,
            record.ScheduledForUtc,
            record.ErrorMessage,
            record.StartedAtUtc,
            record.FinishedAtUtc,
            record.NextAttemptAtUtc,
            record.AttemptCount,
            record.CreatedAtUtc);

    private SqlStatement ResolveListStatement() =>
        databaseOptions.Value.Provider switch
        {
            DatabaseProvider.SqlServer => JobSql.ListExecutionsSqlServer,
            DatabaseProvider.MySql => JobSql.ListExecutionsMySql,
            _ => throw new InvalidOperationException(
                $"Unsupported database provider '{databaseOptions.Value.Provider}'.")
        };
}
