using Full.NET.Abstractions.Ids;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Workflow.Persistence;

namespace Full.NET.Modules.Workflow.Domain;

/// <summary>在 Workflow 本地事务中维护并行汇合状态与分支到达事实。</summary>
/// <param name="commandExecutor">显式 SQL 命令执行器。</param>
/// <param name="queryExecutor">显式 SQL 查询执行器。</param>
/// <param name="idGenerator">UUID v7 标识生成器。</param>
internal sealed class WorkflowParallelJoinCoordinator(
    ICommandExecutor commandExecutor,
    IQueryExecutor queryExecutor,
    IIdGenerator idGenerator)
{
    /// <summary>为一次并行分叉创建等待汇合的状态记录。</summary>
    /// <param name="instanceId">工作流实例标识。</param>
    /// <param name="forkNodeKey">分叉节点键。</param>
    /// <param name="joinNodeKey">汇合节点键。</param>
    /// <param name="requiredBranchCount">需要到达汇合的分支总数。</param>
    /// <param name="createdAtUtc">创建时间。</param>
    /// <param name="cancellationToken">取消当前事务写入的令牌。</param>
    /// <returns>新创建的汇合状态标识。</returns>
    public async Task<Guid> CreateJoinAsync(
        Guid instanceId,
        string forkNodeKey,
        string joinNodeKey,
        int requiredBranchCount,
        DateTimeOffset createdAtUtc,
        string gatewayTypeKey = "parallel",
        CancellationToken cancellationToken = default)
    {
        var joinId = idGenerator.NewId();
        await commandExecutor.ExecuteAsync(
            WorkflowSql.InsertParallelJoin,
            WorkflowSqlParameters.Create(
                ("Id", joinId),
                ("InstanceId", instanceId),
                ("ForkNodeKey", forkNodeKey),
                ("JoinNodeKey", joinNodeKey),
                ("GatewayTypeKey", gatewayTypeKey),
                ("RequiredBranchCount", requiredBranchCount),
                ("CreatedAtUtc", createdAtUtc)),
            cancellationToken).ConfigureAwait(false);
        return joinId;
    }

    /// <summary>记录单个并行分支到达汇合点，并在全部到达时完成汇合。</summary>
    /// <param name="instanceId">工作流实例标识。</param>
    /// <param name="parallelJoinId">汇合状态标识。</param>
    /// <param name="branchKey">稳定分支键。</param>
    /// <param name="arrivedAtUtc">到达时间。</param>
    /// <param name="cancellationToken">取消当前事务写入的令牌。</param>
    /// <returns>到达结果；重复到达时不会再次递增计数。</returns>
    public async Task<WorkflowParallelJoinArrivalResult> TryRecordArrivalAsync(
        Guid instanceId,
        Guid parallelJoinId,
        string branchKey,
        DateTimeOffset arrivedAtUtc,
        CancellationToken cancellationToken = default)
    {
        var join = await queryExecutor.QuerySingleOrDefaultAsync<WorkflowParallelJoinRecord>(
            WorkflowSql.FindParallelJoinById,
            WorkflowSqlParameters.Create(("Id", parallelJoinId), ("InstanceId", instanceId)),
            cancellationToken).ConfigureAwait(false);
        if (join is null || join.StatusKey != "waiting")
        {
            return new WorkflowParallelJoinArrivalResult(false, false, join);
        }

        var isNewArrival = true;
        try
        {
            await commandExecutor.ExecuteAsync(
                WorkflowSql.InsertParallelBranchArrival,
                WorkflowSqlParameters.Create(
                    ("Id", idGenerator.NewId()),
                    ("ParallelJoinId", parallelJoinId),
                    ("BranchKey", branchKey),
                    ("ArrivedAtUtc", arrivedAtUtc)),
                cancellationToken).ConfigureAwait(false);
        }
        catch (DataCommandException exception) when (exception.Kind == DataCommandFailureKind.UniqueConstraint)
        {
            isNewArrival = false;
        }

        if (isNewArrival)
        {
            var updated = await commandExecutor.ExecuteAsync(
                WorkflowSql.IncrementParallelJoinArrival,
                WorkflowSqlParameters.Create(
                    ("Id", parallelJoinId),
                    ("InstanceId", instanceId),
                    ("Revision", join.Revision),
                    ("CompletedAtUtc", arrivedAtUtc)),
                cancellationToken).ConfigureAwait(false);
            if (updated != 1)
            {
                throw new InvalidOperationException("Failed to increment parallel join arrival count.");
            }
        }

        join = await queryExecutor.QuerySingleOrDefaultAsync<WorkflowParallelJoinRecord>(
            WorkflowSql.FindParallelJoinById,
            WorkflowSqlParameters.Create(("Id", parallelJoinId), ("InstanceId", instanceId)),
            cancellationToken).ConfigureAwait(false);
        return new WorkflowParallelJoinArrivalResult(
            isNewArrival,
            join?.StatusKey == "completed",
            join);
    }

    /// <summary>取消实例上仍在等待的并行汇合状态。</summary>
    /// <param name="instanceId">工作流实例标识。</param>
    /// <param name="completedAtUtc">取消时间。</param>
    /// <param name="cancellationToken">取消当前事务写入的令牌。</param>
    public Task CancelWaitingJoinsAsync(
        Guid instanceId,
        DateTimeOffset completedAtUtc,
        CancellationToken cancellationToken = default) =>
        commandExecutor.ExecuteAsync(
            WorkflowSql.CancelWaitingParallelJoinsByInstance,
            WorkflowSqlParameters.Create(
                ("InstanceId", instanceId),
                ("CompletedAtUtc", completedAtUtc)),
            cancellationToken);
}

/// <summary>描述并行分支到达汇合点的结果。</summary>
/// <param name="IsNewArrival">本次调用是否首次记录该分支到达。</param>
/// <param name="IsJoinComplete">汇合是否因全部分支到达而完成。</param>
/// <param name="Join">最新汇合状态快照。</param>
internal sealed record WorkflowParallelJoinArrivalResult(
    bool IsNewArrival,
    bool IsJoinComplete,
    WorkflowParallelJoinRecord? Join);
