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

/// <summary>Worker 领取运行、重验会话并执行单文本 Agent；工具循环留给后续切片。</summary>
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

    private async Task<bool> TryProcessRunAsync(Guid runId, CancellationToken cancellationToken)
    {
        var leaseDuration = TimeSpan.FromSeconds(runtimeOptions.Value.LeaseSeconds);
        var lease = await store.TryAcquireAsync(runId, heartbeat.WorkerId, clock.UtcNow, leaseDuration, cancellationToken)
            .ConfigureAwait(false);
        if (lease is null)
        {
            return false;
        }

        var record = await store.FindByIdAsync(runId, cancellationToken).ConfigureAwait(false);
        if (record is null || record.StatusKey is "cancelled" or "expired")
        {
            return false;
        }

        await using var scope = scopeFactory.CreateAsyncScope();
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
        try
        {
            var bindingValidator = scope.ServiceProvider.GetRequiredService<IBackgroundSessionBindingValidator>();
            var binding = new SessionBindingSnapshot(
                record.ActorUserId,
                record.TenantId,
                record.SessionId,
                record.SecurityStamp,
                record.ActorScope,
                record.EffectiveScope);
            if (!await bindingValidator.IsValidAsync(binding, cancellationToken).ConfigureAwait(false))
            {
                await store.CommitProgressAsync(new(
                    lease,
                    "authorization_required",
                    null,
                    null,
                    new(ids.NewId(), 1, "run.authorization_required", 1, """{"reason":"session_invalid"}""")), cancellationToken)
                    .ConfigureAwait(false);
                await scope.ServiceProvider.GetRequiredService<IAiOperationBudgetStore>()
                    .SettleAsync(runId, new(null, null), "cancelled", cancellationToken).ConfigureAwait(false);
                return true;
            }

            executionContext.SetBinding(binding);

            var snapshot = JsonSerializer.Deserialize(record.BudgetJson, AiJsonSerializerContext.Default.AgentRunBudgetSnapshot);
            if (snapshot is null)
            {
                await FailRunAsync(scope.ServiceProvider, store, lease, runId, "ai.agent_run.invalid_payload", cancellationToken).ConfigureAwait(false);
                return true;
            }

            var queries = scope.ServiceProvider.GetRequiredService<IQueryExecutor>();
            var model = await queries.QuerySingleOrDefaultAsync<AiModelConfigRecord>(
                record.TenantId.HasValue ? AiModelConfigSql.FindAvailableForTenantChat : AiModelConfigSql.FindAvailableForHostChat,
                AiSqlParameters.Create(("ModelConfigId", snapshot.ModelConfigId)),
                cancellationToken).ConfigureAwait(false);
            if (model is null || !model.IsEnabled)
            {
                await FailRunAsync(scope.ServiceProvider, store, lease, runId, AiErrorCodes.ModelConfigUnavailable, cancellationToken).ConfigureAwait(false);
                return true;
            }

            using var credentialScope = scope.ServiceProvider.GetRequiredService<AiModelBindingScope>();
            var factories = scope.ServiceProvider.GetServices<IAiModelClientFactory>();
            var factory = factories.FirstOrDefault(item => item.ProviderKey == model.ProviderKey)
                ?? throw new InvalidOperationException("AI model provider is not registered.");
            var bindingModel = credentialScope.Create(model);
            using var client = await factory.CreateChatClientAsync(bindingModel, cancellationToken).ConfigureAwait(false);
            AgentModelResult result;
            if (record.DefinitionKey == AgentWorkflowRegistry.ChatRenameWorkflowKey)
            {
                var workflow = AgentWorkflowRegistry.Resolve(record.DefinitionKey, record.DefinitionVersion)
                    ?? throw new InvalidOperationException("Unknown workflow definition.");
                var workflowState = await LoadWorkflowStateAsync(store, record, cancellationToken).ConfigureAwait(false);
                if (workflowState is null)
                {
                    await FailRunAsync(scope.ServiceProvider, store, lease, runId, "ai.agent_run.checkpoint_incompatible", cancellationToken).ConfigureAwait(false);
                    return true;
                }

                var toolRegistry = await scope.ServiceProvider.GetRequiredService<IAgentToolRegistrySource>()
                    .GetRegistryAsync(cancellationToken).ConfigureAwait(false);
                var workflowRunner = new AgentWorkflowRunner();
                var workflowResult = await workflowRunner.RunAsync(new(
                    runId,
                    record.DefinitionKey,
                    record.DefinitionVersion,
                    workflowState,
                    client,
                    runner,
                    scope.ServiceProvider.GetRequiredService<IAgentToolExecutor>(),
                    toolRegistry,
                    nodeKey => ResolveWorkflowOperationId(runId, nodeKey)), cancellationToken).ConfigureAwait(false);
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
                    await FailRunAsync(
                        scope.ServiceProvider,
                        store,
                        lease,
                        runId,
                        workflowResult.ErrorCode ?? "ai.agent_run.workflow_failed",
                        cancellationToken).ConfigureAwait(false);
                    return true;
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
                    client,
                    scope.ServiceProvider.GetRequiredService<IAgentToolExecutor>(),
                    toolRegistry,
                    callId => ResolveToolOperationId(runId, callId),
                    additionalAllowed), cancellationToken).ConfigureAwait(false);
                if (loopResult.ApprovalRequired)
                {
                    var waiting = await store.CommitProgressAsync(new(
                        lease,
                        "awaiting_approval",
                        null,
                        null,
                        new(ids.NewId(), 1, "run.awaiting_approval", 1, """{"reason":"tool_approval_required"}""")),
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
                        new(ids.NewId(), 1, "run.reconciliation_required", 1, """{"reason":"tool_receipt_unknown"}""")),
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
                result = await runner.RunAsync(client, snapshot.Prompt, cancellationToken).ConfigureAwait(false);
            }

            var checksum = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(result.Session.GetRawText())));
            var stepId = ids.NewId();
            var checkpointId = ids.NewId();
            var committed = await store.CommitProgressAsync(new(
                lease,
                "completed",
                new(stepId, "model.text", 1, runId, "committed", model.Version, null, null,
                    result.InputTokens, result.OutputTokens, null, null, null, null, null),
                new(checkpointId, 1, 1, AgentFrameworkRuntime.FrameworkVersion, record.DefinitionVersion,
                    result.Session.GetRawText(), checksum),
                new(ids.NewId(), 1, "run.completed", 1, """{"status":"completed"}""")), cancellationToken).ConfigureAwait(false);
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
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (AgentToolLoopBudgetExceededException)
        {
            await FailRunAsync(scope.ServiceProvider, store, lease, runId, "ai.agent_run.tool_budget_exceeded", cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Agent run {RunId} failed.", runId);
            await FailRunAsync(scope.ServiceProvider, store, lease, runId, "ai.agent_run.failed", cancellationToken).ConfigureAwait(false);
            return true;
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

    private static Guid ResolveWorkflowOperationId(Guid runId, string nodeKey)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(FormattableString.Invariant($"{runId:N}|workflow|{nodeKey}")));
        return new Guid(bytes.AsSpan(0, 16), bigEndian: true);
    }

    private static async Task<AgentWorkflowState?> LoadWorkflowStateAsync(
        AiAgentRunStore store,
        AiAgentRunRecord record,
        CancellationToken cancellationToken)
    {
        var checkpoint = await store.FindLatestCheckpointAsync(record.Id, cancellationToken).ConfigureAwait(false);
        if (checkpoint is null)
        {
            return AgentWorkflowState.Create(record.SessionId);
        }

        if (!AgentCheckpointCompatibility.TryValidateWorkflow(
                record.DefinitionKey,
                record.DefinitionVersion,
                checkpoint.FormatVersion,
                checkpoint.FrameworkVersion,
                out _))
        {
            return null;
        }

        var payloadChecksum = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(checkpoint.PayloadProtected)));
        if (!string.Equals(payloadChecksum, checkpoint.Checksum, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var state = AgentWorkflowState.FromJson(checkpoint.PayloadProtected);
        return state is null || state.SessionId != record.SessionId ? null : state;
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
        CancellationToken cancellationToken)
    {
        var payload = state.ToJson();
        var checksum = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
        var latest = await store.FindLatestCheckpointAsync(runId, cancellationToken).ConfigureAwait(false);
        var sequence = (latest?.Sequence ?? 0) + 1;
        var checkpointId = ids.NewId();
        return await store.CommitProgressAsync(new(
            lease,
            statusKey,
            step,
            new(checkpointId, sequence, AgentCheckpointCompatibility.CurrentCheckpointFormatVersion,
                AgentFrameworkRuntime.FrameworkVersion, record.DefinitionVersion, payload, checksum),
            new(ids.NewId(), sequence, $"run.{statusKey}", 1, eventPayload)), cancellationToken).ConfigureAwait(false);
    }

    private static async Task FailRunAsync(
        IServiceProvider provider,
        AiAgentRunStore store,
        AgentRunLease lease,
        Guid runId,
        string errorCode,
        CancellationToken cancellationToken)
    {
        await store.CommitProgressAsync(new(
            lease,
            "failed",
            null,
            null,
            new(provider.GetRequiredService<IIdGenerator>().NewId(), 1, "run.failed", 1, $$"""{"errorCode":"{{errorCode}}"}""")), cancellationToken).ConfigureAwait(false);
        try
        {
            await provider.GetRequiredService<IAiOperationBudgetStore>()
                .SettleAsync(runId, new(null, null), "failed", cancellationToken).ConfigureAwait(false);
        }
        catch (AiBudgetException)
        {
        }
    }
}
