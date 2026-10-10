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

        async Task<bool> FailAsync(string code) => await PrepareCommitAsync().ConfigureAwait(false)
            && await FailRunAsync(scope.ServiceProvider, store, lease, runId, code, cancellationToken).ConfigureAwait(false);
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
        try
        {
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
                var authorizationSequence = await NextEventSequenceAsync(store, runId, cancellationToken).ConfigureAwait(false);
                var authorizationCommitted = await store.CommitProgressAsync(new(
                    lease,
                    "authorization_required",
                    null,
                    null,
                    new(ids.NewId(), authorizationSequence, "run.authorization_required", 1, """{"reason":"session_invalid"}""")), cancellationToken)
                    .ConfigureAwait(false);
                // 失租后的会话拒绝不能改写由当前执行者负责的预算账本。
                if (!authorizationCommitted) return false;
                await scope.ServiceProvider.GetRequiredService<IAiOperationBudgetStore>()
                    .SettleAsync(runId, new(null, null), "cancelled", cancellationToken).ConfigureAwait(false);
                return true;
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

            using var credentialScope = scope.ServiceProvider.GetRequiredService<AiModelBindingScope>();
            if (snapshot.PromptBinding is not null)
            {
                var eligibility = await scope.ServiceProvider.GetRequiredService<AiPromptRunResolver>()
                    .ExecutionAsync(snapshot, record.DefinitionKey, binding, model, cancellationToken).ConfigureAwait(false);
                if (!eligibility.IsSuccess)
                {
                    return await FailAsync(eligibility.Error!.Code).ConfigureAwait(false);
                }
            }
            cancellationToken.ThrowIfCancellationRequested();
            var factories = scope.ServiceProvider.GetServices<IAiModelClientFactory>();
            var factory = factories.FirstOrDefault(item => item.ProviderKey == model.ProviderKey)
                ?? throw new InvalidOperationException("AI model provider is not registered.");
            var bindingModel = credentialScope.Create(model);
            using var client = await factory.CreateChatClientAsync(bindingModel, cancellationToken).ConfigureAwait(false);
            // 客户端工厂可能忽略令牌；取消后仍须释放已创建资源并阻止任何节点派发。
            cancellationToken.ThrowIfCancellationRequested();
            // 三类运行共用冻结输出上限，工作流与工具循环不能绕过单次文本已有的派发保护。
            var executionClient = new AiRunOutputLimitClient(client, snapshot.OutputTokenLimit);
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
                var workflowCommitted = await CommitWorkflowProgressAsync(
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
                if (!workflowCommitted)
                {
                    logger.LogWarning("Agent run {RunId} workflow progress commit lost lease fencing.", runId);
                    return false;
                }

                await scope.ServiceProvider.GetRequiredService<IAiOperationBudgetStore>().SettleAsync(
                    runId,
                    new(result.InputTokens, result.OutputTokens),
                    "succeeded",
                    cancellationToken).ConfigureAwait(false);
                return true;
            }

            if (record.DefinitionKey == AgentDefinitionRegistry.ReadOnlyToolLoopKey)
            {
                var remoteCatalog = scope.ServiceProvider.GetRequiredService<IMcpRemoteToolCatalog>();
                var remoteTools = await remoteCatalog.ListExecutableAsync(cancellationToken).ConfigureAwait(false);
                var additionalAllowed = remoteTools.Select(tool => tool.LocalToolName).ToHashSet(StringComparer.Ordinal);
                var toolRegistry = await scope.ServiceProvider.GetRequiredService<IAgentToolRegistrySource>()
                    .GetRegistryAsync(cancellationToken).ConfigureAwait(false);
                var loop = new AgentToolLoop();
                var loopResult = await loop.RunAsync(new(
                    runId,
                    record.DefinitionKey,
                    record.DefinitionVersion,
                    snapshot.Prompt,
                    executionClient,
                    scope.ServiceProvider.GetRequiredService<IAgentToolExecutor>(),
                    toolRegistry,
                    callId => ResolveToolOperationId(runId, callId),
                    additionalAllowed), cancellationToken).ConfigureAwait(false);
                if (!await PrepareCommitAsync().ConfigureAwait(false)) return false;
                if (loopResult.ApprovalRequired)
                {
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
                result = await runner.RunAsync(executionClient, effectivePrompt, cancellationToken).ConfigureAwait(false);
            }

            // 单次模型调用也可能在取消后返回成功，不能把晚到结果提交或结算。
            cancellationToken.ThrowIfCancellationRequested();
            var checksum = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(result.Session.GetRawText())));
            if (!await PrepareCommitAsync().ConfigureAwait(false)) return false;
            var stepId = ids.NewId();
            var checkpointId = ids.NewId();
            var completionSequence = await NextEventSequenceAsync(store, runId, cancellationToken).ConfigureAwait(false);
            var committed = await store.CommitProgressAsync(new(
                lease,
                "completed",
                new(stepId, "model.text", 1, runId, "committed", model.Version, null, null,
                    result.InputTokens, result.OutputTokens, null, null, null, null, null),
                new(checkpointId, completionSequence, 1, AgentFrameworkRuntime.FrameworkVersion, record.DefinitionVersion,
                    result.Session.GetRawText(), checksum),
                new(ids.NewId(), completionSequence, "run.completed", 1, """{"status":"completed"}""")), cancellationToken).ConfigureAwait(false);
            if (!committed)
            {
                logger.LogWarning("Agent run {RunId} progress commit lost lease fencing.", runId);
                return false;
            }

            await scope.ServiceProvider.GetRequiredService<IAiOperationBudgetStore>().SettleAsync(
                runId,
                new(result.InputTokens, result.OutputTokens),
                "succeeded",
                cancellationToken).ConfigureAwait(false);
            return true;
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
        catch (AgentToolLoopBudgetExceededException)
        {
            return await FailAsync("ai.agent_run.tool_budget_exceeded").ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Agent run {RunId} failed.", runId);
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
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(FormattableString.Invariant($"{runId:N}|tool|{callId}")));
        return new Guid(bytes.AsSpan(0, 16), bigEndian: true);
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
    {
        var payload = state.ToJson();
        var checksum = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
        var latest = await store.FindLatestCheckpointAsync(runId, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        var sequence = Math.Max(checked((latest?.Sequence ?? 0) + 1),
            await NextEventSequenceAsync(store, runId, cancellationToken).ConfigureAwait(false));
        var checkpointId = ids.NewId();
        return await store.CommitProgressAsync(new(
            lease,
            statusKey,
            step,
            new(checkpointId, sequence, AgentCheckpointCompatibility.CurrentCheckpointFormatVersion,
                AgentFrameworkRuntime.FrameworkVersion, record.DefinitionVersion, payload, checksum),
            new(ids.NewId(), sequence, eventType ?? $"run.{statusKey}", 1, eventPayload)), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>失败状态须先通过租约fencing提交；失租时不结算预算，也不计入处理成功数。</summary>
    private static async Task<bool> FailRunAsync(
        IServiceProvider provider,
        AiAgentRunStore store,
        AgentRunLease lease,
        Guid runId,
        string errorCode,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var sequence = await NextEventSequenceAsync(store, runId, cancellationToken).ConfigureAwait(false);
        var committed = await store.CommitProgressAsync(new(
            lease,
            "failed",
            null,
            null,
            new(provider.GetRequiredService<IIdGenerator>().NewId(), sequence, "run.failed", 1, $$"""{"errorCode":"{{errorCode}}"}""")), cancellationToken).ConfigureAwait(false);
        if (!committed) return false;
        try
        {
            await provider.GetRequiredService<IAiOperationBudgetStore>()
                .SettleAsync(runId, new(null, null), "failed", cancellationToken).ConfigureAwait(false);
        }
        catch (AiBudgetException)
        {
        }
        return true;
    }

    private static async Task<long> NextEventSequenceAsync(AiAgentRunStore store, Guid runId, CancellationToken token)
    {
        var sequence = await store.FindLatestEventSequenceAsync(runId, token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        if (sequence < 0) throw new InvalidOperationException("Agent event sequence must be nonnegative.");
        return checked(sequence + 1);
    }
}
