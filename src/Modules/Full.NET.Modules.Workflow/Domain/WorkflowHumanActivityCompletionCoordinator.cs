using System.Text.Json;
using Full.NET.Abstractions.Results;
using Full.NET.Data.Abstractions;
using Full.NET.Messaging.Abstractions;
using Full.NET.Modules.Workflow.Contracts;
using Full.NET.Modules.Workflow.Persistence;

namespace Full.NET.Modules.Workflow.Domain;

/// <summary>真实活动最终收敛时在同一个实例锁事务内完成派生字段和静态业务通知；失败整体回滚。</summary>
internal sealed class WorkflowHumanActivityCompletionCoordinator(WorkflowResultCallbackWriter callbacks, IOutboxWriter outbox)
{
    public async Task<Result<WorkflowResultCallbackOutput>> ApplyAsync(WorkflowInstanceRecord instance, Guid stepId, string nodeKey,
        WorkflowRuntimePlan plan, IReadOnlyDictionary<string, JsonElement> expectedValues, bool accepted, Guid? attributionUserId,
        Guid? joinId, string? branchKey, DateTimeOffset now, CancellationToken token)
    {
        if (!plan.TryGetActivityListeners(nodeKey, out var listeners))
            return Result<WorkflowResultCallbackOutput>.Failure(new(WorkflowErrorCodes.SchemaInvalid, "The published activity listeners are invalid.", ErrorType.BusinessRule));
        var result = await callbacks.ApplyAsync(instance, stepId, nodeKey, plan, expectedValues, accepted,
            attributionUserId, joinId, branchKey, now, token).ConfigureAwait(false);
        if (!result.IsSuccess) return result;
        var phase = accepted ? "completed" : "rejected";
        foreach (var listener in listeners.Where(item => item.EventKey == phase))
            await outbox.AddAsync(WorkflowNotificationIntegrationEventTypes.ActivityListenerRequested, 1,
                new WorkflowActivityListenerRequestedIntegrationEvent(instance.Id, stepId, nodeKey, listener.ListenerKey,
                    phase, WorkflowActivityListenerPolicy.ActionKey, 1, instance.StartedById, instance.BusinessType, instance.BusinessId, now),
                IntegrationEventMetadata.Create(instance.Id.ToString("D"), "fullnet.workflow"), token).ConfigureAwait(false);
        return result;
    }
}
