using Full.NET.Abstractions.Messaging;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Workflow.Contracts;

namespace Full.NET.Modules.Notifications.Features.ProjectWorkflowNotifications;

/// <summary>通知模块只执行已注册的站内信动作；消息租户与持久去重沿用真实投递上下文。</summary>
internal sealed class WorkflowActivityListenerIntegrationEventHandler(IIntegrationEventSerializer serializer,
    WorkflowNotificationProjectionService projection) : IIntegrationEventHandler
{
    public string EventType => WorkflowNotificationIntegrationEventTypes.ActivityListenerRequested;
    public int SchemaVersion => 1;
    public IntegrationEventIdempotencyStrategy IdempotencyStrategy => IntegrationEventIdempotencyStrategy.MessageIdDeduplication;
    public Task HandleAsync(IntegrationEventContext context, ReadOnlyMemory<byte> payload, CancellationToken token)
    {
        var value = serializer.Deserialize<WorkflowActivityListenerRequestedIntegrationEvent>(payload);
        return projection.ProjectAsync(context.TenantId, value.RecipientUserId,
            WorkflowNotificationRequestFactory.Create(context.MessageId, value), token);
    }
    public Task HandleAsync(ReadOnlyMemory<byte> payload, CancellationToken token) =>
        throw new InvalidOperationException("notifications.workflow_message_context_required");
}
