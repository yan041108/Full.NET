using Full.NET.Data.Abstractions;
using Full.NET.Modules.Workflow.Contracts;
using Full.NET.Modules.Workflow.Domain;
using Full.NET.Messaging.Abstractions;
using NSubstitute;

namespace Full.NET.UnitTests.Workflow;

/// <summary>验证 Workflow 提醒事实使用稳定消息类型、载荷和分区键写入事务 Outbox。</summary>
[TestClass]
public sealed class WorkflowNotificationOutboxPublisherTests
{
    /// <summary>待办到达必须发布给办理人，且不得携带表单正文。</summary>
    [TestMethod]
    public async Task Assigned_todo_is_published_with_stable_contract()
    {
        var outbox = Substitute.For<IOutboxWriter>();
        var publisher = new WorkflowNotificationOutboxPublisher(outbox);
        var instanceId = Guid.CreateVersion7();
        var todoId = Guid.CreateVersion7();
        var recipientId = Guid.CreateVersion7();
        var occurredAtUtc = DateTimeOffset.Parse("2026-09-05T01:00:00Z");

        await publisher.PublishTodoAssignedAsync(
            instanceId,
            todoId,
            recipientId,
            "contract",
            "C-001",
            occurredAtUtc,
            TestContext.CancellationToken);

        await outbox.Received(1).AddAsync(
            WorkflowNotificationIntegrationEventTypes.TodoAssigned,
            1,
            Arg.Is<WorkflowTodoAssignedIntegrationEvent>(value =>
                value != null &&
                value.InstanceId == instanceId &&
                value.TodoId == todoId &&
                value.RecipientUserId == recipientId &&
                value.BusinessType == "contract" &&
                value.BusinessId == "C-001" &&
                value.OccurredAtUtc == occurredAtUtc),
            Arg.Is<IntegrationEventMetadata>(value =>
                value != null &&
                value.PartitionKey == instanceId.ToString("D") &&
                value.Producer == "fullnet.workflow"),
            TestContext.CancellationToken);
    }

    /// <summary>终态消息必须按各自事件类型发布给流程发起人。</summary>
    [TestMethod]
    public async Task Terminal_states_are_published_as_distinct_events()
    {
        var outbox = Substitute.For<IOutboxWriter>();
        var publisher = new WorkflowNotificationOutboxPublisher(outbox);
        var instanceId = Guid.CreateVersion7();
        var recipientId = Guid.CreateVersion7();
        var occurredAtUtc = DateTimeOffset.Parse("2026-09-05T02:00:00Z");

        await publisher.PublishInstanceCompletedAsync(
            instanceId, recipientId, "contract", "C-002", occurredAtUtc, TestContext.CancellationToken);
        await publisher.PublishInstanceRejectedAsync(
            instanceId, recipientId, "contract", "C-002", occurredAtUtc, TestContext.CancellationToken);
        await publisher.PublishInstanceCancelledAsync(
            instanceId, recipientId, "contract", "C-002", occurredAtUtc, TestContext.CancellationToken);

        await outbox.Received(1).AddAsync(
            WorkflowNotificationIntegrationEventTypes.InstanceCompleted,
            1,
            Arg.Any<WorkflowInstanceCompletedIntegrationEvent>(),
            Arg.Any<IntegrationEventMetadata>(),
            TestContext.CancellationToken);
        await outbox.Received(1).AddAsync(
            WorkflowNotificationIntegrationEventTypes.InstanceRejected,
            1,
            Arg.Any<WorkflowInstanceRejectedIntegrationEvent>(),
            Arg.Any<IntegrationEventMetadata>(),
            TestContext.CancellationToken);
        await outbox.Received(1).AddAsync(
            WorkflowNotificationIntegrationEventTypes.InstanceCancelled,
            1,
            Arg.Any<WorkflowInstanceCancelledIntegrationEvent>(),
            Arg.Any<IntegrationEventMetadata>(),
            TestContext.CancellationToken);
    }

    /// <summary>催办与升级必须使用不同事件类型，并携带稳定信号序号。</summary>
    [TestMethod]
    public async Task Timeout_signals_are_published_with_distinct_recipients_and_sequence()
    {
        var outbox = Substitute.For<IOutboxWriter>();
        var publisher = new WorkflowNotificationOutboxPublisher(outbox);
        var instanceId = Guid.CreateVersion7();
        var todoId = Guid.CreateVersion7();
        var assigneeId = Guid.CreateVersion7();
        var escalationId = Guid.CreateVersion7();
        var occurredAtUtc = DateTimeOffset.Parse("2026-09-05T03:00:00Z");

        await publisher.PublishTodoReminderAsync(instanceId, todoId, assigneeId,
            "contract", "C-003", 2, occurredAtUtc, TestContext.CancellationToken);
        await publisher.PublishTodoEscalationAsync(instanceId, todoId, escalationId,
            "contract", "C-003", occurredAtUtc, TestContext.CancellationToken);

        await outbox.Received(1).AddAsync(
            WorkflowNotificationIntegrationEventTypes.TodoReminderRequested, 1,
            Arg.Is<WorkflowTodoReminderRequestedIntegrationEvent>(value =>
                value != null && value.RecipientUserId == assigneeId && value.ReminderSequence == 2),
            Arg.Any<IntegrationEventMetadata>(), TestContext.CancellationToken);
        await outbox.Received(1).AddAsync(
            WorkflowNotificationIntegrationEventTypes.TodoEscalationRequested, 1,
            Arg.Is<WorkflowTodoEscalationRequestedIntegrationEvent>(value =>
                value != null && value.RecipientUserId == escalationId),
            Arg.Any<IntegrationEventMetadata>(), TestContext.CancellationToken);
    }

    /// <summary>获取由 MSTest 注入的当前测试上下文。</summary>
    public TestContext TestContext { get; set; } = null!;
}
