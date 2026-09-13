using Full.NET.Abstractions.Messaging;
using Full.NET.Modules.Workflow.Contracts;

namespace Full.NET.Modules.DataApproval.Features.ProjectWorkflowOutcomes;

/// <summary>消费工作流实例完成事件并驱动 DataApproval 终态。</summary>
internal sealed class WorkflowInstanceCompletedDataApprovalSink(
    DataApprovalWorkflowOutcomeService outcomeService) : IWorkflowInstanceCompletedSink
{
    public Task HandleAsync(
        IntegrationEventContext context,
        WorkflowInstanceCompletedIntegrationEvent integrationEvent,
        CancellationToken cancellationToken) =>
        outcomeService.HandleTerminalWorkflowAsync(
            context.TenantId ?? Guid.Empty,
            integrationEvent.BusinessType,
            integrationEvent.BusinessId,
            "completed",
            integrationEvent.RecipientUserId,
            context.MessageId.ToString("D"),
            cancellationToken);
}

/// <summary>消费工作流实例驳回事件并驱动 DataApproval 终态。</summary>
internal sealed class WorkflowInstanceRejectedDataApprovalSink(
    DataApprovalWorkflowOutcomeService outcomeService) : IWorkflowInstanceRejectedSink
{
    public Task HandleAsync(
        IntegrationEventContext context,
        WorkflowInstanceRejectedIntegrationEvent integrationEvent,
        CancellationToken cancellationToken) =>
        outcomeService.HandleTerminalWorkflowAsync(
            context.TenantId ?? Guid.Empty,
            integrationEvent.BusinessType,
            integrationEvent.BusinessId,
            "rejected",
            integrationEvent.RecipientUserId,
            context.MessageId.ToString("D"),
            cancellationToken);
}

/// <summary>消费工作流实例取消事件并驱动 DataApproval 终态。</summary>
internal sealed class WorkflowInstanceCancelledDataApprovalSink(
    DataApprovalWorkflowOutcomeService outcomeService) : IWorkflowInstanceCancelledSink
{
    public Task HandleAsync(
        IntegrationEventContext context,
        WorkflowInstanceCancelledIntegrationEvent integrationEvent,
        CancellationToken cancellationToken) =>
        outcomeService.HandleTerminalWorkflowAsync(
            context.TenantId ?? Guid.Empty,
            integrationEvent.BusinessType,
            integrationEvent.BusinessId,
            "cancelled",
            integrationEvent.RecipientUserId,
            context.MessageId.ToString("D"),
            cancellationToken);
}
