using System.Text.Json;
using Full.NET.Abstractions.Results;
using Full.NET.Agents.Workflows;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Ai.Features.CompletePersonalSchedules;
using Full.NET.Modules.Ai.Runtime;
using Full.NET.Modules.Ai.Serialization;
using Full.NET.Modules.Identity.Contracts;

namespace Full.NET.Modules.Ai.Features.ManageAgentRuns;

internal sealed partial class AiAgentRunManagementService
{
    /// <summary>只从原静态Run和当前准确目标重建批准参数，不允许客户端提交任意工具操作。</summary>
    internal async Task<Result<AiPersonalScheduleCompletionProposal>> ScheduleProposalAsync(Guid runId, SessionBindingSnapshot binding, CancellationToken token)
    {
        var run = await store.FindOwnedAsync(runId, ResolveScope(), binding.UserId, token).ConfigureAwait(false);
        if (run is null || !AiScheduleCompletionService.RunMatches(run, binding) || run.StatusKey != "awaiting_approval"
            || run.DeadlineAtUtc <= clock.UtcNow || !await bindingValidator.IsValidAsync(binding, token).ConfigureAwait(false)) return Denied();
        var snapshot = JsonSerializer.Deserialize(run.BudgetJson, AiJsonSerializerContext.Default.AgentRunBudgetSnapshot);
        var state = AiWorkflowCheckpointAdmission.Restore(run, await store.FindLatestCheckpointAsync(runId, token).ConfigureAwait(false));
        var definition = AgentWorkflowRegistry.Resolve(run.DefinitionKey, run.DefinitionVersion);
        if (snapshot?.PersonalScheduleTarget is not { } target || snapshot.TargetChatSessionId is not null || state is null || definition is null
            || state.TargetResourceId != target.Id || state.TargetResourceVersion != target.Version || state.NextNodeIndex != 0
            || state.PendingNode is not null || state.NodeReceipts.Count != 0 || state.ParallelPendingNodes.Count != 0 || state.Binding is null
            || calendar is null || !await calendar.CanCompleteAsync(target.Id, target.Version, binding, token).ConfigureAwait(false)) return Denied();
        var model = await modelQueries.GetForAgentRuntimeAsync(snapshot.ModelConfigId, tenant.Id.HasValue, token).ConfigureAwait(false);
        if (!model.IsSuccess || state.Binding.ModelConfigId != snapshot.ModelConfigId || state.Binding.ModelConfigVersion != model.Value!.Version) return Denied();
        return Result<AiPersonalScheduleCompletionProposal>.Success(new(runId, AgentWorkflowNodeReceipt.ResolveOperationId(runId, "complete_schedule"),
            target.Id, target.Version, Full.NET.Modules.Calendar.Contracts.PersonalScheduleCompletionKeys.Tool, 1,
            AgentWorkflowNodeReceipt.BuildInput(definition, 0, state)));

        static Result<AiPersonalScheduleCompletionProposal> Denied() => Result<AiPersonalScheduleCompletionProposal>.Failure(
            new("ai.agent_run.proposal_unavailable", "The original completion proposal is unavailable.", ErrorType.Conflict));
    }
}
