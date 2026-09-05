using Full.NET.Abstractions.Messaging;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Workflow.Contracts;

namespace Full.NET.Modules.DataApproval.Features.ProjectWorkflowOutcomes;

/// <summary>消费工作流实例完成事件并驱动 DataApproval 终态。</summary>
internal sealed class WorkflowInstanceCompletedDataApprovalHandler(
    IIntegrationEventSerializer serializer,
    DataApprovalWorkflowOutcomeService outcomeService) : IIntegrationEventHandler
{
    /// <inheritdoc />
    public string EventType => WorkflowNotificationIntegrationEventTypes.InstanceCompleted;

    /// <inheritdoc />
    public int SchemaVersion => 1;

    /// <inheritdoc />
    public IntegrationEventIdempotencyStrategy IdempotencyStrategy =>
        IntegrationEventIdempotencyStrategy.MessageIdDeduplication;

    /// <inheritdoc />
    public Task HandleAsync(
        IntegrationEventContext context,
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken)
    {
        var value = serializer.Deserialize<WorkflowInstanceCompletedIntegrationEvent>(payload);
        return outcomeService.HandleTerminalWorkflowAsync(
            context.TenantId ?? Guid.Empty,
            value.BusinessType,
            value.BusinessId,
            "completed",
            value.RecipientUserId,
            context.MessageId.ToString("D"),
            cancellationToken);
    }

    /// <inheritdoc />
    public Task HandleAsync(ReadOnlyMemory<byte> payload, CancellationToken cancellationToken) =>
        throw new InvalidOperationException("data_approvals.workflow_message_context_required");
}

/// <summary>消费工作流实例驳回事件并驱动 DataApproval 终态。</summary>
internal sealed class WorkflowInstanceRejectedDataApprovalHandler(
    IIntegrationEventSerializer serializer,
    DataApprovalWorkflowOutcomeService outcomeService) : IIntegrationEventHandler
{
    /// <inheritdoc />
    public string EventType => WorkflowNotificationIntegrationEventTypes.InstanceRejected;

    /// <inheritdoc />
    public int SchemaVersion => 1;

    /// <inheritdoc />
    public IntegrationEventIdempotencyStrategy IdempotencyStrategy =>
        IntegrationEventIdempotencyStrategy.MessageIdDeduplication;

    /// <inheritdoc />
    public Task HandleAsync(
        IntegrationEventContext context,
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken)
    {
        var value = serializer.Deserialize<WorkflowInstanceRejectedIntegrationEvent>(payload);
        return outcomeService.HandleTerminalWorkflowAsync(
            context.TenantId ?? Guid.Empty,
            value.BusinessType,
            value.BusinessId,
            "rejected",
            value.RecipientUserId,
            context.MessageId.ToString("D"),
            cancellationToken);
    }

    /// <inheritdoc />
    public Task HandleAsync(ReadOnlyMemory<byte> payload, CancellationToken cancellationToken) =>
        throw new InvalidOperationException("data_approvals.workflow_message_context_required");
}

/// <summary>消费工作流实例取消事件并驱动 DataApproval 终态。</summary>
internal sealed class WorkflowInstanceCancelledDataApprovalHandler(
    IIntegrationEventSerializer serializer,
    DataApprovalWorkflowOutcomeService outcomeService) : IIntegrationEventHandler
{
    /// <inheritdoc />
    public string EventType => WorkflowNotificationIntegrationEventTypes.InstanceCancelled;

    /// <inheritdoc />
    public int SchemaVersion => 1;

    /// <inheritdoc />
    public IntegrationEventIdempotencyStrategy IdempotencyStrategy =>
        IntegrationEventIdempotencyStrategy.MessageIdDeduplication;

    /// <inheritdoc />
    public Task HandleAsync(
        IntegrationEventContext context,
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken)
    {
        var value = serializer.Deserialize<WorkflowInstanceCancelledIntegrationEvent>(payload);
        return outcomeService.HandleTerminalWorkflowAsync(
            context.TenantId ?? Guid.Empty,
            value.BusinessType,
            value.BusinessId,
            "cancelled",
            value.RecipientUserId,
            context.MessageId.ToString("D"),
            cancellationToken);
    }

    /// <inheritdoc />
    public Task HandleAsync(ReadOnlyMemory<byte> payload, CancellationToken cancellationToken) =>
        throw new InvalidOperationException("data_approvals.workflow_message_context_required");
}
