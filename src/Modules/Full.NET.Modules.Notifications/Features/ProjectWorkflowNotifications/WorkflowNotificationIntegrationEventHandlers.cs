using Full.NET.Abstractions.Messaging;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Workflow.Contracts;

namespace Full.NET.Modules.Notifications.Features.ProjectWorkflowNotifications;

/// <summary>消费工作流待办到达事件并创建通知意图。</summary>
internal sealed class WorkflowTodoAssignedIntegrationEventHandler(
    IIntegrationEventSerializer serializer,
    WorkflowNotificationProjectionService projection) : IIntegrationEventHandler
{
    /// <summary>获取规范消息类型。</summary>
    public string EventType => WorkflowNotificationIntegrationEventTypes.TodoAssigned;

    /// <summary>获取当前载荷模式版本。</summary>
    public int SchemaVersion => 1;

    /// <summary>使用 Outbox MessageId 作为 Notifications 持久化幂等键。</summary>
    public IntegrationEventIdempotencyStrategy IdempotencyStrategy =>
        IntegrationEventIdempotencyStrategy.MessageIdDeduplication;

    /// <summary>使用完整消息上下文投影待办提醒。</summary>
    /// <param name="context">可信消息上下文。</param>
    /// <param name="payload">MemoryPack 载荷。</param>
    /// <param name="cancellationToken">消息租约取消令牌。</param>
    public Task HandleAsync(
        IntegrationEventContext context,
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken)
    {
        var integrationEvent = serializer.Deserialize<WorkflowTodoAssignedIntegrationEvent>(payload);
        return projection.ProjectAsync(
            context.TenantId,
            integrationEvent.RecipientUserId,
            WorkflowNotificationRequestFactory.Create(context.MessageId, integrationEvent),
            cancellationToken);
    }

    /// <summary>拒绝缺少 MessageId 的旧式调用，避免失去持久化幂等边界。</summary>
    /// <param name="payload">未使用的事件载荷。</param>
    /// <param name="cancellationToken">未使用的取消令牌。</param>
    public Task HandleAsync(ReadOnlyMemory<byte> payload, CancellationToken cancellationToken) =>
        throw new InvalidOperationException("notifications.workflow_message_context_required");
}

/// <summary>消费逾期待办催办事件并创建通知意图。</summary>
internal sealed class WorkflowTodoReminderRequestedIntegrationEventHandler(
    IIntegrationEventSerializer serializer,
    WorkflowNotificationProjectionService projection) : IIntegrationEventHandler
{
    /// <summary>获取规范消息类型。</summary>
    public string EventType => WorkflowNotificationIntegrationEventTypes.TodoReminderRequested;
    /// <summary>获取当前载荷模式版本。</summary>
    public int SchemaVersion => 1;
    /// <summary>使用消息标识持久化去重。</summary>
    public IntegrationEventIdempotencyStrategy IdempotencyStrategy =>
        IntegrationEventIdempotencyStrategy.MessageIdDeduplication;

    /// <summary>把催办事件投影到当前办理人的通知意图。</summary>
    /// <param name="context">可信消息上下文。</param>
    /// <param name="payload">MemoryPack 载荷。</param>
    /// <param name="cancellationToken">消息租约取消令牌。</param>
    public Task HandleAsync(IntegrationEventContext context, ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken)
    {
        var value = serializer.Deserialize<WorkflowTodoReminderRequestedIntegrationEvent>(payload);
        return projection.ProjectAsync(context.TenantId, value.RecipientUserId,
            WorkflowNotificationRequestFactory.Create(context.MessageId, value), cancellationToken);
    }

    /// <summary>拒绝缺少消息上下文的调用。</summary>
    /// <param name="payload">未使用的载荷。</param>
    /// <param name="cancellationToken">未使用的取消令牌。</param>
    public Task HandleAsync(ReadOnlyMemory<byte> payload, CancellationToken cancellationToken) =>
        throw new InvalidOperationException("notifications.workflow_message_context_required");
}

/// <summary>消费逾期待办升级事件并创建通知意图。</summary>
internal sealed class WorkflowTodoEscalationRequestedIntegrationEventHandler(
    IIntegrationEventSerializer serializer,
    WorkflowNotificationProjectionService projection) : IIntegrationEventHandler
{
    /// <summary>获取规范消息类型。</summary>
    public string EventType => WorkflowNotificationIntegrationEventTypes.TodoEscalationRequested;
    /// <summary>获取当前载荷模式版本。</summary>
    public int SchemaVersion => 1;
    /// <summary>使用消息标识持久化去重。</summary>
    public IntegrationEventIdempotencyStrategy IdempotencyStrategy =>
        IntegrationEventIdempotencyStrategy.MessageIdDeduplication;

    /// <summary>把升级事件投影到发布版本固化的升级接收人。</summary>
    /// <param name="context">可信消息上下文。</param>
    /// <param name="payload">MemoryPack 载荷。</param>
    /// <param name="cancellationToken">消息租约取消令牌。</param>
    public Task HandleAsync(IntegrationEventContext context, ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken)
    {
        var value = serializer.Deserialize<WorkflowTodoEscalationRequestedIntegrationEvent>(payload);
        return projection.ProjectAsync(context.TenantId, value.RecipientUserId,
            WorkflowNotificationRequestFactory.Create(context.MessageId, value), cancellationToken);
    }

    /// <summary>拒绝缺少消息上下文的调用。</summary>
    /// <param name="payload">未使用的载荷。</param>
    /// <param name="cancellationToken">未使用的取消令牌。</param>
    public Task HandleAsync(ReadOnlyMemory<byte> payload, CancellationToken cancellationToken) =>
        throw new InvalidOperationException("notifications.workflow_message_context_required");
}

