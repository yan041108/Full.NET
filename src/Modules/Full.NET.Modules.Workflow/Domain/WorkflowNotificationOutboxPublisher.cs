using Full.NET.Data.Abstractions;
using Full.NET.Messaging.Abstractions;
using Full.NET.Modules.Workflow.Contracts;

namespace Full.NET.Modules.Workflow.Domain;

/// <summary>在 Workflow 本地事务内发布待办与实例终态提醒事实。</summary>
/// <param name="outboxWriter">事务 Outbox 写入边界。</param>
internal sealed class WorkflowNotificationOutboxPublisher(IOutboxWriter outboxWriter)
{
    private const int SchemaVersion = 1;
    private const string Producer = "fullnet.workflow";

    /// <summary>发布待办已分配事实。</summary>
    /// <param name="instanceId">工作流实例标识。</param>
    /// <param name="todoId">新待办标识。</param>
    /// <param name="recipientUserId">受信办理人标识。</param>
    /// <param name="businessType">稳定业务类型。</param>
    /// <param name="businessId">稳定业务标识。</param>
    /// <param name="occurredAtUtc">待办到达时间（UTC）。</param>
    /// <param name="cancellationToken">取消当前事务写入的令牌。</param>
    public Task PublishTodoAssignedAsync(
        Guid instanceId,
        Guid todoId,
        Guid recipientUserId,
        string businessType,
        string businessId,
        DateTimeOffset occurredAtUtc,
        CancellationToken cancellationToken = default) =>
        outboxWriter.AddAsync(
            WorkflowNotificationIntegrationEventTypes.TodoAssigned,
            SchemaVersion,
            new WorkflowTodoAssignedIntegrationEvent(
                instanceId,
                todoId,
                recipientUserId,
                businessType,
                businessId,
                occurredAtUtc),
            CreateMetadata(instanceId),
            cancellationToken);

    /// <summary>发布逾期待办催办事实。</summary>
    /// <param name="instanceId">工作流实例标识。</param>
    /// <param name="todoId">待办标识。</param>
    /// <param name="recipientUserId">当前办理人标识。</param>
    /// <param name="businessType">稳定业务类型。</param>
    /// <param name="businessId">稳定业务标识。</param>
    /// <param name="reminderSequence">催办序号。</param>
    /// <param name="occurredAtUtc">发生时间（UTC）。</param>
    /// <param name="cancellationToken">取消事务写入的令牌。</param>
    public Task PublishTodoReminderAsync(
        Guid instanceId, Guid todoId, Guid recipientUserId,
        string businessType, string businessId, int reminderSequence,
        DateTimeOffset occurredAtUtc, CancellationToken cancellationToken = default) =>
        outboxWriter.AddAsync(
            WorkflowNotificationIntegrationEventTypes.TodoReminderRequested,
            SchemaVersion,
            new WorkflowTodoReminderRequestedIntegrationEvent(
                instanceId, todoId, recipientUserId, businessType, businessId,
                reminderSequence, occurredAtUtc),
            CreateMetadata(instanceId),
            cancellationToken);

    /// <summary>发布逾期待办升级事实。</summary>
    /// <param name="instanceId">工作流实例标识。</param>
    /// <param name="todoId">待办标识。</param>
    /// <param name="recipientUserId">固定升级接收人标识。</param>
    /// <param name="businessType">稳定业务类型。</param>
    /// <param name="businessId">稳定业务标识。</param>
    /// <param name="occurredAtUtc">发生时间（UTC）。</param>
    /// <param name="cancellationToken">取消事务写入的令牌。</param>
    public Task PublishTodoEscalationAsync(
        Guid instanceId, Guid todoId, Guid recipientUserId,
        string businessType, string businessId, DateTimeOffset occurredAtUtc,
        CancellationToken cancellationToken = default) =>
        outboxWriter.AddAsync(
            WorkflowNotificationIntegrationEventTypes.TodoEscalationRequested,
            SchemaVersion,
            new WorkflowTodoEscalationRequestedIntegrationEvent(
                instanceId, todoId, recipientUserId, businessType, businessId, occurredAtUtc),
            CreateMetadata(instanceId),
            cancellationToken);

    /// <summary>发布工作流实例已完成事实。</summary>
    /// <param name="instanceId">工作流实例标识。</param>
    /// <param name="recipientUserId">流程发起人标识。</param>
    /// <param name="businessType">稳定业务类型。</param>
    /// <param name="businessId">稳定业务标识。</param>
    /// <param name="occurredAtUtc">实例完成时间（UTC）。</param>
    /// <param name="cancellationToken">取消当前事务写入的令牌。</param>
    public Task PublishInstanceCompletedAsync(
        Guid instanceId,
        Guid recipientUserId,
        string businessType,
        string businessId,
        DateTimeOffset occurredAtUtc,
        CancellationToken cancellationToken = default) =>
        outboxWriter.AddAsync(
            WorkflowNotificationIntegrationEventTypes.InstanceCompleted,
            SchemaVersion,
            new WorkflowInstanceCompletedIntegrationEvent(
                instanceId, recipientUserId, businessType, businessId, occurredAtUtc),
            CreateMetadata(instanceId),
            cancellationToken);

    /// <summary>发布工作流实例已驳回事实。</summary>
    /// <param name="instanceId">工作流实例标识。</param>
    /// <param name="recipientUserId">流程发起人标识。</param>
    /// <param name="businessType">稳定业务类型。</param>
    /// <param name="businessId">稳定业务标识。</param>
    /// <param name="occurredAtUtc">实例驳回时间（UTC）。</param>
    /// <param name="cancellationToken">取消当前事务写入的令牌。</param>
    public Task PublishInstanceRejectedAsync(
        Guid instanceId,
        Guid recipientUserId,
        string businessType,
        string businessId,
        DateTimeOffset occurredAtUtc,
        CancellationToken cancellationToken = default) =>
        outboxWriter.AddAsync(
            WorkflowNotificationIntegrationEventTypes.InstanceRejected,
            SchemaVersion,
            new WorkflowInstanceRejectedIntegrationEvent(
                instanceId, recipientUserId, businessType, businessId, occurredAtUtc),
            CreateMetadata(instanceId),
            cancellationToken);

    /// <summary>发布工作流实例已取消事实。</summary>
    /// <param name="instanceId">工作流实例标识。</param>
    /// <param name="recipientUserId">流程发起人标识。</param>
    /// <param name="businessType">稳定业务类型。</param>
    /// <param name="businessId">稳定业务标识。</param>
    /// <param name="occurredAtUtc">实例取消时间（UTC）。</param>
    /// <param name="cancellationToken">取消当前事务写入的令牌。</param>
    public Task PublishInstanceCancelledAsync(
        Guid instanceId,
        Guid recipientUserId,
        string businessType,
        string businessId,
        DateTimeOffset occurredAtUtc,
        CancellationToken cancellationToken = default) =>
        outboxWriter.AddAsync(
            WorkflowNotificationIntegrationEventTypes.InstanceCancelled,
            SchemaVersion,
            new WorkflowInstanceCancelledIntegrationEvent(
                instanceId, recipientUserId, businessType, businessId, occurredAtUtc),
            CreateMetadata(instanceId),
            cancellationToken);

    /// <summary>为同一实例的全部事件固定分区顺序和生产者所有权。</summary>
    /// <param name="instanceId">工作流实例标识。</param>
    /// <returns>受控 Outbox 元数据。</returns>
    private static IntegrationEventMetadata CreateMetadata(Guid instanceId) =>
        IntegrationEventMetadata.Create(instanceId.ToString("D"), Producer);
}
