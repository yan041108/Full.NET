using Full.NET.Abstractions.Messaging;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Workflow.Contracts;

namespace Full.NET.Modules.Notifications.Features.ProjectWorkflowNotifications;

/// <summary>人工催办只信任消息租户上下文，按真实办理人投影独立通知意图。</summary>
internal sealed class WorkflowManualReminderIntegrationEventHandler(IIntegrationEventSerializer serializer,
    WorkflowNotificationProjectionService projection) : IIntegrationEventHandler
{
    public string EventType => WorkflowNotificationIntegrationEventTypes.TodoManualReminderRequested;
    public int SchemaVersion => 1;
    public IntegrationEventIdempotencyStrategy IdempotencyStrategy => IntegrationEventIdempotencyStrategy.MessageIdDeduplication;
    public Task HandleAsync(IntegrationEventContext context, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken)
    {
        var value = serializer.Deserialize<WorkflowTodoManualReminderRequestedIntegrationEvent>(payload);
        return projection.ProjectAsync(context.TenantId, value.RecipientUserId,
            WorkflowNotificationRequestFactory.Create(context.MessageId, value), cancellationToken);
    }
    public Task HandleAsync(ReadOnlyMemory<byte> payload, CancellationToken cancellationToken) =>
        throw new InvalidOperationException("notifications.workflow_message_context_required");
}
