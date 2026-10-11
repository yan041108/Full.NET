using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Full.NET.AI.Abstractions.Budgets;
using Full.NET.AI.Abstractions.Models;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Abstractions.Time;
using Full.NET.Agents.Definitions;
using Full.NET.Agents.Mcp;
using Full.NET.Agents.Runtime;
using Full.NET.Agents.Tools;
using Full.NET.Agents.Workflows;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Ai.Features.ManageAgentRuns;
using Full.NET.Modules.Ai.Persistence;
using Full.NET.Modules.Ai.Security;
using Full.NET.Modules.Ai.Serialization;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Ai.Runtime;

/// <summary>Worker 领取运行、重验会话与恢复状态后执行静态 Agent 或工作流。</summary>
internal sealed class AiAgentRunCoordinator(
    IServiceScopeFactory scopeFactory,
    AiAgentRunStore store,
    AiAgentWorkerHeartbeatService heartbeat,
    IAgentModelRunner runner,
    IClock clock,
    IIdGenerator ids,
    IOptions<AiAgentRuntimeOptions> runtimeOptions,
    ILogger<AiAgentRunCoordinator> logger)
{
    public async Task<int> ProcessPendingAsync(CancellationToken cancellationToken = default)
    {
        await heartbeat.UpsertAsync(cancellationToken).ConfigureAwait(false);
        var now = clock.UtcNow;
        var runIds = await store.ScanQueuedAsync(runtimeOptions.Value.BatchSize, now, cancellationToken).ConfigureAwait(false);
        var processed = 0;
        foreach (var runId in runIds)
        {
            if (await TryProcessRunAsync(runId, cancellationToken).ConfigureAwait(false))
            {
                processed++;
            }
        }

        return processed;
    }

    private async Task<bool> TryProcessRunAsync(Guid runId, CancellationToken callerCancellationToken)
    {
        var cancellationToken = callerCancellationToken;
        var leaseDuration = TimeSpan.FromSeconds(runtimeOptions.Value.LeaseSeconds);
        var lease = await store.TryAcquireAsync(runId, heartbeat.WorkerId, clock.UtcNow, leaseDuration, cancellationToken)
            .ConfigureAwait(false);
        callerCancellationToken.ThrowIfCancellationRequested();
        if (lease is null)
        {
            return false;
        }

        var record = await store.FindByIdAsync(runId, cancellationToken).ConfigureAwait(false);
        callerCancellationToken.ThrowIfCancellationRequested();
        if (record is null || record.StatusKey is "cancelled" or "expired")
        {
            return false;
        }

        await using var leaseWatch = new AiAgentRunLeaseWatch(scopeFactory, clock, lease,
            record.DeadlineAtUtc, leaseDuration, logger, callerCancellationToken);
        cancellationToken = leaseWatch.Token;
        if (!leaseWatch.IsActive) return false;

        await using var scope = scopeFactory.CreateAsyncScope();

        // 所有状态写入之前先收敛续租，确保提交使用最后成功续租的行版本。
        async Task<bool> PrepareCommitAsync()
        {
            lease = await leaseWatch.StopAsync().ConfigureAwait(false);
            callerCancellationToken.ThrowIfCancellationRequested();
            return leaseWatch.IsActive;
        }

        var preserveDispatchBudget = false;
        var completionPending = false;
        async Task<bool> FailAsync(string code)
        {
            if (!await PrepareCommitAsync().ConfigureAwait(false)) return false;
            // 恢复前置拒绝不能覆盖已发生派发的计量；损坏凭证也不能被当成未执行。
            if (preserveDispatchBudget)
                return await store.CommitProgressAsync(new(lease, "reconciliation_required", null, null,
                    new(ids.NewId(), await NextEventSequenceAsync(store, runId, cancellationToken).ConfigureAwait(false),
                        "run.reconciliation_required", 1, $$"""{"errorCode":"{{code}}"}""")), cancellationToken).ConfigureAwait(false);
            return await RejectAsync(async () => new(lease, "failed", null, null,
                new(ids.NewId(), await NextEventSequenceAsync(store, runId, cancellationToken).ConfigureAwait(false),
                    "run.failed", 1, $$"""{"errorCode":"{{code}}"}"""))).ConfigureAwait(false);
        }
        // 此边界也供 catch 内的明确未派发拒绝使用；收尾失败不能逃逸后再次改判或结算。
        async Task<bool> RejectAsync(Func<Task<AgentRunProgressCommit>> prepare)
        {
            try
            {
                var progress = await prepare().ConfigureAwait(false);
                return await scope.ServiceProvider.GetRequiredService<AiAgentRunCompletion>()
                    .RejectAsync(progress, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (callerCancellationToken.IsCancellationRequested) { throw; }
            catch (Exception exception)
            {
                logger.LogError(exception, "Agent run {RunId} rejection transaction did not succeed.", runId);
                return false;
            }
        }
        var tenantWriter = scope.ServiceProvider.GetRequiredService<ICurrentTenantContextWriter>();
        if (record.TenantId is { } tenantId)
        {
            tenantWriter.SetTenant(new TenantContext(tenantId, record.ScopeKey, record.ScopeKey));
        }
        else
        {
            tenantWriter.SetHost();
        }

        var executionContext = scope.ServiceProvider.GetRequiredService<IAgentRunExecutionContext>();
        AgentWorkflowState? workflowState = null;
        AiModelRunCheckpoint? modelState = null;
        try
        {
            // 读取失败属于状态未知，必须在等待前保留预算；只有成功读到首次空状态才解除保护。
            preserveDispatchBudget = record.DefinitionKey is AgentDefinitionRegistry.SingleTextKey or AgentDefinitionRegistry.ReadOnlyToolLoopKey;
            var modelCheckpoint = preserveDispatchBudget
                ? await store.FindLatestCheckpointAsync(runId, cancellationToken).ConfigureAwait(false) : null;
            cancellationToken.ThrowIfCancellationRequested();
            preserveDispatchBudget = (record.DefinitionKey is AgentDefinitionRegistry.SingleTextKey or AgentDefinitionRegistry.ReadOnlyToolLoopKey)
                && (modelCheckpoint is not null || lease.Epoch != 1);
            var bindingValidator = scope.ServiceProvider.GetRequiredService<IBackgroundSessionBindingValidator>();
            var binding = new SessionBindingSnapshot(
                record.ActorUserId,
                record.TenantId,
                record.SessionId,
                record.SecurityStamp,
                record.ActorScope,
                record.EffectiveScope,
                string.IsNullOrWhiteSpace(record.SessionKind)
                    ? SessionBindingKinds.Refresh
                    : record.SessionKind);
            if (!await bindingValidator.IsValidAsync(binding, cancellationToken).ConfigureAwait(false))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!await PrepareCommitAsync().ConfigureAwait(false)) return false;
                async Task<AgentRunProgressCommit> PrepareAuthorizationAsync() => new(
                    lease, "authorization_required", null, null,
                    new(ids.NewId(), await NextEventSequenceAsync(store, runId, cancellationToken).ConfigureAwait(false),
                        "run.authorization_required", 1, """{"reason":"session_invalid"}"""));
                // 会话失效阻止结果交付；已派发凭证要求保留原预算时只记录状态，不结算未知结果。
                if (preserveDispatchBudget)
                    return await store.CommitProgressAsync(await PrepareAuthorizationAsync().ConfigureAwait(false), cancellationToken).ConfigureAwait(false);
                return await RejectAsync(PrepareAuthorizationAsync).ConfigureAwait(false);
            }

            executionContext.SetBinding(binding);

            var isWorkflow = record.DefinitionKey == AgentWorkflowRegistry.ChatRenameWorkflowKey;
            if (isWorkflow
                ? AgentWorkflowRegistry.Resolve(record.DefinitionKey, record.DefinitionVersion) is null
                : AgentDefinitionRegistry.Resolve(record.DefinitionKey, record.DefinitionVersion) is null)
            {
                return await FailAsync("ai.agent_run.definition_incompatible").ConfigureAwait(false);
            }
            if (isWorkflow)
            {
                workflowState = await LoadWorkflowStateAsync(store, record, cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                if (workflowState is null)
                {
                    return await FailAsync("ai.agent_run.checkpoint_incompatible").ConfigureAwait(false);
                }
                if (workflowState.PendingNode is not null)
                {
                    // 派发意图存在但没有完成凭证，创建客户端前停止，不能重发结果不明的模型或工具调用。
                    if (!await PrepareCommitAsync().ConfigureAwait(false)) return false;
                    return await CommitWorkflowProgressAsync(store, lease, runId, record, workflowState,
                        "reconciliation_required", """{"reason":"node_outcome_unknown"}""", null, cancellationToken).ConfigureAwait(false);
                }
            }
            // 查询提供者可能忽略令牌；恢复校验结束后仍须阻止取消请求创建外部客户端。
            cancellationToken.ThrowIfCancellationRequested();

            var snapshot = JsonSerializer.Deserialize(record.BudgetJson, AiJsonSerializerContext.Default.AgentRunBudgetSnapshot);
            if (snapshot is null)
            {
                return await FailAsync("ai.agent_run.invalid_payload").ConfigureAwait(false);
            }
            if (record.DefinitionKey is AgentDefinitionRegistry.SingleTextKey or AgentDefinitionRegistry.ReadOnlyToolLoopKey)
            {
                var checkpoint = modelCheckpoint;
                if (checkpoint is not null)
                {
                    modelState = AiModelRunCheckpoint.Restore(record, snapshot, checkpoint);
                    if (modelState is null || modelState.Phase == "not_dispatched")
                        return await FailAsync("ai.agent_run.checkpoint_incompatible").ConfigureAwait(false);
                    if (modelState.Phase == "started")
                    {
                        if (!await PrepareCommitAsync().ConfigureAwait(false)) return false;
                        return await CommitModelProgressAsync(lease, record, modelState, "reconciliation_required",
                            "run.reconciliation_required", null, cancellationToken).ConfigureAwait(false);
                    }
                }
                else if (lease.Epoch != 1)
                {
                    // 已领取过但没有新凭证，不能以缺失检查点推断旧执行者未调用模型。
                    return await FailAsync("ai.agent_run.checkpoint_incompatible").ConfigureAwait(false);
                }
            }
            if (!AiRunOutputBudgetAdmission.TryGetConsumedTokens(snapshot, workflowState, out var consumedInputTokens, out var consumedOutputTokens))
                return await FailAsync("ai.budget.limit_exceeded").ConfigureAwait(false);
            if (modelState?.LoopState is { } savedLoop)
            {
                var info = AgentToolLoopCheckpoint.Inspect(savedLoop, runId, record.DefinitionKey, record.DefinitionVersion,
                    modelState.InputDigest, callId => ResolveToolOperationId(runId, callId));
                consumedInputTokens = info.InputTokens; consumedOutputTokens = info.OutputTokens;
                // 最终回执允许保留未知用量；有剩余步骤时，未知或耗尽不能重置为新客户端的零用量。
                if (info.Phase != "completed" && (consumedInputTokens is null || consumedOutputTokens is null
                    || consumedInputTokens >= snapshot.InputTokenLimit || consumedOutputTokens >= snapshot.OutputTokenLimit))
                    return await FailAsync("ai.budget.limit_exceeded").ConfigureAwait(false);
            }
            // 未知或耗尽的历史步数在客户端创建前拒绝；未知派发意图已优先转入对账。
            if (workflowState is not null && !AgentWorkflowRunner.HasExecutionBudget(workflowState,
                    AgentWorkflowRegistry.Resolve(record.DefinitionKey, record.DefinitionVersion)!.Nodes.Count))
                return await FailAsync("ai.agent_run.tool_budget_exceeded").ConfigureAwait(false);

            var queries = scope.ServiceProvider.GetRequiredService<IQueryExecutor>();
            var model = await queries.QuerySingleOrDefaultAsync<AiModelConfigRecord>(
                record.TenantId.HasValue ? AiModelConfigSql.FindAvailableForTenantChat : AiModelConfigSql.FindAvailableForHostChat,
                AiSqlParameters.Create(("ModelConfigId", snapshot.ModelConfigId)),
                cancellationToken).ConfigureAwait(false);
            if (model is null || !model.IsEnabled)
            {
                return await FailAsync(AiErrorCodes.ModelConfigUnavailable).ConfigureAwait(false);
            }
            if (workflowState?.Binding is { } savedBinding
                && (savedBinding.ModelConfigId != model.Id || savedBinding.ModelConfigVersion != model.Version))
                return await FailAsync("ai.agent_run.checkpoint_incompatible").ConfigureAwait(false);
            if (modelState is not null && (modelState.ModelConfigId != model.Id || modelState.ModelConfigVersion != model.Version))
                return await FailAsync("ai.agent_run.checkpoint_incompatible").ConfigureAwait(false);

            using var credentialScope = scope.ServiceProvider.GetRequiredService<AiModelBindingScope>();
            var effectiveSinglePrompt = snapshot.Prompt;
            if (snapshot.PromptBinding is not null)
            {
                var eligibility = await scope.ServiceProvider.GetRequiredService<AiPromptRunResolver>()
                    .ExecutionAsync(snapshot, record.DefinitionKey, binding, model, cancellationToken).ConfigureAwait(false);
                if (!eligibility.IsSuccess)
                {
                    return await FailAsync(eligibility.Error!.Code).ConfigureAwait(false);
                }
                effectiveSinglePrompt = eligibility.Value!;
            }
            cancellationToken.ThrowIfCancellationRequested();
            if (modelState is not null)
            {
                if (modelState.InputDigest != AiModelRunCheckpoint.Digest(effectiveSinglePrompt))
                    return await FailAsync("ai.agent_run.checkpoint_incompatible").ConfigureAwait(false);
                if (modelState.LoopState is { } loopPayload)
                {
                    var completed = AgentToolLoopCheckpoint.ReadCompletedResult(loopPayload, runId, record.DefinitionKey,
                        record.DefinitionVersion, effectiveSinglePrompt, callId => ResolveToolOperationId(runId, callId));
                    if (completed is not null)
                        modelState = modelState.Complete(new(completed.Text, completed.Session, completed.InputTokens, completed.OutputTokens));
                }
                if (modelState.Phase == "completed")
                {
                    // 最终模型或旧整轮完成回执只收敛状态和原用量，不创建客户端或补零未知计量。
                    if (!await PrepareCommitAsync().ConfigureAwait(false)) return false;
                    completionPending = true;
                    return await CompleteModelRunAsync(scope.ServiceProvider, lease, record, modelState, cancellationToken).ConfigureAwait(false);
                }
            }
            var factories = scope.ServiceProvider.GetServices<IAiModelClientFactory>();
            var factory = factories.FirstOrDefault(item => item.ProviderKey == model.ProviderKey)
                ?? throw new InvalidOperationException("AI model provider is not registered.");
            var bindingModel = credentialScope.Create(model);
            using var client = await factory.CreateChatClientAsync(bindingModel, cancellationToken).ConfigureAwait(false);
            // 客户端工厂可能忽略令牌；取消后仍须释放已创建资源并阻止任何节点派发。
            cancellationToken.ThrowIfCancellationRequested();
            // 三类运行共用累计输入回执／输出额度；恢复先扣减已确认用量，未知历史不补零。
            var executionClient = new AiRunOutputLimitClient(client, snapshot.OutputTokenLimit, consumedOutputTokens,
                snapshot.InputTokenLimit, consumedInputTokens);
            AgentModelResult result;
            if (isWorkflow)
            {
                workflowState!.Binding ??= new(runId, record.DefinitionKey, record.DefinitionVersion,
                    AgentWorkflowNodeReceipt.DigestDefinition(AgentWorkflowRegistry.Resolve(record.DefinitionKey, record.DefinitionVersion)!),
                    model.Id, model.Version, workflowState.NextNodeIndex);
                var toolRegistry = await scope.ServiceProvider.GetRequiredService<IAgentToolRegistrySource>()
                    .GetRegistryAsync(cancellationToken).ConfigureAwait(false);
                var workflowRunner = new AgentWorkflowRunner();
                var workflowResult = await workflowRunner.RunAsync(new(
                    runId,
                    record.DefinitionKey,
                    record.DefinitionVersion,
                    workflowState!,
                    executionClient,
                    runner,
                    scope.ServiceProvider.GetRequiredService<IAgentToolExecutor>(),
                    toolRegistry,
                    nodeKey => AgentWorkflowNodeReceipt.ResolveOperationId(runId, nodeKey))
                {
                    PersistNodeAsync = (receipt, state, _) => leaseWatch.CommitNodeAsync((currentLease, token) =>
                        CommitWorkflowProgressAsync(store, currentLease, runId, record, state, "running",
                            receipt.OutputDigest is null ? """{"phase":"node_started"}""" : """{"phase":"node_completed"}""",
                            receipt.OutputDigest is null ? null : new(ids.NewId(), receipt.NodeKey, 1, receipt.OperationId, "committed",
                                receipt.ToolVersion is null ? model.Version : null, receipt.ToolVersion, null,
                                receipt.InputTokens, receipt.OutputTokens, null, null, null, receipt.InputDigest, null), token,
                            receipt.OutputDigest is null ? "run.node_started" : "run.node_completed")),
                }, cancellationToken).ConfigureAwait(false);
                if (!await PrepareCommitAsync().ConfigureAwait(false)) return false;
                if (workflowResult.Status == AgentWorkflowRunStatus.AwaitingApproval)
                {
                    var waiting = await CommitWorkflowProgressAsync(
                        store, lease, runId, record, workflowResult.State, "awaiting_approval",
                        """{"reason":"tool_approval_required"}""", null, cancellationToken).ConfigureAwait(false);
                    if (!waiting)
                    {
                        logger.LogWarning("Agent run {RunId} awaiting_approval commit lost lease fencing.", runId);
                        return false;
                    }

                    return true;
                }

                if (workflowResult.Status == AgentWorkflowRunStatus.ReconciliationRequired)
                {
                    var reconciling = await CommitWorkflowProgressAsync(
                        store, lease, runId, record, workflowResult.State, "reconciliation_required",
                        """{"reason":"tool_receipt_unknown"}""", null, cancellationToken).ConfigureAwait(false);
                    if (!reconciling)
                    {
                        logger.LogWarning("Agent run {RunId} reconciliation_required commit lost lease fencing.", runId);
                        return false;
                    }

                    return true;
                }

                if (workflowResult.Status == AgentWorkflowRunStatus.Failed)
                {
                    return await FailAsync(workflowResult.ErrorCode ?? "ai.agent_run.workflow_failed").ConfigureAwait(false);
                }

                var workflowSession = JsonSerializer.SerializeToElement(
                    new AiWorkflowSessionSnapshot(
                        record.DefinitionKey,
                        workflowResult.State.Outputs,
                        workflowResult.FinalText),
                    AiJsonSerializerContext.Default.AiWorkflowSessionSnapshot);
                result = new(workflowResult.FinalText ?? string.Empty, workflowSession, workflowResult.State.InputTokens, workflowResult.State.OutputTokens);
                var workflowStepId = ids.NewId();
                completionPending = true;
                var completion = await CreateWorkflowProgressAsync(
                    store,
                    lease,
                    runId,
                    record,
                    workflowResult.State,
                    "completed",
                    """{"status":"completed"}""",
                    new(workflowStepId, "workflow.complete", 1, runId, "committed", model.Version, null, null,
                        result.InputTokens, result.OutputTokens, null, null, null, null, null),
                    cancellationToken).ConfigureAwait(false);
                var workflowCommitted = await scope.ServiceProvider.GetRequiredService<AiAgentRunCompletion>()
                    .CommitAsync(completion, new(result.InputTokens, result.OutputTokens), cancellationToken).ConfigureAwait(false);
                if (!workflowCommitted)
                {
                    logger.LogWarning("Agent run {RunId} workflow progress commit lost lease fencing.", runId);
                    return false;
                }

                return true;
            }

            if (record.DefinitionKey == AgentDefinitionRegistry.ReadOnlyToolLoopKey)
            {
                var remoteCatalog = scope.ServiceProvider.GetRequiredService<IMcpRemoteToolCatalog>();
                var remoteTools = await remoteCatalog.ListExecutableAsync(cancellationToken).ConfigureAwait(false);
                var additionalAllowed = remoteTools.Select(tool => tool.LocalToolName).ToHashSet(StringComparer.Ordinal);
                var toolRegistry = await scope.ServiceProvider.GetRequiredService<IAgentToolRegistrySource>()
                    .GetRegistryAsync(cancellationToken).ConfigureAwait(false);
                // 整轮意图先落库；恢复不重放未知的模型或工具，也不把单次本地拒绝当作整轮未派发。
                preserveDispatchBudget = true;
                if (modelState is null)
                {
                    modelState = AiModelRunCheckpoint.Start(record, model, effectiveSinglePrompt);
                    if (!await leaseWatch.CommitNodeAsync((currentLease, token) => CommitModelProgressAsync(
                        currentLease, record, modelState, "running", "run.node_started", null, token)).ConfigureAwait(false)) return false;
                }
                cancellationToken.ThrowIfCancellationRequested();
                var loop = new AgentToolLoop();
                var loopResult = await loop.RunAsync(new(
                    runId,
                    record.DefinitionKey,
                    record.DefinitionVersion,
                    effectiveSinglePrompt,
                    executionClient,
                    scope.ServiceProvider.GetRequiredService<IAgentToolExecutor>(),
                    toolRegistry,
                    callId => ResolveToolOperationId(runId, callId),
                    additionalAllowed,
                    modelState.LoopState,
                    async (payload, token) =>
                    {
                        var candidate = modelState.WithLoop(payload);
                        var committed = await leaseWatch.CommitNodeAsync((currentLease, commitToken) => CommitModelProgressAsync(
                            currentLease, record, candidate, "running", candidate.Phase == "started" ? "run.node_started" : "run.node_completed",
                            null, commitToken)).ConfigureAwait(false);
                        token.ThrowIfCancellationRequested();
                        if (committed) modelState = candidate;
                        return committed;
                    }), cancellationToken).ConfigureAwait(false);
                if (loopResult.ApprovalRequired)
                {
                    if (!await PrepareCommitAsync().ConfigureAwait(false)) return false;
                    var waiting = await store.CommitProgressAsync(new(
                        lease,
                        "awaiting_approval",
                        null,
                        null,
                        new(ids.NewId(), await NextEventSequenceAsync(store, runId, cancellationToken).ConfigureAwait(false), "run.awaiting_approval", 1, """{"reason":"tool_approval_required"}""")),
                        cancellationToken).ConfigureAwait(false);
                    if (!waiting)
                    {
                        logger.LogWarning("Agent run {RunId} awaiting_approval commit lost lease fencing.", runId);
                        return false;
                    }

                    return true;
                }

                if (loopResult.ReconciliationRequired)
                {
                    if (!await PrepareCommitAsync().ConfigureAwait(false)) return false;
                    var reconciling = await store.CommitProgressAsync(new(
                        lease,
                        "reconciliation_required",
                        null,
                        null,
                        new(ids.NewId(), await NextEventSequenceAsync(store, runId, cancellationToken).ConfigureAwait(false), "run.reconciliation_required", 1, """{"reason":"tool_receipt_unknown"}""")),
                        cancellationToken).ConfigureAwait(false);
                    if (!reconciling)
                    {
                        logger.LogWarning("Agent run {RunId} reconciliation_required commit lost lease fencing.", runId);
                        return false;
                    }

                    return true;
                }

                result = new(loopResult.Text, loopResult.Session, loopResult.InputTokens, loopResult.OutputTokens);
                if (!AiModelRunCheckpoint.ValidResult(result, snapshot))
                    throw new InvalidOperationException("Tool loop result is not a valid completion receipt.");
                var completedState = modelState.Complete(result);
                var completedStep = new AgentStepUpsert(ids.NewId(), "model.text", 1, runId, "committed", model.Version,
                    null, null, result.InputTokens, result.OutputTokens, null, null, null, modelState.InputDigest, null);
                if (!await leaseWatch.CommitNodeAsync((currentLease, token) => CommitModelProgressAsync(
                    currentLease, record, completedState, "running", "run.node_completed", completedStep, token)).ConfigureAwait(false)) return false;
                modelState = completedState;
                if (!await PrepareCommitAsync().ConfigureAwait(false)) return false;
                completionPending = true;
                return await CompleteModelRunAsync(scope.ServiceProvider, lease, record, modelState, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                var effectivePrompt = snapshot.Prompt;
                if (snapshot.PromptBinding is not null)
                {
                    // 建立客户端后再次复核，关闭停用、模型修订或权限撤销在创建期间发生的窗口。
                    var proven = await scope.ServiceProvider.GetRequiredService<AiPromptRunResolver>()
                        .ExecutionAsync(snapshot, record.DefinitionKey, binding, model, cancellationToken).ConfigureAwait(false);
                    if (!proven.IsSuccess)
                    {
                        return await FailAsync(proven.Error!.Code).ConfigureAwait(false);
                    }
                    effectivePrompt = proven.Value!;
                }
                modelState = AiModelRunCheckpoint.Start(record, model, effectivePrompt);
                preserveDispatchBudget = true;
                if (!await leaseWatch.CommitNodeAsync((currentLease, token) => CommitModelProgressAsync(
                    currentLease, record, modelState, "running", "run.node_started", null, token)).ConfigureAwait(false)) return false;
                cancellationToken.ThrowIfCancellationRequested();
                result = await runner.RunAsync(executionClient, effectivePrompt, cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                if (!AiModelRunCheckpoint.ValidResult(result, snapshot))
                    throw new InvalidOperationException("Single model result is not a valid completion receipt.");
                var completedState = modelState.Complete(result);
                var completedStep = new AgentStepUpsert(ids.NewId(), "model.text", 1, runId, "committed", model.Version,
                    null, null, result.InputTokens, result.OutputTokens, null, null, null, modelState.InputDigest, null);
                if (!await leaseWatch.CommitNodeAsync((currentLease, token) => CommitModelProgressAsync(
                    currentLease, record, completedState, "running", "run.node_completed", completedStep, token)).ConfigureAwait(false)) return false;
                modelState = completedState;
                if (!await PrepareCommitAsync().ConfigureAwait(false)) return false;
                completionPending = true;
                return await CompleteModelRunAsync(scope.ServiceProvider, lease, record, modelState, cancellationToken).ConfigureAwait(false);
            }


        }
        catch (OperationCanceledException) when (callerCancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // 失租或到期不是当前执行者可提交的失败；预算仍由持久化状态与后续对账收敛。
            return false;
        }
        catch (Exception exception) when (completionPending)
        {
            // 已取得结果后的收尾失败不能改判模型失败；保留原凭证和预算，后续按恢复规则收敛。
            logger.LogError(exception, "Agent run {RunId} completion transaction did not succeed.", runId);
            return false;
        }
        catch (AgentToolLoopBudgetExceededException)
        {
            return await FailAsync("ai.agent_run.tool_budget_exceeded").ConfigureAwait(false);
        }
        catch (AiRunOutputBudgetException exception) when (!exception.DispatchStarted)
        {
            if (record.DefinitionKey == AgentDefinitionRegistry.SingleTextKey && modelState?.Phase == "started")
            {
                // 预算客户端明确证明尚未派发，保存拒绝事实后才允许结算失败。
                modelState = modelState with { Phase = "not_dispatched" };
                if (!await PrepareCommitAsync().ConfigureAwait(false)) return false;
                return await RejectAsync(() => CreateModelProgressAsync(lease, record, modelState,
                    "failed", "run.failed", null, cancellationToken)).ConfigureAwait(false);
            }
            if (workflowState?.PendingNode is not null)
            {
                // 本地额度拒绝能证明本次模型尚未派发；清除已保存意图后失败，不要求外部对账。
                workflowState.PendingNode = null;
                if (!await PrepareCommitAsync().ConfigureAwait(false)) return false;
                return await RejectAsync(() => CreateWorkflowProgressAsync(store, lease, runId, record, workflowState,
                    "failed", $$"""{"errorCode":"{{exception.Code}}"}""", null, cancellationToken)).ConfigureAwait(false);
            }
            return await FailAsync(exception.Code).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Agent run {RunId} failed.", runId);
            if (modelState is not null)
            {
                // 完成提交或结算异常不得把已确认结果改成失败；派发后未知异常保留预算待对账。
                if (modelState.Phase != "started" || !await PrepareCommitAsync().ConfigureAwait(false)) return false;
                return await CommitModelProgressAsync(lease, record, modelState, "reconciliation_required",
                    "run.reconciliation_required", null, cancellationToken).ConfigureAwait(false);
            }
            if (workflowState?.PendingNode is not null)
            {
                // 已持久化意图后的网络异常与提供者独立超时无法证明未执行，预算预留保留待对账。
                if (!await PrepareCommitAsync().ConfigureAwait(false)) return false;
                return await CommitWorkflowProgressAsync(store, lease, runId, record, workflowState,
                    "reconciliation_required", """{"reason":"node_outcome_unknown"}""", null, cancellationToken).ConfigureAwait(false);
            }
            return await FailAsync("ai.agent_run.failed").ConfigureAwait(false);
        }
        finally
        {
            executionContext.Clear();
            tenantWriter.Clear();
        }
    }

    private static Guid ResolveToolOperationId(Guid runId, string callId)
    {
        return AiModelRunCheckpoint.ResolveToolOperationId(runId, callId);
    }

    private async Task<bool> CompleteModelRunAsync(IServiceProvider provider, AgentRunLease lease,
        AiAgentRunRecord run, AiModelRunCheckpoint state, CancellationToken token)
    {
        var progress = await CreateModelProgressAsync(lease, run, state, "completed", "run.completed", null, token).ConfigureAwait(false);
        return await provider.GetRequiredService<AiAgentRunCompletion>().CommitAsync(progress,
            new(state.Result!.InputTokens, state.Result.OutputTokens), token).ConfigureAwait(false);
    }

    /// <summary>检查点、步骤和事件共享租约事务；正文仅保存在检查点，不进入事件或日志。</summary>
    private async Task<bool> CommitModelProgressAsync(AgentRunLease lease, AiAgentRunRecord run,
        AiModelRunCheckpoint state, string status, string eventType, AgentStepUpsert? step, CancellationToken token) =>
        await store.CommitProgressAsync(await CreateModelProgressAsync(lease, run, state, status, eventType, step, token)
            .ConfigureAwait(false), token).ConfigureAwait(false);

    /// <summary>事务外准备不可变提交参数；最终成功提交由同作用域结算服务持有事务。</summary>
    private async Task<AgentRunProgressCommit> CreateModelProgressAsync(AgentRunLease lease, AiAgentRunRecord run,
        AiModelRunCheckpoint state, string status, string eventType, AgentStepUpsert? step, CancellationToken token)
    {
        var payload = state.ToJson();
        var latest = await store.FindLatestCheckpointAsync(run.Id, token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        var sequence = Math.Max(checked((latest?.Sequence ?? 0) + 1), await NextEventSequenceAsync(store, run.Id, token).ConfigureAwait(false));
        return new(lease, status, step,
            new(ids.NewId(), sequence, AgentCheckpointCompatibility.CurrentCheckpointFormatVersion,
                AgentFrameworkRuntime.FrameworkVersion, run.DefinitionVersion, payload, AiModelRunCheckpoint.Digest(payload)),
            new(ids.NewId(), sequence, eventType, 1, $$"""{"phase":"{{state.Phase}}"}"""));
    }

    private static async Task<AgentWorkflowState?> LoadWorkflowStateAsync(
        AiAgentRunStore store,
        AiAgentRunRecord record,
        CancellationToken cancellationToken)
    {
        var checkpoint = await store.FindLatestCheckpointAsync(record.Id, cancellationToken).ConfigureAwait(false);
        return AiWorkflowCheckpointAdmission.Restore(record, checkpoint);
    }

    private async Task<bool> CommitWorkflowProgressAsync(
        AiAgentRunStore store,
        AgentRunLease lease,
        Guid runId,
        AiAgentRunRecord record,
        AgentWorkflowState state,
        string statusKey,
        string eventPayload,
        AgentStepUpsert? step,
        CancellationToken cancellationToken,
        string? eventType = null)
    => await store.CommitProgressAsync(await CreateWorkflowProgressAsync(store, lease, runId, record, state,
            statusKey, eventPayload, step, cancellationToken, eventType).ConfigureAwait(false), cancellationToken).ConfigureAwait(false);

    /// <summary>完成与中间节点共用凭证构造，不在准备阶段提前持久化终态。</summary>
    private async Task<AgentRunProgressCommit> CreateWorkflowProgressAsync(
        AiAgentRunStore store,
        AgentRunLease lease,
        Guid runId,
        AiAgentRunRecord record,
        AgentWorkflowState state,
        string statusKey,
        string eventPayload,
        AgentStepUpsert? step,
        CancellationToken cancellationToken,
        string? eventType = null)
    {
        var payload = state.ToJson();
        var checksum = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
        var latest = await store.FindLatestCheckpointAsync(runId, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        var sequence = Math.Max(checked((latest?.Sequence ?? 0) + 1),
            await NextEventSequenceAsync(store, runId, cancellationToken).ConfigureAwait(false));
        var checkpointId = ids.NewId();
        return new(
            lease,
            statusKey,
            step,
            new(checkpointId, sequence, AgentCheckpointCompatibility.CurrentCheckpointFormatVersion,
                AgentFrameworkRuntime.FrameworkVersion, record.DefinitionVersion, payload, checksum),
            new(ids.NewId(), sequence, eventType ?? $"run.{statusKey}", 1, eventPayload));
    }

    private static async Task<long> NextEventSequenceAsync(AiAgentRunStore store, Guid runId, CancellationToken token)
    {
        var sequence = await store.FindLatestEventSequenceAsync(runId, token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        if (sequence < 0) throw new InvalidOperationException("Agent event sequence must be nonnegative.");
        return checked(sequence + 1);
    }
}
