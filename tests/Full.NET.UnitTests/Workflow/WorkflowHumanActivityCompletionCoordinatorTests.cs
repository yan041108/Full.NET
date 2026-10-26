using System.Text.Json;
using Full.NET.Abstractions.Ids;
using Full.NET.Data.Abstractions;
using Full.NET.Messaging.Abstractions;
using Full.NET.Modules.Workflow.Contracts;
using Full.NET.Modules.Workflow.Domain;
using Full.NET.Modules.Workflow.Persistence;
using NSubstitute;

namespace Full.NET.UnitTests.Workflow;

/// <summary>活动最终效果与真实步骤绑定，不产生假待办、跨模块调用或个人票。</summary>
[TestClass]
public sealed class WorkflowHumanActivityCompletionCoordinatorTests
{
    [TestMethod]
    [DataRow(true, "completed")]
    [DataRow(false, "rejected")]
    public async Task Final_result_publishes_only_matching_listener_without_form_or_personal_vote(bool accepted, string phase)
    {
        var query = Substitute.For<IQueryExecutor>(); var commands = Substitute.For<ICommandExecutor>();
        var outbox = Substitute.For<IOutboxWriter>(); var ids = Substitute.For<IIdGenerator>();
        var coordinator = new WorkflowHumanActivityCompletionCoordinator(new(query, commands, new(query, commands), ids), outbox);
        var instance = new WorkflowInstanceRecord(Guid.CreateVersion7(), null, "host", "host", Guid.CreateVersion7(), Guid.CreateVersion7(),
            "order", "one", null, "active", 1, Guid.CreateVersion7(), DateTimeOffset.UtcNow, null, null, null, null, null, null);
        WorkflowNodeDraft Node(string key, string kind, object config) => new(key, kind, 1, JsonSerializer.SerializeToElement(config));
        var draft = new WorkflowDefinitionDraft(1, [Node("start", "start", new { nextNodeKeys = new[] { "review" } }),
            Node("review", "human.approval", new { nextNodeKeys = new[] { "end" }, listeners = new[] {
                new { listenerKey = "done", eventKey = "completed", actionKey = "notify.initiator", actionVersion = 1 },
                new { listenerKey = "failed", eventKey = "rejected", actionKey = "notify.initiator", actionVersion = 1 } } }), Node("end", "end", new { })], "automation");
        Assert.IsTrue(WorkflowRuntimePlan.TryCreate(draft, out var plan));
        var step = Guid.CreateVersion7();
        var result = await coordinator.ApplyAsync(instance, step, "review", plan!, new Dictionary<string, JsonElement>(), accepted,
            null, null, null, DateTimeOffset.UtcNow, CancellationToken.None);
        Assert.IsTrue(result.IsSuccess);
        await outbox.Received(1).AddAsync(WorkflowNotificationIntegrationEventTypes.ActivityListenerRequested, 1,
            Arg.Is<WorkflowActivityListenerRequestedIntegrationEvent>(value => value != null && value.InstanceId == instance.Id &&
                value.StepId == step && value.EventKey == phase && value.RecipientUserId == instance.StartedById),
            Arg.Is<IntegrationEventMetadata>(metadata => metadata != null && metadata.PartitionKey == instance.Id.ToString("D")), CancellationToken.None);
        Assert.AreEqual(0, query.ReceivedCalls().Count()); Assert.AreEqual(0, commands.ReceivedCalls().Count());
    }
}
