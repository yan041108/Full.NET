using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Workflow.Domain;
using Full.NET.Modules.Workflow.Persistence;

namespace Full.NET.Modules.Workflow.Execution;

/// <summary>扫描卡住实例、未完成步骤和过期租约，并幂等写入未关闭恢复任务。</summary>
/// <param name="queryExecutor">三类扫描查询执行器。</param>
/// <param name="commandExecutor">占用去重插入执行器。</param>
/// <param name="clock">UTC 时钟。</param>
/// <param name="idGenerator">生成新恢复任务标识。</param>
internal sealed class WorkflowRecoveryScanner(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    IClock clock,
    IIdGenerator idGenerator)
{
    /// <summary>扫描三类异常并补齐占用中的恢复任务；重复扫描不得插入第二行。</summary>
    /// <param name="cancellationToken">取消当前扫描的令牌。</param>
    public async Task ScanAsync(CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        await EnqueueAsync(
                WorkflowRecoverySql.ScanExpiredLeases,
                WorkflowRecoveryKinds.ExpiredLease,
                now,
                cancellationToken)
            .ConfigureAwait(false);
        await EnqueueAsync(
                WorkflowRecoverySql.ScanStuckInstances,
                WorkflowRecoveryKinds.StuckInstance,
                now,
                cancellationToken)
            .ConfigureAwait(false);
        await EnqueueAsync(
                WorkflowRecoverySql.ScanIncompleteSteps,
                WorkflowRecoveryKinds.IncompleteStep,
                now,
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>把扫描候选插入为未关闭恢复任务；占用冲突时保持原行。</summary>
    /// <param name="scan">当前种类对应的扫描 SQL。</param>
    /// <param name="kindKey">恢复种类键。</param>
    /// <param name="now">当前 UTC 时间。</param>
    /// <param name="cancellationToken">取消当前扫描的令牌。</param>
    private async Task EnqueueAsync(
        SqlStatement scan,
        string kindKey,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var candidates = await queryExecutor.QueryAsync<WorkflowRecoveryScanCandidate>(
                scan,
                WorkflowSqlParameters.Create(("Now", now)),
                cancellationToken)
            .ConfigureAwait(false);
        foreach (var candidate in candidates)
        {
            await commandExecutor.ExecuteAsync(
                    WorkflowRecoverySql.InsertOpenTask,
                    WorkflowSqlParameters.Create(
                        ("Id", idGenerator.NewId()),
                        ("TenantId", candidate.TenantId),
                        ("ScopeKey", candidate.ScopeKey),
                        ("TenantScopeKey", candidate.TenantScopeKey),
                        ("InstanceId", candidate.InstanceId),
                        ("StepId", candidate.StepId),
                        ("KindKey", kindKey),
                        ("CreatedAtUtc", now),
                        ("UpdatedAtUtc", now)),
                    cancellationToken)
                .ConfigureAwait(false);
        }
    }
}
