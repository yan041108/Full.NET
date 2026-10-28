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
        Guid executionEpochId,
        IReadOnlyList<WorkflowAutomaticRuntimeNode> automaticNodes,
        long nextExecutionSequence,
        DateTimeOffset occurredAtUtc,
        CancellationToken cancellationToken = default, WorkflowActivationTrace? trace=null)
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
            if (node.NodeTypeKey == "end")
            {
                if (trace is null) throw new InvalidOperationException("workflow.end.source_missing");
                var endId = idGenerator.NewId();
                if (await commandExecutor.ExecuteAsync(WorkflowEndSql.InsertStep, WorkflowSqlParameters.Create(("Id", endId),
                        ("InstanceId", instanceId), ("TenantScopeKey", tenantScopeKey), ("ExecutionEpochId", executionEpochId), ("NodeKey", node.NodeKey),
                        ("ExecutionSequence", executionSequence), ("Now", occurredAtUtc), ("ParallelJoinId", trace.ParallelJoinId),
                        ("ParallelBranchKey", trace.BranchKey)), cancellationToken).ConfigureAwait(false) != 1)
                    throw new InvalidOperationException("workflow.end.storage_conflict");
                await WorkflowActivationBindingWriter.BindAsync(commandExecutor, instanceId, tenantScopeKey, executionEpochId, endId,
                    occurredAtUtc, cancellationToken, trace, idGenerator).ConfigureAwait(false);
                await commandExecutor.ExecuteAsync(WorkflowSql.InsertExecutionLog, WorkflowSqlParameters.Create(("Id", idGenerator.NewId()),
                    ("InstanceId", instanceId), ("StepId", endId), ("CorrelationJson", Full.NET.Abstractions.Observability.LogCorrelationContext.Capture().ToJson()),
                    ("TransitionKey", "node.end"), ("FromStatusKey", null), ("ToStatusKey", "completed"), ("IdempotencyKey", null),
                    ("Summary", null), ("CreatedAtUtc", occurredAtUtc)), cancellationToken).ConfigureAwait(false);
                continue;
            }
            if (node.NodeTypeKey == "control.empty")
            {
                var emptyId = idGenerator.NewId();
                await commandExecutor.ExecuteAsync(WorkflowSql.InsertCompletedEmptyStep,
                    WorkflowSqlParameters.Create(("Id", emptyId), ("InstanceId", instanceId), ("ExecutionEpochId", executionEpochId), ("NodeKey", node.NodeKey),
                        ("ExecutionSequence", executionSequence), ("StartedAtUtc", occurredAtUtc), ("CompletedAtUtc", occurredAtUtc), ("ParallelJoinId",trace?.ParallelJoinId),("ParallelBranchKey",trace?.BranchKey)), cancellationToken).ConfigureAwait(false);
                await WorkflowActivationBindingWriter.BindAsync(commandExecutor,instanceId,tenantScopeKey,executionEpochId,emptyId,occurredAtUtc,cancellationToken,trace,idGenerator).ConfigureAwait(false);
                await commandExecutor.ExecuteAsync(WorkflowSql.InsertExecutionLog,
                    WorkflowSqlParameters.Create(("Id", idGenerator.NewId()), ("InstanceId", instanceId), ("ExecutionEpochId", executionEpochId), ("StepId", emptyId),
                        ("CorrelationJson", Full.NET.Abstractions.Observability.LogCorrelationContext.Capture().ToJson()),
                        ("TransitionKey", "node.control.empty"), ("FromStatusKey", null), ("ToStatusKey", "completed"),
                        ("IdempotencyKey", null), ("Summary", null), ("CreatedAtUtc", occurredAtUtc)), cancellationToken).ConfigureAwait(false);
                continue;
            }
            if (node.NodeTypeKey == "notify.cc")
            {
                await ccTransitionWriter.WriteNodeAsync(
                    instanceId,
                    executionEpochId,
                    new WorkflowCcRuntimeNode(node.NodeKey, node.RecipientUserIds, node.ResolutionJson, node.ResolutionHash),
                    knownRecipients,
                    executionSequence,
                    occurredAtUtc,
                    tenantScopeKey,
                    cancellationToken, trace).ConfigureAwait(false);
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
                            ("InstanceId", instanceId), ("ExecutionEpochId", executionEpochId),
                            ("NodeKey", node.NodeKey),
                            ("NodeTypeKey", node.NodeTypeKey),
                            ("ExecutionSequence", executionSequence),
                            ("ParallelJoinId", trace?.ParallelJoinId),
                            ("ParallelBranchKey", trace?.BranchKey),
                            ("StartedAtUtc", occurredAtUtc),
                            ("CompletedAtUtc", occurredAtUtc)),
                        cancellationToken).ConfigureAwait(false);
                await WorkflowActivationBindingWriter.BindAsync(commandExecutor,instanceId,tenantScopeKey,executionEpochId,parallelStepId,occurredAtUtc,cancellationToken,trace,idGenerator).ConfigureAwait(false);
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
                    ("CorrelationJson", Full.NET.Abstractions.Observability.LogCorrelationContext.Capture().ToJson()),
                            ("Id", idGenerator.NewId()),
                            ("InstanceId", instanceId), ("ExecutionEpochId", executionEpochId),
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
                    ("InstanceId", instanceId), ("ExecutionEpochId", executionEpochId),
                    ("NodeKey", node.NodeKey),
                    ("ExecutionSequence", executionSequence),
                    ("StartedAtUtc", occurredAtUtc),
                    ("CompletedAtUtc", occurredAtUtc), ("ParallelJoinId",trace?.ParallelJoinId),("ParallelBranchKey",trace?.BranchKey)),
                cancellationToken).ConfigureAwait(false);
            await WorkflowActivationBindingWriter.BindAsync(commandExecutor,instanceId,tenantScopeKey,executionEpochId,stepId,occurredAtUtc,cancellationToken,trace,idGenerator).ConfigureAwait(false);
            await commandExecutor.ExecuteAsync(
                WorkflowSql.InsertExecutionLog,
                WorkflowSqlParameters.Create(
                    ("CorrelationJson", Full.NET.Abstractions.Observability.LogCorrelationContext.Capture().ToJson()),
                    ("Id", idGenerator.NewId()),
                    ("InstanceId", instanceId), ("ExecutionEpochId", executionEpochId),
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
