using global::MemoryPack;

namespace Full.NET.Modules.Workflow.Contracts;

/// <summary>Workflow 向通知平台发布的稳定提醒消息类型目录。</summary>
public static class WorkflowNotificationIntegrationEventTypes
{
    /// <summary>待办已到达受信办理人。</summary>
    public const string TodoAssigned = "fullnet.workflow.todo.assigned";

    /// <summary>工作流实例已完成。</summary>
    public const string InstanceCompleted = "fullnet.workflow.instance.completed";

    /// <summary>工作流实例已驳回。</summary>
    public const string InstanceRejected = "fullnet.workflow.instance.rejected";

    /// <summary>工作流实例已取消。</summary>
    public const string InstanceCancelled = "fullnet.workflow.instance.cancelled";
}

/// <summary>表示待办已与工作流状态原子提交并到达指定办理人。</summary>
/// <param name="InstanceId">工作流实例稳定标识。</param>
/// <param name="TodoId">待办稳定标识，用于生成登录后深链。</param>
/// <param name="RecipientUserId">受信任的办理人用户标识。</param>
/// <param name="BusinessType">发起方提供的稳定业务类型。</param>
/// <param name="BusinessId">发起方提供的稳定业务标识。</param>
/// <param name="OccurredAtUtc">待办到达时间（UTC）。</param>
[MemoryPackable]
public partial record WorkflowTodoAssignedIntegrationEvent(
    Guid InstanceId,
    Guid TodoId,
    Guid RecipientUserId,
    string BusinessType,
    string BusinessId,
    DateTimeOffset OccurredAtUtc);

/// <summary>表示工作流实例完成事实已提交，应提醒流程发起人。</summary>
/// <param name="InstanceId">工作流实例稳定标识。</param>
/// <param name="RecipientUserId">受信任的流程发起人用户标识。</param>
/// <param name="BusinessType">发起方提供的稳定业务类型。</param>
/// <param name="BusinessId">发起方提供的稳定业务标识。</param>
/// <param name="OccurredAtUtc">实例完成时间（UTC）。</param>
[MemoryPackable]
public partial record WorkflowInstanceCompletedIntegrationEvent(
    Guid InstanceId,
    Guid RecipientUserId,
    string BusinessType,
    string BusinessId,
    DateTimeOffset OccurredAtUtc);

/// <summary>表示工作流实例驳回事实已提交，应提醒流程发起人。</summary>
/// <param name="InstanceId">工作流实例稳定标识。</param>
/// <param name="RecipientUserId">受信任的流程发起人用户标识。</param>
/// <param name="BusinessType">发起方提供的稳定业务类型。</param>
/// <param name="BusinessId">发起方提供的稳定业务标识。</param>
/// <param name="OccurredAtUtc">实例驳回时间（UTC）。</param>
[MemoryPackable]
public partial record WorkflowInstanceRejectedIntegrationEvent(
    Guid InstanceId,
    Guid RecipientUserId,
    string BusinessType,
    string BusinessId,
    DateTimeOffset OccurredAtUtc);

/// <summary>表示工作流实例取消事实已提交，应提醒流程发起人。</summary>
/// <param name="InstanceId">工作流实例稳定标识。</param>
/// <param name="RecipientUserId">受信任的流程发起人用户标识。</param>
/// <param name="BusinessType">发起方提供的稳定业务类型。</param>
/// <param name="BusinessId">发起方提供的稳定业务标识。</param>
/// <param name="OccurredAtUtc">实例取消时间（UTC）。</param>
[MemoryPackable]
public partial record WorkflowInstanceCancelledIntegrationEvent(
    Guid InstanceId,
    Guid RecipientUserId,
    string BusinessType,
    string BusinessId,
    DateTimeOffset OccurredAtUtc);