/// <summary>消费工作流实例完成事件并创建通知意图。</summary>
internal sealed class WorkflowInstanceCompletedIntegrationEventHandler(
    IIntegrationEventSerializer serializer,
    WorkflowNotificationProjectionService projection) : IIntegrationEventHandler
{
    /// <summary>获取规范消息类型。</summary>
    public string EventType => WorkflowNotificationIntegrationEventTypes.InstanceCompleted;
    /// <summary>获取当前载荷模式版本。</summary>
    public int SchemaVersion => 1;
    /// <summary>使用 Outbox MessageId 作为 Notifications 持久化幂等键。</summary>
    public IntegrationEventIdempotencyStrategy IdempotencyStrategy => IntegrationEventIdempotencyStrategy.MessageIdDeduplication;

    /// <summary>使用完整上下文投影实例完成提醒。</summary>
    /// <param name="context">可信消息上下文。</param>
    /// <param name="payload">MemoryPack 载荷。</param>
    /// <param name="cancellationToken">消息租约取消令牌。</param>
    public Task HandleAsync(IntegrationEventContext context, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken)
    {
        var value = serializer.Deserialize<WorkflowInstanceCompletedIntegrationEvent>(payload);
        return projection.ProjectAsync(context.TenantId, value.RecipientUserId,
            WorkflowNotificationRequestFactory.Create(context.MessageId, value), cancellationToken);
    }

    /// <summary>拒绝缺少 MessageId 的旧式调用。</summary>
    /// <param name="payload">未使用的事件载荷。</param>
    /// <param name="cancellationToken">未使用的取消令牌。</param>
    public Task HandleAsync(ReadOnlyMemory<byte> payload, CancellationToken cancellationToken) =>
        throw new InvalidOperationException("notifications.workflow_message_context_required");
}

/// <summary>消费工作流实例驳回事件并创建通知意图。</summary>
internal sealed class WorkflowInstanceRejectedIntegrationEventHandler(
    IIntegrationEventSerializer serializer,
    WorkflowNotificationProjectionService projection) : IIntegrationEventHandler
{
    /// <summary>获取规范消息类型。</summary>
    public string EventType => WorkflowNotificationIntegrationEventTypes.InstanceRejected;
    /// <summary>获取当前载荷模式版本。</summary>
    public int SchemaVersion => 1;
    /// <summary>使用 Outbox MessageId 作为 Notifications 持久化幂等键。</summary>
    public IntegrationEventIdempotencyStrategy IdempotencyStrategy => IntegrationEventIdempotencyStrategy.MessageIdDeduplication;

    /// <summary>使用完整上下文投影实例驳回提醒。</summary>
    /// <param name="context">可信消息上下文。</param>
    /// <param name="payload">MemoryPack 载荷。</param>
    /// <param name="cancellationToken">消息租约取消令牌。</param>
    public Task HandleAsync(IntegrationEventContext context, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken)
    {
        var value = serializer.Deserialize<WorkflowInstanceRejectedIntegrationEvent>(payload);
        return projection.ProjectAsync(context.TenantId, value.RecipientUserId,
            WorkflowNotificationRequestFactory.Create(context.MessageId, value), cancellationToken);
    }

    /// <summary>拒绝缺少 MessageId 的旧式调用。</summary>
    /// <param name="payload">未使用的事件载荷。</param>
    /// <param name="cancellationToken">未使用的取消令牌。</param>
    public Task HandleAsync(ReadOnlyMemory<byte> payload, CancellationToken cancellationToken) =>
        throw new InvalidOperationException("notifications.workflow_message_context_required");
}

/// <summary>消费工作流实例取消事件并创建通知意图。</summary>
internal sealed class WorkflowInstanceCancelledIntegrationEventHandler(
    IIntegrationEventSerializer serializer,
    WorkflowNotificationProjectionService projection) : IIntegrationEventHandler
{
    /// <summary>获取规范消息类型。</summary>
    public string EventType => WorkflowNotificationIntegrationEventTypes.InstanceCancelled;
    /// <summary>获取当前载荷模式版本。</summary>
    public int SchemaVersion => 1;
    /// <summary>使用 Outbox MessageId 作为 Notifications 持久化幂等键。</summary>
    public IntegrationEventIdempotencyStrategy IdempotencyStrategy => IntegrationEventIdempotencyStrategy.MessageIdDeduplication;

    /// <summary>使用完整上下文投影实例取消提醒。</summary>
    /// <param name="context">可信消息上下文。</param>
    /// <param name="payload">MemoryPack 载荷。</param>
    /// <param name="cancellationToken">消息租约取消令牌。</param>
    public Task HandleAsync(IntegrationEventContext context, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken)
    {
        var value = serializer.Deserialize<WorkflowInstanceCancelledIntegrationEvent>(payload);
        return projection.ProjectAsync(context.TenantId, value.RecipientUserId,
            WorkflowNotificationRequestFactory.Create(context.MessageId, value), cancellationToken);
    }

    /// <summary>拒绝缺少 MessageId 的旧式调用。</summary>
    /// <param name="payload">未使用的事件载荷。</param>
    /// <param name="cancellationToken">未使用的取消令牌。</param>
    public Task HandleAsync(ReadOnlyMemory<byte> payload, CancellationToken cancellationToken) =>
        throw new InvalidOperationException("notifications.workflow_message_context_required");
}
