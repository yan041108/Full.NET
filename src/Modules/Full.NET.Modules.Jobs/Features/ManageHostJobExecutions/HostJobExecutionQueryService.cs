using Full.NET.Abstractions.Results;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Jobs.Contracts;
using Full.NET.Modules.Jobs.Persistence;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Jobs.Features.ManageHostJobExecutions;

/// <summary>Host 任务执行记录分页查询。</summary>
internal sealed class HostJobExecutionQueryService(
    IQueryExecutor queryExecutor,
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
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var offset = (page - 1) * pageSize;
        var total = await queryExecutor.QuerySingleOrDefaultAsync<long>(
                JobSql.CountExecutions,
                new { JobDefinitionId = jobDefinitionId },
                cancellationToken)
            .ConfigureAwait(false);
        var rows = await queryExecutor.QueryAsync<JobExecutionRecord>(
                ResolveListStatement(),
                new
                {
                    Offset = offset,
                    PageSize = pageSize,
                    JobDefinitionId = jobDefinitionId,
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

    internal static HostJobExecutionResponse MapExecution(JobExecutionRecord record) =>
        new(
            record.Id,
            record.JobDefinitionId,
            record.Status,
            record.TriggerKind,
            record.ErrorMessage,
            record.StartedAtUtc,
            record.FinishedAtUtc,
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
