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
    /// <param name="occurredAtUtc">统一业务发生时间。</param>
    /// <param name="cancellationToken">调用方本地事务取消令牌。</param>
    public async Task WriteAsync(
        Guid instanceId,
        string tenantScopeKey,
        IReadOnlyList<WorkflowCcRuntimeNode> ccNodes,
        DateTimeOffset occurredAtUtc,
        CancellationToken cancellationToken = default)
    {
        if (ccNodes.Count == 0)
        {
            return;
        }

        var existing = await queryExecutor.QueryAsync<Guid>(
            WorkflowSql.ListCcRecipientIdsByInstance,
            WorkflowSqlParameters.Create(
                ("InstanceId", instanceId),
                ("TenantScopeKey", tenantScopeKey)),
            cancellationToken).ConfigureAwait(false);
        var knownRecipients = existing.ToHashSet();

        foreach (var node in ccNodes)
        {
            var stepId = idGenerator.NewId();
            await commandExecutor.ExecuteAsync(
                WorkflowSql.InsertCompletedCcStep,
                WorkflowSqlParameters.Create(
                    ("Id", stepId),
                    ("InstanceId", instanceId),
                    ("NodeKey", node.NodeKey),
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
}
