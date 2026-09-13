using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Full.NET.AI.Abstractions.Budgets;
using Full.NET.Abstractions.Ids;
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
internal sealed class AiAgentRunManagementService(
    AiAgentRunStore store,
    AiAgentRunReadiness readiness,
    AiAgentRunApprovalGate approvalGate,
    AiModelConfigQueryService modelQueries,
    IBackgroundSessionBindingValidator bindingValidator,
    IAiOperationBudgetStore budgetStore,
    ICurrentTenant tenant,
    IClock clock,
    IIdGenerator ids,
    IOptions<AiAgentRuntimeOptions> runtimeOptions)
{
    public const string SingleTextDefinitionKey = AgentDefinitionRegistry.SingleTextKey;
    public const string ReadOnlyToolLoopDefinitionKey = AgentDefinitionRegistry.ReadOnlyToolLoopKey;
    public const string ChatRenameWorkflowDefinitionKey = AgentWorkflowRegistry.ChatRenameWorkflowKey;
    public const int SingleTextDefinitionVersion = 1;

    public async Task<Result<CreateAiAgentRunResponse>> CreateAsync(
        CreateAiAgentRunRequest request,
        SessionBindingSnapshot binding,
        CancellationToken cancellationToken = default)
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
        var requestHash = ComputeRequestHash(request, scope);
        var existingId = await store.FindClientRequestAsync(scope, binding.UserId, request.ClientRequestId, cancellationToken)
            .ConfigureAwait(false);
        if (existingId != Guid.Empty)
        {
            return Result<CreateAiAgentRunResponse>.Success(new(existingId));
        }

        var modelResult = await modelQueries.GetByIdAsync(request.ModelConfigId, cancellationToken).ConfigureAwait(false);
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

        var runId = ids.NewId();
        var budgetJson = JsonSerializer.Serialize(
            new AgentRunBudgetSnapshot(request.ModelConfigId, request.Prompt, request.InputTokenLimit, request.OutputTokenLimit),
            AiJsonSerializerContext.Default.AgentRunBudgetSnapshot);
        var deadline = clock.UtcNow.AddSeconds(runtimeOptions.Value.MaxRunDurationSeconds);
        var bindingId = ids.NewId();

        try
        {
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
            SingleTextDefinitionVersion,
            budgetJson,
            deadline,
            runId), cancellationToken).ConfigureAwait(false);

        return Result<CreateAiAgentRunResponse>.Success(new(createdId));
    }

    public async Task<Result<bool>> CancelAsync(Guid runId, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var scope = ResolveScope();
        var cancelled = await store.TryCancelOwnedAsync(runId, scope, actorUserId, cancellationToken).ConfigureAwait(false);
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

        try
        {
            await budgetStore.SettleAsync(runId, new(null, null), "cancelled", cancellationToken).ConfigureAwait(false);
        }
        catch (AiBudgetException)
        {
            // 取消优先；预算回执失败留给对账，不伪造 succeeded。
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
        if (existing is null)
        {
            return Result<bool>.Failure(new Error(
                AiErrorCodes.AgentRunNotFound,
                "The agent run was not found.",
                ErrorType.NotFound));
        }

        if (!string.Equals(existing.StatusKey, "awaiting_approval", StringComparison.Ordinal)
            || existing.DeadlineAtUtc <= clock.UtcNow
            || existing.SessionId != binding.SessionId
            || !string.Equals(existing.SecurityStamp, binding.SecurityStamp, StringComparison.Ordinal))
        {
            return NotResumable();
        }

        if (!await bindingValidator.IsValidAsync(binding, cancellationToken).ConfigureAwait(false))
        {
            return NotResumable();
        }

        if (!await approvalGate.HasApprovedUnconsumedForRunAsync(runId, binding.UserId, cancellationToken).ConfigureAwait(false))
        {
            return NotResumable();
        }

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
        var isWorkflow = AgentWorkflowRegistry.Resolve(request.DefinitionKey, SingleTextDefinitionVersion) is not null;
        var isAgent = AgentDefinitionRegistry.Resolve(request.DefinitionKey, SingleTextDefinitionVersion) is not null;
        if (request.ClientRequestId == Guid.Empty || request.ModelConfigId == Guid.Empty
            || (!isWorkflow && !isAgent)
            || (!isWorkflow && (string.IsNullOrWhiteSpace(request.Prompt) || request.Prompt.Length > 16_384))
            || (isWorkflow && request.Prompt.Length > 16_384)
            || request.InputTokenLimit is <= 0 or > AiExecutionBudget.MaximumTokens
            || request.OutputTokenLimit is <= 0 or > AiExecutionBudget.MaximumTokens)
        {
            return "The agent run request is invalid.";
        }

        return null;
    }

    private static string ComputeRequestHash(CreateAiAgentRunRequest request, string scope) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            FormattableString.Invariant($"{scope}|{request.ClientRequestId:N}|{request.DefinitionKey}|{SingleTextDefinitionVersion}|{request.ModelConfigId:N}|{request.InputTokenLimit}|{request.OutputTokenLimit}|{request.Prompt.Length}:{request.Prompt}"))));

    private string ResolveScope() => tenant.Id is { } id ? id.ToString("N") : tenant.IsHost ? "host"
        : throw new InvalidOperationException("Tenant scope is required for agent runs.");
}
