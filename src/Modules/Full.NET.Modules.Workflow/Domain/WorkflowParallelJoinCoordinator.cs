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
    /// <param name="executionEpochId">实际执行代次；不能绑定另一轮分叉。</param>
    /// <param name="forkNodeKey">分叉节点键。</param>
    /// <param name="joinNodeKey">汇合节点键。</param>
    /// <param name="requiredBranchCount">需要到达汇合的分支总数。</param>
    /// <param name="createdAtUtc">创建时间。</param>
    /// <param name="cancellationToken">取消当前事务写入的令牌。</param>
    /// <returns>新创建的汇合状态标识。</returns>
    public async Task<Guid> CreateJoinAsync(
        Guid instanceId,
        Guid executionEpochId,
        string forkNodeKey,
        string joinNodeKey,
        int requiredBranchCount,
        DateTimeOffset createdAtUtc,
        string gatewayTypeKey = "parallel",
        CancellationToken cancellationToken = default, Guid? forkStepId = null)
    {
        if (forkStepId is null || forkStepId==Guid.Empty) throw new InvalidOperationException("workflow.causality.fork_missing");
        var joinId = idGenerator.NewId();
        var inserted = await commandExecutor.ExecuteAsync(
            WorkflowSql.InsertParallelJoin,
            WorkflowSqlParameters.Create(
                ("Id", joinId),
                ("InstanceId", instanceId),
                ("ExecutionEpochId", executionEpochId),
                ("ForkNodeKey", forkNodeKey),
                ("JoinNodeKey", joinNodeKey),
                ("GatewayTypeKey", gatewayTypeKey),
                ("RequiredBranchCount", requiredBranchCount),
                ("CreatedAtUtc", createdAtUtc),("ForkStepId",forkStepId)),
            cancellationToken).ConfigureAwait(false);
        if (inserted != 1) throw new InvalidOperationException("workflow.execution_epoch.missing");
        return joinId;
    }

    /// <summary>记录单个并行分支到达汇合点，并在全部到达时完成汇合。</summary>
    /// <param name="instanceId">工作流实例标识。</param>
    /// <param name="executionEpochId">本次汇合的目标代次；跨代次来源须由原路由或退回后继收据证明。</param>
    /// <param name="parallelJoinId">汇合状态标识。</param>
    /// <param name="forkNodeKey">当前发布迁移绑定的分叉节点键。</param>
    /// <param name="joinNodeKey">当前发布迁移绑定的汇合节点键。</param>
    /// <param name="branchKey">稳定分支键。</param>
    /// <param name="arrivedAtUtc">到达时间。</param>
    /// <param name="cancellationToken">取消当前事务写入的令牌。</param>
    /// <returns>到达结果；重复到达时不会再次递增计数。</returns>
    public async Task<WorkflowParallelJoinArrivalResult> TryRecordArrivalAsync(
        Guid instanceId,
        Guid executionEpochId,
        Guid parallelJoinId,
        string forkNodeKey,
        string joinNodeKey,
        string branchKey,
        DateTimeOffset arrivedAtUtc,
        CancellationToken cancellationToken = default, Guid? sourceStepId = null, string? tenantScopeKey = null,
        WorkflowRouteContinuationProof? routeProof = null, WorkflowReturnContinuationProof? returnProof = null)
    {
        var join = await queryExecutor.QuerySingleOrDefaultAsync<WorkflowParallelJoinRecord>(
            WorkflowSql.FindParallelJoinById,
            WorkflowSqlParameters.Create(("Id", parallelJoinId), ("InstanceId", instanceId)),
            cancellationToken).ConfigureAwait(false);
        if (executionEpochId == Guid.Empty || join is null || join.Id != parallelJoinId || join.InstanceId != instanceId ||
            join.ExecutionEpochId != executionEpochId || join.ForkNodeKey != forkNodeKey || join.JoinNodeKey != joinNodeKey ||
            await queryExecutor.QuerySingleOrDefaultAsync<int>(WorkflowBranchSql.CountSelectedBranch,
                WorkflowSqlParameters.Create(("ParallelJoinId", parallelJoinId), ("BranchKey", branchKey)), cancellationToken)
                .ConfigureAwait(false) != 1)
            return new WorkflowParallelJoinArrivalResult(false, false, join, false);
        if (sourceStepId is null || sourceStepId==Guid.Empty || string.IsNullOrWhiteSpace(tenantScopeKey) ||
            returnProof is not null && (routeProof is not null || returnProof.NodeKey != joinNodeKey) ||
            routeProof is not null && (routeProof.RouteId!=sourceStepId || routeProof.NodeKey!=joinNodeKey) ||
            await queryExecutor.QuerySingleOrDefaultAsync<int>(returnProof is not null ? WorkflowReturnRoundSql.CountJoinSourceProof :
                routeProof is null?WorkflowCausalitySql.CountSourceFrame:WorkflowRouteSql.CountJoinSourceProof,
                WorkflowSqlParameters.Create(("InstanceId",instanceId),("ExecutionEpochId",executionEpochId),("ParallelJoinId",parallelJoinId),
                    ("ParallelBranchKey",branchKey),("SourceStepId",sourceStepId),("TenantScopeKey",tenantScopeKey),
                    ("RouteDecisionId",routeProof?.RouteId),("ReturnRoundId",returnProof?.RoundId),
                    ("WorkId",returnProof?.WorkId ?? routeProof?.WorkId),("NodeKey",returnProof?.NodeKey ?? routeProof?.NodeKey),
                    ("InputHash",returnProof?.InputHash ?? routeProof?.InputHash),("InputFormRevision",returnProof?.InputFormRevision),
                    ("CauseKindKey",returnProof is null ? "region" : "return"),("JoinNodeKey",joinNodeKey)),cancellationToken).ConfigureAwait(false)!=1)
            return new WorkflowParallelJoinArrivalResult(false,false,join,false);
        if (join.StatusKey != "waiting")
        {
            return new WorkflowParallelJoinArrivalResult(false, false, join, join.StatusKey == "completed");
        }

        // 调用方已经持有实例锁；先读去重事实，避免唯一约束异常使 SQL Server 外层事务不可提交。
        if (await queryExecutor.QuerySingleOrDefaultAsync<int>(WorkflowBranchSql.CountArrival,
                WorkflowSqlParameters.Create(("ParallelJoinId", parallelJoinId), ("BranchKey", branchKey)), cancellationToken)
                .ConfigureAwait(false) != 0)
            return new WorkflowParallelJoinArrivalResult(false, false, join);
        await commandExecutor.ExecuteAsync(
                WorkflowSql.InsertParallelBranchArrival,
                WorkflowSqlParameters.Create(
                    ("Id", idGenerator.NewId()),
                    ("ParallelJoinId", parallelJoinId),
                    ("BranchKey", branchKey),
                    ("ArrivedAtUtc", arrivedAtUtc),("InstanceId",instanceId),("SourceStepId",sourceStepId)),
                cancellationToken).ConfigureAwait(false);
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


        join = await queryExecutor.QuerySingleOrDefaultAsync<WorkflowParallelJoinRecord>(
            WorkflowSql.FindParallelJoinById,
            WorkflowSqlParameters.Create(("Id", parallelJoinId), ("InstanceId", instanceId)),
            cancellationToken).ConfigureAwait(false);
        return new WorkflowParallelJoinArrivalResult(
            true,
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
/// <param name="IsValid">到达身份必须属于本次分叉选定的分支。</param>
internal sealed record WorkflowParallelJoinArrivalResult(
    bool IsNewArrival,
    bool IsJoinComplete,
    WorkflowParallelJoinRecord? Join,
    bool IsValid = true);
