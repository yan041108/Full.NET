using Full.NET.Abstractions.Messaging;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Webhooks.Contracts;
using Full.NET.Modules.Workflow.Contracts;

namespace Full.NET.Modules.Webhooks.Features.DeliverWebhooks;

/// <summary>将工作流实例完成集成事件投影为 Webhook 投递。</summary>
internal sealed class WorkflowInstanceCompletedWebhookHandler(
    IIntegrationEventSerializer serializer,
    WebhookEventEnqueueService enqueueService) : IIntegrationEventHandler
{
    public string EventType => WorkflowNotificationIntegrationEventTypes.InstanceCompleted;

    public int SchemaVersion => 1;

    public IntegrationEventIdempotencyStrategy IdempotencyStrategy =>
        IntegrationEventIdempotencyStrategy.MessageIdDeduplication;

    public async Task HandleAsync(
        IntegrationEventContext context,
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken)
    {
        if (context.TenantId is not Guid tenantId)
        {
            return;
        }

        var integrationEvent = serializer.Deserialize<WorkflowInstanceCompletedIntegrationEvent>(payload);
        var envelope = new WebhookEventEnvelope(
            WorkflowNotificationIntegrationEventTypes.InstanceCompleted,
            context.MessageId,
            tenantId,
            integrationEvent.OccurredAtUtc,
            new WebhookWorkflowInstanceCompletedData(
                integrationEvent.InstanceId,
                integrationEvent.RecipientUserId,
                integrationEvent.BusinessType,
                integrationEvent.BusinessId));
        await enqueueService.EnqueueAsync(
                tenantId,
                WorkflowNotificationIntegrationEventTypes.InstanceCompleted,
                context.MessageId,
                envelope,
                cancellationToken)
            .ConfigureAwait(false);
    }

    public Task HandleAsync(ReadOnlyMemory<byte> payload, CancellationToken cancellationToken) =>
        throw new InvalidOperationException("webhooks.workflow_message_context_required");
}