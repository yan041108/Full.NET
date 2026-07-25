using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Jobs.Contracts;
using Full.NET.Modules.Jobs.Execution;
using Full.NET.Modules.Jobs.Features.ManageHostJobDefinitions;
using Full.NET.Modules.Jobs.Persistence;

namespace Full.NET.Modules.Jobs.Features.ManageHostJobExecutions;

/// <summary>手动触发 Host 任务执行。</summary>
internal sealed class HostJobTriggerService(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    ICommandTransaction transaction,
    HostJobExecutionQueryService queries,
    JobExecutionRunner runner,
    IClock clock,
    IIdGenerator idGenerator)
{
    public async Task<Result<HostJobExecutionResponse>> TriggerAsync(
        Guid definitionId,
        CancellationToken cancellationToken = default)
    {
        var createResult = await transaction.ExecuteAsync(
                token => CreatePendingExecutionAsync(definitionId, token),
                cancellationToken)
            .ConfigureAwait(false);
        if (!createResult.IsSuccess)
        {
            return Result<HostJobExecutionResponse>.Failure(createResult.Error!);
        }

        await runner.ProcessPendingAsync(cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        return await queries.GetByIdAsync(createResult.Value, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<Result<Guid>> CreatePendingExecutionAsync(
        Guid definitionId,
        CancellationToken cancellationToken)
    {
        var definition = await queryExecutor.QuerySingleOrDefaultAsync<JobDefinitionRecord>(
                JobSql.FindDefinitionById,
                new { Id = definitionId },
                cancellationToken)
            .ConfigureAwait(false);
        if (definition is null)
        {
            return HostJobDefinitionQueryService.DefinitionNotFound<Guid>();
        }

        if (!definition.IsEnabled)
        {
            return Result<Guid>.Failure(new Error(
                JobsErrorCodes.DefinitionDisabled,
                "The job definition is disabled.",
                ErrorType.Validation));
        }

        var executionId = idGenerator.NewId();
        var now = clock.UtcNow;
        await commandExecutor.ExecuteAsync(
                JobSql.InsertExecution,
                new
                {
                    Id = executionId,
                    JobDefinitionId = definitionId,
                    Status = JobExecutionStatuses.Pending,
                    TriggerKind = JobTriggerKinds.Manual,
                    CreatedAtUtc = now,
                },
                cancellationToken)
            .ConfigureAwait(false);

        return Result<Guid>.Success(executionId);
    }
}
