using Full.NET.Abstractions.Ids;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Workflow.Persistence;

namespace Full.NET.Modules.Workflow.Domain;

/// <summary>在调用方的 Workflow 本地事务内落库同步抄送步骤和实例级收件人。</summary>
/// <param name="queryExecutor">受控查询执行器。</param>
/// <param name="commandExecutor">显式 SQL 命令执行器。</param>
/// <param name="idGenerator">UUID v7 标识生成器。</param>
internal sealed class WorkflowCcTransitionWriter(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    IIdGenerator idGenerator)
{
    /// <summary>按计划顺序写入抄送步骤，并跳过当前实例已经知会的用户。</summary>
    /// <param name="instanceId">工作流实例标识。</param>
    /// <param name="tenantScopeKey">可信租户作用域键。</param>
    /// <param name="ccNodes">经过编译器验证的有序抄送节点。</param>
    /// <param name="nextExecutionSequence">首个抄送步骤可用的实例内单调执行序号。</param>
    /// <param name="occurredAtUtc">统一业务发生时间。</param>
    /// <param name="cancellationToken">调用方本地事务取消令牌。</param>
    /// <returns>全部抄送步骤写入后，下一个步骤可用的执行序号。</returns>
    public async Task<long> WriteAsync(
        Guid instanceId,
        string tenantScopeKey,
        IReadOnlyList<WorkflowCcRuntimeNode> ccNodes,
        long nextExecutionSequence,
        DateTimeOffset occurredAtUtc,
        CancellationToken cancellationToken = default)
    {
        if (ccNodes.Count == 0)
        {
            return nextExecutionSequence;
        }

        var knownRecipients = await LoadKnownRecipientsAsync(
            instanceId,
            tenantScopeKey,
            cancellationToken).ConfigureAwait(false);

        foreach (var node in ccNodes)
        {
            var executionSequence = nextExecutionSequence++;
            await WriteNodeAsync(
                instanceId,
                node,
                knownRecipients,
                executionSequence,
                occurredAtUtc,
                cancellationToken).ConfigureAwait(false);
        }

        return nextExecutionSequence;
    }

    /// <summary>一次读取实例已知抄送人，供混合自动节点迁移复用。</summary>
    /// <param name="instanceId">工作流实例标识。</param>
    /// <param name="tenantScopeKey">可信租户作用域键。</param>
    /// <param name="cancellationToken">调用方本地事务取消令牌。</param>
    /// <returns>可在后续节点间共享的实例级抄送人集合。</returns>
    internal async Task<HashSet<Guid>> LoadKnownRecipientsAsync(
        Guid instanceId,
        string tenantScopeKey,
        CancellationToken cancellationToken)
    {
        var existing = await queryExecutor.QueryAsync<Guid>(
            WorkflowSql.ListCcRecipientIdsByInstance,
            WorkflowSqlParameters.Create(
                ("InstanceId", instanceId),
                ("TenantScopeKey", tenantScopeKey)),
            cancellationToken).ConfigureAwait(false);
        return existing.ToHashSet();
    }

    /// <summary>写入单个抄送步骤，并使用迁移级集合保持实例收件人去重。</summary>
    /// <param name="instanceId">工作流实例标识。</param>
    /// <param name="node">经过编译器验证的抄送节点。</param>
    /// <param name="knownRecipients">同一实例已经知会的用户集合。</param>
    /// <param name="executionSequence">本次实例迁移的单调执行序号。</param>
    /// <param name="occurredAtUtc">统一业务发生时间。</param>
    /// <param name="cancellationToken">调用方本地事务取消令牌。</param>
    internal async Task WriteNodeAsync(
        Guid instanceId,
        WorkflowCcRuntimeNode node,
        ISet<Guid> knownRecipients,
        long executionSequence,
        DateTimeOffset occurredAtUtc,
        CancellationToken cancellationToken)
    {
        var stepId = idGenerator.NewId();
        await commandExecutor.ExecuteAsync(
            WorkflowSql.InsertCompletedCcStep,
            WorkflowSqlParameters.Create(
                ("Id", stepId),
                ("InstanceId", instanceId),
                ("NodeKey", node.NodeKey),
                ("ExecutionSequence", executionSequence),
                ("StartedAtUtc", occurredAtUtc),
                ("CompletedAtUtc", occurredAtUtc)),
            cancellationToken).ConfigureAwait(false);

        var insertedCount = 0;
        foreach (var recipientUserId in node.RecipientUserIds)
        {
            // 表结构按实例和用户唯一；后续抄送节点只保留执行轨迹，不重复制造知识记录。
            if (!knownRecipients.Add(recipientUserId))
            {
                continue;
            }

            await commandExecutor.ExecuteAsync(
                WorkflowSql.InsertCc,
                WorkflowSqlParameters.Create(
                    ("Id", idGenerator.NewId()),
                    ("InstanceId", instanceId),
                    ("StepId", stepId),
                    ("RecipientUserId", recipientUserId),
                    ("CreatedAtUtc", occurredAtUtc)),
                cancellationToken).ConfigureAwait(false);
            insertedCount++;
        }

        await commandExecutor.ExecuteAsync(
            WorkflowSql.InsertExecutionLog,
            WorkflowSqlParameters.Create(
                ("Id", idGenerator.NewId()),
                ("InstanceId", instanceId),
                ("StepId", stepId),
                ("TransitionKey", "node.notify.cc"),
                ("FromStatusKey", null),
                ("ToStatusKey", "completed"),
                ("IdempotencyKey", null),
                ("Summary", $"recipients:{insertedCount}"),
                ("CreatedAtUtc", occurredAtUtc)),
            cancellationToken).ConfigureAwait(false);
    }
}
