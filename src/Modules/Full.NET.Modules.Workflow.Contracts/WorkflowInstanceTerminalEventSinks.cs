using Full.NET.Abstractions.Messaging;

namespace Full.NET.Modules.Workflow.Contracts;

/// <summary>消费工作流实例完成事实的 Outbox 扇出接收点。</summary>
public interface IWorkflowInstanceCompletedSink
{
    /// <summary>处理已反序列化的实例完成事件。</summary>
    /// <param name="context">集成事件投递上下文，包含租户、消息元数据与投递追踪信息。</param>
    /// <param name="integrationEvent">已反序列化的工作流实例完成事件。</param>
    /// <param name="cancellationToken">请求取消令牌。</param>
    /// <returns>表示处理完成的 Task；处理失败时通过异常向上传播，由 Outbox 投递器决定重试或转入死信。</returns>
    Task HandleAsync(
        IntegrationEventContext context,
        WorkflowInstanceCompletedIntegrationEvent integrationEvent,
        CancellationToken cancellationToken);
}

/// <summary>消费工作流实例驳回事实的 Outbox 扇出接收点。</summary>
public interface IWorkflowInstanceRejectedSink
{
    /// <summary>处理已反序列化的实例驳回事件。</summary>
    /// <param name="context">集成事件投递上下文，包含租户、消息元数据与投递追踪信息。</param>
    /// <param name="integrationEvent">已反序列化的工作流实例驳回事件。</param>
    /// <param name="cancellationToken">请求取消令牌。</param>
    /// <returns>表示处理完成的 Task；处理失败时通过异常向上传播，由 Outbox 投递器决定重试或转入死信。</returns>
    Task HandleAsync(
        IntegrationEventContext context,
        WorkflowInstanceRejectedIntegrationEvent integrationEvent,
        CancellationToken cancellationToken);
}

/// <summary>消费工作流实例取消事实的 Outbox 扇出接收点。</summary>
public interface IWorkflowInstanceCancelledSink
{
    /// <summary>处理已反序列化的实例取消事件。</summary>
    /// <param name="context">集成事件投递上下文，包含租户、消息元数据与投递追踪信息。</param>
    /// <param name="integrationEvent">已反序列化的工作流实例取消事件。</param>
    /// <param name="cancellationToken">请求取消令牌。</param>
    /// <returns>表示处理完成的 Task；处理失败时通过异常向上传播，由 Outbox 投递器决定重试或转入死信。</returns>
    Task HandleAsync(
        IntegrationEventContext context,
        WorkflowInstanceCancelledIntegrationEvent integrationEvent,
        CancellationToken cancellationToken);
}
