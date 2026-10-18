using Full.NET.Abstractions.Messaging;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Workflow.Contracts;
using System.Runtime.ExceptionServices;

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
        await WorkflowTerminalEventFanout.DeliverAsync(sinks,
            sink => sink.HandleAsync(context, integrationEvent, cancellationToken), cancellationToken).ConfigureAwait(false);
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
        await WorkflowTerminalEventFanout.DeliverAsync(sinks,
            sink => sink.HandleAsync(context, integrationEvent, cancellationToken), cancellationToken).ConfigureAwait(false);
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
        await WorkflowTerminalEventFanout.DeliverAsync(sinks,
            sink => sink.HandleAsync(context, integrationEvent, cancellationToken), cancellationToken).ConfigureAwait(false);
    }

    public Task HandleAsync(ReadOnlyMemory<byte> payload, CancellationToken cancellationToken) =>
        throw new InvalidOperationException("workflow.instance_terminal_message_context_required");
}

/// <summary>顺序尝试独立事务消费者；保留全部失败，避免通知故障阻断业务终态回写。</summary>
internal static class WorkflowTerminalEventFanout
{
    internal static async Task DeliverAsync<TSink>(IEnumerable<TSink> sinks,
        Func<TSink, Task> deliver, CancellationToken cancellationToken)
    {
        List<Exception>? failures = null;
        cancellationToken.ThrowIfCancellationRequested();
        foreach (var sink in sinks)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                // 共享作用域中的数据库会话不能并发使用，各消费者仍分别维护本地事务。
                await deliver(sink).ConfigureAwait(false);
            }
            catch (Exception error) when (!cancellationToken.IsCancellationRequested)
            {
                (failures ??= []).Add(error);
            }
            cancellationToken.ThrowIfCancellationRequested();
        }
        // 取消后即使最后一个消费者忽略令牌并返回成功，也不能确认整条 Outbox 消息。
        cancellationToken.ThrowIfCancellationRequested();
        if (failures is { Count: 1 }) ExceptionDispatchInfo.Capture(failures[0]).Throw();
        if (failures is { Count: > 1 }) throw new AggregateException(failures);
    }
}
