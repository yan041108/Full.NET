using Full.NET.Abstractions.Messaging;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Workflow.Contracts;

namespace Full.NET.Modules.Workflow.Features.ProjectWorkflowTerminalEvents;

/// <summary>
/// 旧 Outbox 轮询对 workflow 终态事件只允许单一路由；本 Handler 负责反序列化后扇出到各模块 Sink。
/// </summary>
internal sealed class WorkflowInstanceCompletedIntegrationEventHandler(
    IIntegrationEventSerializer serializer,
    IEnumerable<IWorkflowInstanceCompletedSink> sinks) : IIntegrationEventHandler
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
        var integrationEvent = serializer.Deserialize<WorkflowInstanceCompletedIntegrationEvent>(payload);
        foreach (var sink in sinks)
        {
            await sink.HandleAsync(context, integrationEvent, cancellationToken).ConfigureAwait(false);
        }
    }

    public Task HandleAsync(ReadOnlyMemory<byte> payload, CancellationToken cancellationToken) =>
        throw new InvalidOperationException("workflow.instance_terminal_message_context_required");
}

internal sealed class WorkflowInstanceRejectedIntegrationEventHandler(
    IIntegrationEventSerializer serializer,
    IEnumerable<IWorkflowInstanceRejectedSink> sinks) : IIntegrationEventHandler
{
    public string EventType => WorkflowNotificationIntegrationEventTypes.InstanceRejected;
    public int SchemaVersion => 1;
    public IntegrationEventIdempotencyStrategy IdempotencyStrategy =>
        IntegrationEventIdempotencyStrategy.MessageIdDeduplication;

    public async Task HandleAsync(
        IntegrationEventContext context,
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken)
    {
        var integrationEvent = serializer.Deserialize<WorkflowInstanceRejectedIntegrationEvent>(payload);
        foreach (var sink in sinks)
        {
            await sink.HandleAsync(context, integrationEvent, cancellationToken).ConfigureAwait(false);
        }
    }

    public Task HandleAsync(ReadOnlyMemory<byte> payload, CancellationToken cancellationToken) =>
        throw new InvalidOperationException("workflow.instance_terminal_message_context_required");
}

internal sealed class WorkflowInstanceCancelledIntegrationEventHandler(
    IIntegrationEventSerializer serializer,
    IEnumerable<IWorkflowInstanceCancelledSink> sinks) : IIntegrationEventHandler
{
    public string EventType => WorkflowNotificationIntegrationEventTypes.InstanceCancelled;
    public int SchemaVersion => 1;
    public IntegrationEventIdempotencyStrategy IdempotencyStrategy =>
        IntegrationEventIdempotencyStrategy.MessageIdDeduplication;

    public async Task HandleAsync(
        IntegrationEventContext context,
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken)
    {
        var integrationEvent = serializer.Deserialize<WorkflowInstanceCancelledIntegrationEvent>(payload);
        foreach (var sink in sinks)
        {
            await sink.HandleAsync(context, integrationEvent, cancellationToken).ConfigureAwait(false);
        }
    }

    public Task HandleAsync(ReadOnlyMemory<byte> payload, CancellationToken cancellationToken) =>
        throw new InvalidOperationException("workflow.instance_terminal_message_context_required");
}
