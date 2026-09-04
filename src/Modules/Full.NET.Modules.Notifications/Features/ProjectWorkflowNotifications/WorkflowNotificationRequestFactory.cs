using System.Buffers;
using System.Text.Json;
using Full.NET.Modules.Notifications.Contracts;
using Full.NET.Modules.Workflow.Contracts;

namespace Full.NET.Modules.Notifications.Features.ProjectWorkflowNotifications;

/// <summary>将闭合 Workflow 事件映射为 Notifications 的模板化意图请求。</summary>
internal static class WorkflowNotificationRequestFactory
{
    private const string ProducerKey = "workflow";

    /// <summary>把待办到达事件映射为站内信意图。</summary>
    /// <param name="messageId">稳定 Outbox 消息标识。</param>
    /// <param name="integrationEvent">待办到达事件。</param>
    /// <returns>只包含定位待办所需字段的通知意图请求。</returns>
    public static CreateNotificationIntentRequest Create(
        Guid messageId,
        WorkflowTodoAssignedIntegrationEvent integrationEvent) =>
        CreateCore(
            messageId,
            "workflow.todo.assigned",
            integrationEvent.RecipientUserId,
            integrationEvent.InstanceId,
            integrationEvent.BusinessType,
            integrationEvent.BusinessId,
            integrationEvent.TodoId);

    /// <summary>把实例完成事件映射为站内信意图。</summary>
    /// <param name="messageId">稳定 Outbox 消息标识。</param>
    /// <param name="integrationEvent">实例完成事件。</param>
    /// <returns>通知流程发起人的意图请求。</returns>
    public static CreateNotificationIntentRequest Create(
        Guid messageId,
        WorkflowInstanceCompletedIntegrationEvent integrationEvent) =>
        CreateCore(
            messageId,
            "workflow.instance.completed",
            integrationEvent.RecipientUserId,
            integrationEvent.InstanceId,
            integrationEvent.BusinessType,
            integrationEvent.BusinessId,
            todoId: null);

    /// <summary>把实例驳回事件映射为站内信意图。</summary>
    /// <param name="messageId">稳定 Outbox 消息标识。</param>
    /// <param name="integrationEvent">实例驳回事件。</param>
    /// <returns>通知流程发起人的意图请求。</returns>
    public static CreateNotificationIntentRequest Create(
        Guid messageId,
        WorkflowInstanceRejectedIntegrationEvent integrationEvent) =>
        CreateCore(
            messageId,
            "workflow.instance.rejected",
            integrationEvent.RecipientUserId,
            integrationEvent.InstanceId,
            integrationEvent.BusinessType,
            integrationEvent.BusinessId,
            todoId: null);

    /// <summary>把实例取消事件映射为站内信意图。</summary>
    /// <param name="messageId">稳定 Outbox 消息标识。</param>
    /// <param name="integrationEvent">实例取消事件。</param>
    /// <returns>通知流程发起人的意图请求。</returns>
    public static CreateNotificationIntentRequest Create(
        Guid messageId,
        WorkflowInstanceCancelledIntegrationEvent integrationEvent) =>
        CreateCore(
            messageId,
            "workflow.instance.cancelled",
            integrationEvent.RecipientUserId,
            integrationEvent.InstanceId,
            integrationEvent.BusinessType,
            integrationEvent.BusinessId,
            todoId: null);

    /// <summary>创建使用同名场景和模板的稳定通知请求。</summary>
    /// <param name="messageId">稳定 Outbox 消息标识。</param>
    /// <param name="sceneKey">通知场景与模板键。</param>
    /// <param name="recipientUserId">受信收件人标识。</param>
    /// <param name="instanceId">工作流实例标识。</param>
    /// <param name="businessType">稳定业务类型。</param>
    /// <param name="businessId">稳定业务标识。</param>
    /// <param name="todoId">可选待办标识。</param>
    /// <returns>可交给既有 Intent 管道的请求。</returns>
    private static CreateNotificationIntentRequest CreateCore(
        Guid messageId,
        string sceneKey,
        Guid recipientUserId,
        Guid instanceId,
        string businessType,
        string businessId,
        Guid? todoId)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("instanceId", instanceId);
            if (todoId is { } value)
            {
                writer.WriteString("todoId", value);
            }

            writer.WriteString("businessType", businessType);
            writer.WriteString("businessId", businessId);
            writer.WriteEndObject();
        }

        using var document = JsonDocument.Parse(buffer.WrittenMemory);
        return new CreateNotificationIntentRequest(
            ProducerKey,
            sceneKey,
            sceneKey,
            [new NotificationRecipientInput("user", recipientUserId.ToString("N"))],
            document.RootElement.Clone(),
            $"workflow-{messageId:N}");
    }
}
