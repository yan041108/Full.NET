using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Jobs.Contracts;
using Full.NET.Modules.Jobs.Persistence;

namespace Full.NET.Modules.Jobs.Features.ManageHostJobExecutions;

/// <summary>
/// 处理 Host 任务执行的取消请求：pending 直接终态为 cancelled，running 标记为 cancelling 并由 Worker 协作停止。
/// 不把 HTTP 请求断开当作业务回滚；取消语义与租约终态由执行器统一收敛。
/// </summary>
internal sealed class HostJobExecutionCancelService(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    IClock clock,
    HostJobExecutionQueryService queries)
{
    /// <summary>
    /// 请求取消指定执行记录；cancelling 视为幂等成功，终态返回不可取消错误。
    /// </summary>
    /// <param name="executionId">执行记录标识。</param>
    /// <param name="cancellationToken">调用方取消令牌。</param>
    public async Task<Result<HostJobExecutionResponse>> CancelAsync(
        Guid executionId,
        CancellationToken cancellationToken = default)
    {
        var record = await queryExecutor.QuerySingleOrDefaultAsync<JobExecutionRecord>(
                JobSql.FindExecutionById,
                JobsSqlParameters.Create(("Id", executionId)),
                cancellationToken)
            .ConfigureAwait(false);
        if (record is null)
        {
            return Result<HostJobExecutionResponse>.Failure(new Error(
                JobsErrorCodes.ExecutionNotFound,
                "The job execution was not found.",
                ErrorType.NotFound));
        }

        if (string.Equals(
                record.Status,
                JobExecutionStatuses.Cancelling,
                StringComparison.Ordinal)
            || string.Equals(
                record.Status,
                JobExecutionStatuses.Cancelled,
                StringComparison.Ordinal))
        {
            return Result<HostJobExecutionResponse>.Success(
                HostJobExecutionQueryService.MapExecution(record));
        }

        if (JobExecutionStatuses.IsTerminal(record.Status))
        {
            return Result<HostJobExecutionResponse>.Failure(new Error(
                JobsErrorCodes.ExecutionNotCancellable,
                "The job execution cannot be cancelled in its current state.",
                ErrorType.Conflict));
        }

        if (string.Equals(
                record.Status,
                JobExecutionStatuses.Pending,
                StringComparison.Ordinal))
        {
            var cancelledRows = await commandExecutor.ExecuteAsync(
                    JobSql.CancelPendingExecution,
                    JobsSqlParameters.Create(
                        ("Id", executionId),
                        ("PendingStatus", JobExecutionStatuses.Pending),
                        ("CancelledStatus", JobExecutionStatuses.Cancelled),
                        ("FinishedAtUtc", clock.UtcNow)),
                    cancellationToken)
                .ConfigureAwait(false);
            if (cancelledRows == 0)
            {
                return await CancelAsync(executionId, cancellationToken)
                    .ConfigureAwait(false);
            }
        }
        else if (string.Equals(
                     record.Status,
                     JobExecutionStatuses.Running,
                     StringComparison.Ordinal))
        {
            var requestedRows = await commandExecutor.ExecuteAsync(
                    JobSql.RequestCancelRunningExecution,
                    JobsSqlParameters.Create(
                        ("Id", executionId),
                        ("RunningStatus", JobExecutionStatuses.Running),
                        ("CancellingStatus", JobExecutionStatuses.Cancelling)),
                    cancellationToken)
                .ConfigureAwait(false);
            if (requestedRows == 0)
            {
                return await CancelAsync(executionId, cancellationToken)
                    .ConfigureAwait(false);
            }
        }
        else
        {
            return Result<HostJobExecutionResponse>.Failure(new Error(
                JobsErrorCodes.ExecutionNotCancellable,
                "The job execution cannot be cancelled in its current state.",
                ErrorType.Conflict));
        }

        return await queries.GetByIdAsync(executionId, cancellationToken)
            .ConfigureAwait(false);
    }
}
