using Full.NET.Abstractions.Ids;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Workflow.Persistence;

namespace Full.NET.Modules.Workflow.Domain;

/// <summary>在调用方本地事务中按运行路径顺序落库抄送与排他网关自动步骤。</summary>
/// <param name="commandExecutor">显式 SQL 命令执行器。</param>
/// <param name="idGenerator">UUID v7 标识生成器。</param>
/// <param name="ccTransitionWriter">同步抄送步骤写入器。</param>
internal sealed class WorkflowAutomaticTransitionWriter(
    ICommandExecutor commandExecutor,
    IIdGenerator idGenerator,
    WorkflowCcTransitionWriter ccTransitionWriter)
{
    /// <summary>按运行计划顺序写入自动步骤和对应执行日志。</summary>
    /// <param name="instanceId">工作流实例标识。</param>
    /// <param name="tenantScopeKey">可信租户作用域键。</param>
    /// <param name="automaticNodes">经过编译与运行时求值的有序自动节点。</param>
    /// <param name="nextExecutionSequence">首个自动步骤可用的实例内单调执行序号。</param>
    /// <param name="occurredAtUtc">统一业务发生时间。</param>
    /// <param name="cancellationToken">调用方本地事务取消令牌。</param>
    /// <returns>全部自动步骤写入后，下一个步骤可用的执行序号。</returns>
    public async Task<long> WriteAsync(
        Guid instanceId,
        string tenantScopeKey,
        IReadOnlyList<WorkflowAutomaticRuntimeNode> automaticNodes,
        long nextExecutionSequence,
        DateTimeOffset occurredAtUtc,
        CancellationToken cancellationToken = default)
    {
        HashSet<Guid> knownRecipients = automaticNodes.Any(node => node.NodeTypeKey == "notify.cc")
            ? await ccTransitionWriter.LoadKnownRecipientsAsync(
                instanceId,
                tenantScopeKey,
                cancellationToken).ConfigureAwait(false)
            : [];
        foreach (var node in automaticNodes)
        {
            var executionSequence = nextExecutionSequence++;
            if (node.NodeTypeKey == "notify.cc")
            {
                await ccTransitionWriter.WriteNodeAsync(
                    instanceId,
                    new WorkflowCcRuntimeNode(node.NodeKey, node.RecipientUserIds),
                    knownRecipients,
                    executionSequence,
                    occurredAtUtc,
                    cancellationToken).ConfigureAwait(false);
                continue;
            }

            if (node.NodeTypeKey != "gateway.exclusive" || string.IsNullOrWhiteSpace(node.OutcomeKey))
            {
                if (node.NodeTypeKey is "gateway.parallel" or "gateway.inclusive")
                {
                    var parallelStepId = idGenerator.NewId();
                    await commandExecutor.ExecuteAsync(
                        WorkflowSql.InsertCompletedParallelGatewayStep,
                        WorkflowSqlParameters.Create(
                            ("Id", parallelStepId),
                            ("InstanceId", instanceId),
                            ("NodeKey", node.NodeKey),
                            ("ExecutionSequence", executionSequence),
                            ("ParallelJoinId", null),
                            ("ParallelBranchKey", node.OutcomeKey),
                            ("StartedAtUtc", occurredAtUtc),
                            ("CompletedAtUtc", occurredAtUtc)),
                        cancellationToken).ConfigureAwait(false);
                    var transitionKey = node.NodeTypeKey switch
                    {
                        "gateway.inclusive" => string.IsNullOrWhiteSpace(node.OutcomeKey) ||
                            node.OutcomeKey == "joined"
                                ? "node.gateway.inclusive.join"
                                : "node.gateway.inclusive.fork",
                        _ => string.IsNullOrWhiteSpace(node.OutcomeKey) ||
                            node.OutcomeKey == "joined"
                                ? "node.gateway.parallel.join"
                                : "node.gateway.parallel.fork",
                    };
                    await commandExecutor.ExecuteAsync(
                        WorkflowSql.InsertExecutionLog,
                        WorkflowSqlParameters.Create(
                            ("Id", idGenerator.NewId()),
                            ("InstanceId", instanceId),
                            ("StepId", parallelStepId),
                            ("TransitionKey", transitionKey),
                            ("FromStatusKey", null),
                            ("ToStatusKey", "completed"),
                            ("IdempotencyKey", null),
                            ("Summary", node.OutcomeKey is null ? null : $"branch:{node.OutcomeKey}"),
                            ("CreatedAtUtc", occurredAtUtc)),
                        cancellationToken).ConfigureAwait(false);
                    continue;
                }

                throw new InvalidOperationException($"Unsupported workflow automatic node '{node.NodeTypeKey}'.");
            }

            var stepId = idGenerator.NewId();
            await commandExecutor.ExecuteAsync(
                WorkflowSql.InsertCompletedGatewayStep,
                WorkflowSqlParameters.Create(
                    ("Id", stepId),
                    ("InstanceId", instanceId),
                    ("NodeKey", node.NodeKey),
                    ("ExecutionSequence", executionSequence),
                    ("StartedAtUtc", occurredAtUtc),
                    ("CompletedAtUtc", occurredAtUtc)),
                cancellationToken).ConfigureAwait(false);
            await commandExecutor.ExecuteAsync(
                WorkflowSql.InsertExecutionLog,
                WorkflowSqlParameters.Create(
                    ("Id", idGenerator.NewId()),
                    ("InstanceId", instanceId),
                    ("StepId", stepId),
                    ("TransitionKey", "node.gateway.exclusive"),
                    ("FromStatusKey", null),
                    ("ToStatusKey", "completed"),
                    ("IdempotencyKey", null),
                    ("Summary", $"branch:{node.OutcomeKey}"),
                    ("CreatedAtUtc", occurredAtUtc)),
                cancellationToken).ConfigureAwait(false);
        }

        return nextExecutionSequence;
    }
}
