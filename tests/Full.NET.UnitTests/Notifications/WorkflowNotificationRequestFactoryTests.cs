using Full.NET.Modules.Notifications.Features.ProjectWorkflowNotifications;
using Full.NET.Modules.Workflow.Contracts;

namespace Full.NET.UnitTests.Notifications;

/// <summary>验证 Workflow 事件只映射为有界通知参数和稳定业务幂等键。</summary>
[TestClass]
public sealed class WorkflowNotificationRequestFactoryTests
{
    /// <summary>待办事件应选择待办模板，并提供登录后定位所需的最小参数。</summary>
    [TestMethod]
    public void Assigned_event_maps_to_todo_scene_and_recipient()
    {
        var messageId = Guid.CreateVersion7();
        var integrationEvent = new WorkflowTodoAssignedIntegrationEvent(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            "contract",
            "C-001",
            DateTimeOffset.Parse("2026-09-05T01:00:00Z"));

        var request = WorkflowNotificationRequestFactory.Create(messageId, integrationEvent);

        Assert.AreEqual("workflow", request.ProducerKey);
        Assert.AreEqual("workflow.todo.assigned", request.SceneKey);
        Assert.AreEqual("workflow.todo.assigned", request.TemplateKey);
        Assert.AreEqual($"workflow-{messageId:N}", request.IdempotencyKey);
        Assert.HasCount(1, request.Recipients);
        Assert.AreEqual(integrationEvent.RecipientUserId.ToString("N"), request.Recipients[0].RecipientKey);
        Assert.AreEqual(integrationEvent.InstanceId.ToString("D"), request.Parameters.GetProperty("instanceId").GetString());
        Assert.AreEqual(integrationEvent.TodoId.ToString("D"), request.Parameters.GetProperty("todoId").GetString());
        Assert.AreEqual(4, request.Parameters.EnumerateObject().Count());
    }

    /// <summary>实例终态必须选择独立场景，且不得伪造不存在的待办参数。</summary>
    [TestMethod]
    public void Terminal_events_map_to_distinct_scenes_without_todo_id()
    {
        var instanceId = Guid.CreateVersion7();
        var recipientId = Guid.CreateVersion7();
        var occurredAtUtc = DateTimeOffset.Parse("2026-09-05T02:00:00Z");
        var messageId = Guid.CreateVersion7();

        var completed = WorkflowNotificationRequestFactory.Create(messageId,
            new WorkflowInstanceCompletedIntegrationEvent(
                instanceId, recipientId, "contract", "C-002", occurredAtUtc));
        var rejected = WorkflowNotificationRequestFactory.Create(messageId,
            new WorkflowInstanceRejectedIntegrationEvent(
                instanceId, recipientId, "contract", "C-002", occurredAtUtc));
        var cancelled = WorkflowNotificationRequestFactory.Create(messageId,
            new WorkflowInstanceCancelledIntegrationEvent(
                instanceId, recipientId, "contract", "C-002", occurredAtUtc));

        Assert.AreEqual("workflow.instance.completed", completed.SceneKey);
        Assert.AreEqual("workflow.instance.rejected", rejected.SceneKey);
        Assert.AreEqual("workflow.instance.cancelled", cancelled.SceneKey);
        Assert.IsFalse(completed.Parameters.TryGetProperty("todoId", out _));
        Assert.AreEqual(3, completed.Parameters.EnumerateObject().Count());
    }

    /// <summary>超时信号必须保留待办深链，并选择各自的模板和收件人。</summary>
    [TestMethod]
    public void Timeout_signals_map_to_distinct_scenes_with_todo_deep_link()
    {
        var instanceId = Guid.CreateVersion7();
        var todoId = Guid.CreateVersion7();
        var reminderRecipient = Guid.CreateVersion7();
        var escalationRecipient = Guid.CreateVersion7();
        var occurredAtUtc = DateTimeOffset.Parse("2026-09-05T03:00:00Z");
        var reminder = WorkflowNotificationRequestFactory.Create(Guid.CreateVersion7(),
            new WorkflowTodoReminderRequestedIntegrationEvent(instanceId, todoId,
                reminderRecipient, "contract", "C-003", 2, occurredAtUtc));
        var escalation = WorkflowNotificationRequestFactory.Create(Guid.CreateVersion7(),
            new WorkflowTodoEscalationRequestedIntegrationEvent(instanceId, todoId,
                escalationRecipient, "contract", "C-003", occurredAtUtc));

        Assert.AreEqual("workflow.todo.reminder", reminder.SceneKey);
        Assert.AreEqual("workflow.todo.escalation", escalation.SceneKey);
        Assert.AreEqual(reminderRecipient.ToString("N"), reminder.Recipients[0].RecipientKey);
        Assert.AreEqual(escalationRecipient.ToString("N"), escalation.Recipients[0].RecipientKey);
        Assert.AreEqual(todoId.ToString("D"), reminder.Parameters.GetProperty("todoId").GetString());
        Assert.AreEqual(2, reminder.Parameters.GetProperty("reminderSequence").GetInt32());
    }
}
