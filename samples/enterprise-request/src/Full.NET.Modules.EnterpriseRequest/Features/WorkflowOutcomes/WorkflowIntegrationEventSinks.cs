using Full.NET.Abstractions.Messaging;
using Full.NET.Modules.EnterpriseRequest.Contracts;
using Full.NET.Modules.Workflow.Contracts;

namespace Full.NET.Modules.EnterpriseRequest.Features.WorkflowOutcomes;

internal sealed class WorkflowInstanceCompletedEnterpriseRequestSink(
    EnterpriseRequestWorkflowOutcomeService outcomeService) : IWorkflowInstanceCompletedSink
{
    public Task HandleAsync(
        IntegrationEventContext context,
        WorkflowInstanceCompletedIntegrationEvent integrationEvent,
        CancellationToken cancellationToken) =>
        outcomeService.HandleTerminalWorkflowAsync(
            integrationEvent.BusinessType,
            integrationEvent.BusinessId,
            EnterpriseRequestStatusKeys.Approved,
            context.MessageId.ToString("D"),
            cancellationToken);
}

internal sealed class WorkflowInstanceRejectedEnterpriseRequestSink(
    EnterpriseRequestWorkflowOutcomeService outcomeService) : IWorkflowInstanceRejectedSink
{
    public Task HandleAsync(
        IntegrationEventContext context,
        WorkflowInstanceRejectedIntegrationEvent integrationEvent,
        CancellationToken cancellationToken) =>
        outcomeService.HandleTerminalWorkflowAsync(
            integrationEvent.BusinessType,
            integrationEvent.BusinessId,
            EnterpriseRequestStatusKeys.Rejected,
            context.MessageId.ToString("D"),
            cancellationToken);
}

internal sealed class WorkflowInstanceCancelledEnterpriseRequestSink(
    EnterpriseRequestWorkflowOutcomeService outcomeService) : IWorkflowInstanceCancelledSink
{
    public Task HandleAsync(
        IntegrationEventContext context,
        WorkflowInstanceCancelledIntegrationEvent integrationEvent,
        CancellationToken cancellationToken) =>
        outcomeService.HandleTerminalWorkflowAsync(
            integrationEvent.BusinessType,
            integrationEvent.BusinessId,
            EnterpriseRequestStatusKeys.Cancelled,
            context.MessageId.ToString("D"),
            cancellationToken);
}