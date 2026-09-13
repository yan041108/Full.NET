using Full.NET.Abstractions.Messaging;

namespace Full.NET.Modules.Workflow.Contracts;

/// <summary>消费工作流实例完成事实的 Outbox 扇出接收点。</summary>
public interface IWorkflowInstanceCompletedSink
{
    /// <summary>处理已反序列化的实例完成事件。</summary>
    Task HandleAsync(
        IntegrationEventContext context,
        WorkflowInstanceCompletedIntegrationEvent integrationEvent,
        CancellationToken cancellationToken);
}

/// <summary>消费工作流实例驳回事实的 Outbox 扇出接收点。</summary>
public interface IWorkflowInstanceRejectedSink
{
    /// <summary>处理已反序列化的实例驳回事件。</summary>
    Task HandleAsync(
        IntegrationEventContext context,
        WorkflowInstanceRejectedIntegrationEvent integrationEvent,
        CancellationToken cancellationToken);
}

/// <summary>消费工作流实例取消事实的 Outbox 扇出接收点。</summary>
public interface IWorkflowInstanceCancelledSink
{
    /// <summary>处理已反序列化的实例取消事件。</summary>
    Task HandleAsync(
        IntegrationEventContext context,
        WorkflowInstanceCancelledIntegrationEvent integrationEvent,
        CancellationToken cancellationToken);
}
