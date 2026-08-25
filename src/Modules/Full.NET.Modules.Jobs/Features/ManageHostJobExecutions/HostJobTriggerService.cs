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

/// <summary>
/// Host 任务手动触发执行服务。按定义 Id 定位已启用定义后，事务内立即写入一条 Pending 状态执行记录（TriggerKind=Manual），
/// 紧接着调用 JobExecutionRunner.ProcessPendingAsync 在当前作用域内立刻调度执行该实例（无需等待下一次轮询周期），
/// 最后回查最新执行状态返回。全程单事务写入执行记录，确保立即触发可追溯。
/// </summary>
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
        var createResult = await transaction.ExecuteResultAsync(
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
                JobsSqlParameters.Create(("Id", definitionId)),
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
                JobsSqlParameters.Create(
                    ("Id", executionId),
                    ("JobDefinitionId", definitionId),
                    ("Status", JobExecutionStatuses.Pending),
                    ("TriggerKind", JobTriggerKinds.Manual),
                    ("CreatedAtUtc", now)
                ),
                cancellationToken)
            .ConfigureAwait(false);

        return Result<Guid>.Success(executionId);
    }
}
