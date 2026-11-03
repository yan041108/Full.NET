using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Full.NET.AI.Abstractions.Budgets;
using Full.NET.AI.Abstractions.Models;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Agents.Definitions;
using Full.NET.Agents.Runtime;
using Full.NET.Agents.Workflows;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Ai.Features.ManageAgentApprovals;
using Full.NET.Modules.Ai.Features.ManageModelConfigs;
using Full.NET.Modules.Ai.Runtime;
using Full.NET.Modules.Ai.Serialization;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Ai.Features.ManageAgentRuns;

/// <summary>创建与取消持久运行；预算预留与就绪门禁在派发模型前完成。</summary>
internal sealed partial class AiAgentRunManagementService(
    AiAgentRunStore store,
    AiAgentRunReadiness readiness,
    AiAgentRunApprovalGate approvalGate,
    AiModelConfigQueryService modelQueries,
    IBackgroundSessionBindingValidator bindingValidator,
    AiPromptRunResolver prompts,
    IAiOperationBudgetStore budgetStore,
    ICurrentTenant tenant,
    IClock clock,
    IIdGenerator ids,
    IOptions<AiAgentRuntimeOptions> runtimeOptions,
    ICommandTransaction transaction, AiWorkflowTargetAdmission targets, AiChatInputAdmissionPolicy? inputPolicy = null,
    Full.NET.Modules.Calendar.Contracts.IPersonalScheduleCompletion? calendar = null)
{
    public const string SingleTextDefinitionKey = AgentDefinitionRegistry.SingleTextKey;
    public const string ReadOnlyToolLoopDefinitionKey = AgentDefinitionRegistry.ReadOnlyToolLoopKey;
    public const string ChatRenameWorkflowDefinitionKey = AgentWorkflowRegistry.ChatRenameWorkflowKey;
    public const int SingleTextDefinitionVersion = 1;

    /// <summary>封闭任务路由必须精确匹配已有定义、版本和完整预算；原入口保持既有重放兼容。</summary>
    internal Task<Result<CreateAiAgentRunResponse>> CreateRoutedAsync(CreateAiAgentRunRequest request,
        SessionBindingSnapshot binding, CancellationToken cancellationToken = default) => CreateCoreAsync(request, binding, true, cancellationToken);

    public Task<Result<CreateAiAgentRunResponse>> CreateAsync(
        CreateAiAgentRunRequest request,
        SessionBindingSnapshot binding,
        CancellationToken cancellationToken = default) => CreateCoreAsync(request, binding, false, cancellationToken);

    private async Task<Result<CreateAiAgentRunResponse>> CreateCoreAsync(CreateAiAgentRunRequest request,
        SessionBindingSnapshot binding, bool strictReplay, CancellationToken cancellationToken)
    {
        if (!await readiness.CanAcceptNewRunsAsync(cancellationToken).ConfigureAwait(false))
        {
            return Result<CreateAiAgentRunResponse>.Failure(new Error(
                AiErrorCodes.AgentRuntimeUnavailable,
                "Agent runtime is not ready to accept new runs.",
                ErrorType.BusinessRule));
        }

        var validation = ValidateRequest(request);
        if (validation is not null)
        {
            return Result<CreateAiAgentRunResponse>.Failure(new Error(
                AiErrorCodes.AgentRunInvalid,
                validation,
                ErrorType.Validation));
        }

        var scope = ResolveScope();
        if (request.PromptVersionId.HasValue && !await prompts.AuthorizedAsync(binding, cancellationToken).ConfigureAwait(false))
            return Result<CreateAiAgentRunResponse>.Failure(new Error("authorization.permission_denied", "The published Prompt version is unavailable.", ErrorType.Forbidden));
        var requestHash = ComputeRequestHash(request, scope);
        var existingId = await store.FindClientRequestAsync(scope, binding.UserId, request.ClientRequestId, cancellationToken)
            .ConfigureAwait(false);
        if (existingId != Guid.Empty)
        {
            var existing = await store.FindOwnedAsync(existingId, scope, binding.UserId, cancellationToken).ConfigureAwait(false);
            if (!(strictReplay ? RoutedReplayMatches(existing, request) : ReplayMatches(existing, request))) return ReplayConflict(strictReplay);
            return Result<CreateAiAgentRunResponse>.Success(new(existingId));
        }

        AiPromptRunPreparation? prepared = null;
        if (request.PromptVersionId.HasValue)
        {
            var promptResult = await prompts.PrepareAsync(request, binding, cancellationToken).ConfigureAwait(false);
            if (!promptResult.IsSuccess) return Result<CreateAiAgentRunResponse>.Failure(promptResult.Error!);
            prepared = promptResult.Value!;
        }
        // 普通运行同样按可信上下文限制模型归属，不能复用跨租户 Host 管理详情查询。
        var modelResult = prepared is null ? await modelQueries.GetForAgentRuntimeAsync(request.ModelConfigId, tenant.Id.HasValue, cancellationToken).ConfigureAwait(false)
            : Result<AiModelConfigResponse>.Success(AiModelConfigMapper.MapDetail(prepared.Model));
        if (!modelResult.IsSuccess)
        {
            return Result<CreateAiAgentRunResponse>.Failure(modelResult.Error!);
        }

        var model = modelResult.Value!;
        if (!model.IsEnabled)
        {
            return Result<CreateAiAgentRunResponse>.Failure(new Error(
                AiErrorCodes.ModelConfigUnavailable,
                "The AI model configuration is not available for agent runs.",
                ErrorType.BusinessRule));
        }

        if (request.TargetChatSessionId is { } target && !await targets.AuthorizeAsync(target, binding, cancellationToken).ConfigureAwait(false))
            return Result<CreateAiAgentRunResponse>.Failure(new Error("ai.agent_run.target_unavailable", "The target chat session is unavailable under the current authorization.", ErrorType.Forbidden));

        if (request.PersonalScheduleTarget is { } schedule && (calendar is null
            || !await calendar.CanCompleteAsync(schedule.Id, schedule.Version, binding, cancellationToken).ConfigureAwait(false)))
            return Result<CreateAiAgentRunResponse>.Failure(new Error("ai.agent_run.target_unavailable", "The original personal schedule is unavailable.", ErrorType.Forbidden));

        if (request.PersonalScheduleTarget is { } boundTarget
            && await store.FindBoundScheduleAsync(scope, binding.UserId, boundTarget.Id, boundTarget.Version, cancellationToken).ConfigureAwait(false) != Guid.Empty)
            return Result<CreateAiAgentRunResponse>.Failure(new Error("ai.agent_run.target_bound", "This exact target version is already bound to an original operation. Reconcile that operation.", ErrorType.Conflict));

        try { if (request.PersonalScheduleTarget is null) (inputPolicy ?? AiChatInputAdmissionPolicy.Default).RequireBudgetReservation(model.ProviderKey, model.ModelId); }
        catch (AiBudgetException ex)
        { return Result<CreateAiAgentRunResponse>.Failure(new Error(ex.Code, "Exact input metering is unavailable for this Chat transport.", ErrorType.BusinessRule)); }

        var runId = ids.NewId();
        var budgetJson = JsonSerializer.Serialize(
            new AgentRunBudgetSnapshot(request.ModelConfigId, request.Prompt, request.InputTokenLimit, request.OutputTokenLimit, prepared?.Binding, request.TargetChatSessionId, request.PersonalScheduleTarget),
            AiJsonSerializerContext.Default.AgentRunBudgetSnapshot);
        var deadline = clock.UtcNow.AddSeconds(runtimeOptions.Value.MaxRunDurationSeconds);
        var bindingId = ids.NewId();

        try
        {
            if (request.PersonalScheduleTarget is null)
            await budgetStore.ReserveAsync(new(
                runId,
                runId,
                request.ModelConfigId,
                "agent",
                requestHash,
                request.InputTokenLimit,
                request.OutputTokenLimit,
                ProviderKey: model.ProviderKey,
                ModelId: model.ModelId), cancellationToken).ConfigureAwait(false);
        }
        catch (AiBudgetException ex)
        {
            return Result<CreateAiAgentRunResponse>.Failure(new Error(ex.Code, "Agent run budget was rejected.", ErrorType.BusinessRule));
        }

        var createdId = await store.CreateOrGetAsync(new(
            request.ClientRequestId,
            requestHash,
            scope,
            tenant.Id,
            binding.UserId,
            binding.SessionId,
            bindingId,
            binding.SecurityStamp,
            binding.ActorScope,
            binding.EffectiveScope,
            request.DefinitionKey,
            ResolveDefinitionVersion(request.DefinitionKey),
            budgetJson,
            deadline,
            runId,
            binding.SessionKind), cancellationToken).ConfigureAwait(false);

        if (createdId != runId)
        {
            // 落败请求没有插入 Run 或调用模型，已知零用量必须释放 Token/费用预留，冲突载荷不能借幂等覆盖。
            if (request.PersonalScheduleTarget is null)
                await budgetStore.SettleAsync(runId, new(0, 0), "cancelled", cancellationToken).ConfigureAwait(false);
            var existing = await store.FindOwnedAsync(createdId, scope, binding.UserId, cancellationToken).ConfigureAwait(false);
            if (!(strictReplay ? RoutedReplayMatches(existing, request) : ReplayMatches(existing, request))) return ReplayConflict(strictReplay);
        }
        return Result<CreateAiAgentRunResponse>.Success(new(createdId));
    }

    /// <summary>提案只对原会话的当前授权主体可读，完整节点凭证核对后才重建待审批参数。</summary>
    internal async Task<Result<AiAgentWorkflowProposalResponse>> ProposalAsync(Guid runId, SessionBindingSnapshot binding, CancellationToken token)
    {
        var row = await store.FindOwnedAsync(runId, ResolveScope(), binding.UserId, token).ConfigureAwait(false);
        if (row is null) return Denied(ErrorType.NotFound);
        if (row.StatusKey != "awaiting_approval" || row.DeadlineAtUtc <= clock.UtcNow || row.TenantId != binding.TenantId
            || row.SessionId != binding.SessionId || row.SecurityStamp != binding.SecurityStamp || row.ActorScope != binding.ActorScope
            || row.EffectiveScope != binding.EffectiveScope
            || (string.IsNullOrWhiteSpace(row.SessionKind) ? SessionBindingKinds.Refresh : row.SessionKind) != binding.SessionKind
            || !await bindingValidator.IsValidAsync(binding, token).ConfigureAwait(false)) return Denied();
        AgentRunBudgetSnapshot? snapshot;
        try { snapshot = JsonSerializer.Deserialize(row.BudgetJson, AiJsonSerializerContext.Default.AgentRunBudgetSnapshot); }
        catch (JsonException) { return Denied(); }
        if (snapshot?.TargetChatSessionId is not { } target) return Denied();
        var definition = AgentWorkflowRegistry.Resolve(row.DefinitionKey, row.DefinitionVersion);
        var state = AiWorkflowCheckpointAdmission.Restore(row, await store.FindLatestCheckpointAsync(runId, token).ConfigureAwait(false));
        if (definition is null || state is null || state.TargetChatSessionId != target || state.NextNodeIndex >= definition.Nodes.Count
            || definition.Nodes[state.NextNodeIndex].ToolName != "ai.chat.sessions.rename" || state.PendingNode is not null
            || state.ParallelPendingNodes.Count != 0 || !AgentWorkflowRunner.HasValidRenameProposal(state)
            || !AiRunOutputBudgetAdmission.TryGetConsumedTokens(snapshot, state, out _)) return Denied();
        var model = await modelQueries.GetForAgentRuntimeAsync(snapshot.ModelConfigId, tenant.Id.HasValue, token).ConfigureAwait(false);
        if (!model.IsSuccess || state.Binding is null || model.Value!.Version != state.Binding.ModelConfigVersion
            || state.Binding.ModelConfigId != snapshot.ModelConfigId) return Denied();
        var current = await targets.ReadAsync(target, binding, token).ConfigureAwait(false);
        if (current is null) return Denied(ErrorType.Forbidden);
        return Result<AiAgentWorkflowProposalResponse>.Success(new(runId, AgentWorkflowNodeReceipt.ResolveOperationId(runId, "rename"),
            target, current.Title, current.Version, state.Outputs["summarize"].Trim(), "ai.chat.sessions.rename", 1,
            AgentWorkflowNodeReceipt.BuildInput(definition, state.NextNodeIndex, state)));

        static Result<AiAgentWorkflowProposalResponse> Denied(ErrorType type = ErrorType.Conflict) =>
            Result<AiAgentWorkflowProposalResponse>.Failure(new Error("ai.agent_run.proposal_unavailable", "The current session cannot access an actionable workflow proposal.", type));
    }

    public async Task<Result<bool>> CancelAsync(Guid runId, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var scope = ResolveScope();
        var cancelled = await transaction.ExecuteAsync(async token =>
        {
            if (!await store.TryCancelOwnedAsync(runId, scope, actorUserId, token).ConfigureAwait(false)) return false;
            token.ThrowIfCancellationRequested();
            // 取消不能证明外部派发没有发生；保留未知用量及已知账本，不按零用量退款。
            // 结算异常必须传播到最外层事务，不能在嵌套事务中转成成功或失败 Result 后继续提交。
            var cancelledRun = await store.FindOwnedAsync(runId, scope, actorUserId, token).ConfigureAwait(false);
            if (cancelledRun is null) throw new InvalidOperationException("The cancelled run disappeared.");
            if (!AgentWorkflowRegistry.IsModelFreeSchedule(cancelledRun.DefinitionKey, cancelledRun.DefinitionVersion))
                await budgetStore.SettleAsync(runId, new(null, null), "cancelled", token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            return true;
        }, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (!cancelled)
        {
            var existing = await store.FindOwnedAsync(runId, scope, actorUserId, cancellationToken).ConfigureAwait(false);
            if (existing is null)
            {
                return Result<bool>.Failure(new Error(
                    AiErrorCodes.AgentRunNotFound,
                    "The agent run was not found.",
                    ErrorType.NotFound));
            }

            return Result<bool>.Failure(new Error(
                AiErrorCodes.AgentRunNotCancellable,
                "The agent run cannot be cancelled in its current state.",
                ErrorType.BusinessRule));
        }

        return Result<bool>.Success(true);
    }

    public async Task<Result<bool>> ResumeAsync(
        Guid runId,
        SessionBindingSnapshot binding,
        CancellationToken cancellationToken = default)
    {
        var scope = ResolveScope();
        var existing = await store.FindOwnedAsync(runId, scope, binding.UserId, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (existing is null)
        {
            return Result<bool>.Failure(new Error(
                AiErrorCodes.AgentRunNotFound,
                "The agent run was not found.",
                ErrorType.NotFound));
        }

        if (!string.Equals(existing.StatusKey, "awaiting_approval", StringComparison.Ordinal)
            || existing.DeadlineAtUtc <= clock.UtcNow
            || existing.TenantId != binding.TenantId || binding.TenantId != tenant.Id
            || existing.ScopeKey != scope || ResolveScope() != scope
            || existing.ActorUserId != binding.UserId
            || existing.ActorScope != binding.ActorScope || existing.EffectiveScope != binding.EffectiveScope
            || (string.IsNullOrWhiteSpace(existing.SessionKind) ? SessionBindingKinds.Refresh : existing.SessionKind) != binding.SessionKind
            || existing.SessionId != binding.SessionId
            || !string.Equals(existing.SecurityStamp, binding.SecurityStamp, StringComparison.Ordinal))
        {
            return NotResumable();
        }

        var validBinding = await bindingValidator.IsValidAsync(binding, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (!validBinding)
        {
            return NotResumable();
        }

        var approved = await approvalGate.HasApprovedUnconsumedForRunAsync(runId, binding.UserId, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (!approved)
        {
            return NotResumable();
        }

        // 审批复核期间会话可能撤销，最后一次 Identity 检查仍处于本地写事务之外。
        validBinding = await bindingValidator.IsValidAsync(binding, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (!validBinding || existing.DeadlineAtUtc <= clock.UtcNow || ResolveScope() != scope)
            return NotResumable();

        var resumed = await store.TryResumeOwnedAsync(
            runId,
            scope,
            binding.UserId,
            binding.SessionId,
            binding.SecurityStamp,
            cancellationToken).ConfigureAwait(false);
        return resumed
            ? Result<bool>.Success(true)
            : NotResumable();
    }

    private static Result<bool> NotResumable() => Result<bool>.Failure(new Error(
        AiErrorCodes.AgentRunNotResumable,
        "The agent run cannot be resumed in its current state.",
        ErrorType.BusinessRule));

    private static string? ValidateRequest(CreateAiAgentRunRequest request)
    {
        var schedule = request.DefinitionKey == AgentWorkflowRegistry.ScheduleCompletionWorkflowKey;
        var isWorkflow = AgentWorkflowRegistry.Resolve(request.DefinitionKey, ResolveDefinitionVersion(request.DefinitionKey)) is not null;
        var isAgent = AgentDefinitionRegistry.Resolve(request.DefinitionKey, SingleTextDefinitionVersion) is not null;
        if (request.Prompt is null || request.ClientRequestId == Guid.Empty || request.ModelConfigId == Guid.Empty || request.PromptVersionId == Guid.Empty
            || (!isWorkflow && !isAgent)
            || schedule && !AgentWorkflowRegistry.IsModelFreeSchedule(request.DefinitionKey, 1)
            || (schedule ? request.TargetChatSessionId is not null || request.PromptVersionId is not null || request.Prompt.Length != 0
                    || request.InputTokenLimit != 1 || request.OutputTokenLimit != 1
                    || request.PersonalScheduleTarget is not { Version: > 0 and < int.MaxValue } s || s.Id == Guid.Empty
                : request.PersonalScheduleTarget is not null || (isWorkflow ? request.TargetChatSessionId is null || request.TargetChatSessionId == Guid.Empty : request.TargetChatSessionId is not null))
            || (!isWorkflow && (string.IsNullOrWhiteSpace(request.Prompt) || request.Prompt.Length > 16_384))
            || (isWorkflow && request.Prompt.Length > 16_384)
            || request.InputTokenLimit is <= 0 or > AiExecutionBudget.MaximumTokens
            || request.OutputTokenLimit is <= 0 or > AiExecutionBudget.MaximumTokens)
        {
            return "The agent run request is invalid.";
        }

        return null;
    }

    private static bool ReplayMatches(Persistence.AiAgentRunRecord? row, CreateAiAgentRunRequest request)
    {
        try
        {
            var saved = row is null ? null : JsonSerializer.Deserialize(row.BudgetJson, AiJsonSerializerContext.Default.AgentRunBudgetSnapshot);
            return row?.DefinitionKey == request.DefinitionKey && saved is not null && saved.ModelConfigId == request.ModelConfigId
                && saved.Prompt == request.Prompt && saved.InputTokenLimit == request.InputTokenLimit && saved.OutputTokenLimit == request.OutputTokenLimit
                && saved.PromptBinding?.VersionId == request.PromptVersionId && saved.TargetChatSessionId == request.TargetChatSessionId
                && saved.PersonalScheduleTarget == request.PersonalScheduleTarget;
        }
        catch (JsonException) { return false; }
    }
    private static Result<CreateAiAgentRunResponse> PromptConflict() => Result<CreateAiAgentRunResponse>.Failure(new Error("ai.prompts.conflict", "The Prompt run request conflicts with the original request.", ErrorType.Conflict));

    private static Result<CreateAiAgentRunResponse> ReplayConflict(bool strictReplay) => strictReplay
        ? Result<CreateAiAgentRunResponse>.Failure(new Error("ai.routing.conflict", "The routed task conflicts with the original request.", ErrorType.Conflict))
        : PromptConflict();

    /// <summary>幂等查找已经绑定当前范围、用户和请求键；比较持久快照全部语义字段，不能沿用无 Prompt 的宽松重放。</summary>
    private static bool RoutedReplayMatches(Persistence.AiAgentRunRecord? row, CreateAiAgentRunRequest request)
    {
        if (row is null || row.DefinitionKey != request.DefinitionKey || row.DefinitionVersion != ResolveDefinitionVersion(request.DefinitionKey)) return false;
        try
        {
            var saved = JsonSerializer.Deserialize(row.BudgetJson, AiJsonSerializerContext.Default.AgentRunBudgetSnapshot);
            return saved is not null && saved.PromptBinding is null && request.PromptVersionId is null
                && saved.ModelConfigId == request.ModelConfigId && saved.Prompt == request.Prompt
                && saved.InputTokenLimit == request.InputTokenLimit && saved.OutputTokenLimit == request.OutputTokenLimit
                && saved.TargetChatSessionId == request.TargetChatSessionId && saved.PersonalScheduleTarget == request.PersonalScheduleTarget;
        }
        catch (JsonException) { return false; }
    }

    private static string ComputeRequestHash(CreateAiAgentRunRequest request, string scope) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            FormattableString.Invariant($"{scope}|{request.ClientRequestId:N}|{request.DefinitionKey}|{ResolveDefinitionVersion(request.DefinitionKey)}|{request.ModelConfigId:N}|{request.InputTokenLimit}|{request.OutputTokenLimit}|{request.Prompt.Length}:{request.Prompt}")
            + (request.PromptVersionId is { } version ? FormattableString.Invariant($"|prompt-version:{version:N}") : "")
            + (request.TargetChatSessionId is { } target ? FormattableString.Invariant($"|chat-target:{target:N}") : "")
            + (request.PersonalScheduleTarget is { } schedule ? FormattableString.Invariant($"|personal-schedule:{schedule.Id:N}:{schedule.Version}") : ""))));

    /// <summary>创建与摘要使用同一版本，防止新运行继续写入旧校验协议版本。</summary>
    internal static int ResolveDefinitionVersion(string definitionKey) =>
        definitionKey switch
        {
            ChatRenameWorkflowDefinitionKey => AgentWorkflowRegistry.ChatRenameWorkflowVersion,
            AgentWorkflowRegistry.ChatReviewWorkflowKey => AgentWorkflowRegistry.ChatReviewWorkflowVersion,
            _ => SingleTextDefinitionVersion,
        };

    private string ResolveScope() => tenant.Id is { } id ? id.ToString("N") : tenant.IsHost ? "host"
        : throw new InvalidOperationException("Tenant scope is required for agent runs.");
}
