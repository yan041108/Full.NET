using System.Text.Json;
using Full.NET.Abstractions.Ids;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Workflow.Domain;
using Full.NET.Modules.Workflow.Features;
using Full.NET.Modules.Workflow.Persistence;
using NSubstitute;

namespace Full.NET.UnitTests.Workflow;

/// <summary>系统组保留原组间门槛与零个人席位，不能用系统通过满足真实人审路径。</summary>
[TestClass]
public sealed class WorkflowSystemApprovalGroupTests
{
    [TestMethod]
    [DataRow("all", true)]
    [DataRow("any", false)]
    public async Task Threshold_uses_system_groups_but_human_barrier_requires_a_real_decision(string mode, bool guaranteedHuman)
    {
        var initiator = Guid.CreateVersion7(); var person = Guid.CreateVersion7();
        var config = JsonSerializer.SerializeToElement(new { nextNodeKeys = new[] { "end" }, groupApprovalPolicy = new { modeKey = mode }, groups = new object[] {
            new { groupKey = "automatic", assigneePolicy = new { sources = new[] { new { resolverKindKey = "initiator" } }, handling = new { whenInitiator = new { actionKey = "skip" } } } },
            new { groupKey = "human", assigneePolicy = new { sources = new[] { new { resolverKindKey = "specified_users", userIds = new[] { person } } } } }
        } });
        Assert.IsTrue(WorkflowGroupApprovalConfiguration.TryRead(config, out var groups));
        var result = await WorkflowTodoManagementTestDependencies.CreateAssigneeCoordinator().ResolveAsync(WorkflowAssigneePolicy.CreateDefault(),
            new("group", [], groups!.RequiredGroups, GroupConfiguration: groups), new(null, "host", "host"), initiator);
        Assert.IsTrue(result.IsSuccess); var prepared = result.Value!;
        CollectionAssert.AreEqual(new[] { person }, prepared.ApprovalPolicy!.ApproverUserIds.ToArray());
        Assert.AreEqual(0, prepared.ApprovalPolicy.Groups![0].Policy.ApproverUserIds.Count);
        Assert.AreEqual("system", prepared.ApprovalPolicy.Groups[0].Policy.ModeKey);
        Assert.AreEqual(guaranteedHuman ? null : "auto_pass", prepared.Handling?.ActionKey);
        var draft = new WorkflowDefinitionDraft(1,
            [new("start", "start", 1, JsonSerializer.SerializeToElement(new { nextNodeKeys = new[] { "groups" } })),
             new("groups", "human.group_approval", 1, config), new("end", "end", 1, JsonSerializer.SerializeToElement(new { }))]);
        Assert.AreEqual(guaranteedHuman, WorkflowRuntimePlan.TryCreate(draft, out _));
        Assert.IsTrue(WorkflowRuntimePlan.TryCreate(draft with { WorkflowKindKey = "automation" }, out _));
    }

    [TestMethod]
    public async Task Mixed_activation_writes_one_system_group_and_only_the_actual_human_todo()
    {
        var command = Substitute.For<ICommandExecutor>(); var ids = Substitute.For<IIdGenerator>(); var outbox = Substitute.For<IOutboxWriter>();
        command.ExecuteAsync(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(1); ids.NewId().Returns(_ => Guid.CreateVersion7());
        var writer = new WorkflowHumanActivityActivationWriter(command, ids, new(outbox)); var user = Guid.CreateVersion7();
        var policy = new WorkflowApprovalPolicy("group", [user], 2, Groups: [
            new("automatic", null, new("system", [], 0), new("auto_pass", "empty")), new("human", null, new("single", [user], 1))]);
        var activation = await writer.WriteAsync(Guid.CreateVersion7(), "host", Guid.CreateVersion7(), "groups", policy, Guid.Empty, 1, DateTimeOffset.UtcNow, null, "test", "one", default, "{}", "hash");
        Assert.HasCount(1, activation.TodoIds);
        await command.Received(1).ExecuteAsync(WorkflowAssigneeHandlingSql.InsertApprovedGroup, Arg.Any<object?>(), Arg.Any<CancellationToken>());
        await command.Received(1).ExecuteAsync(WorkflowGroupApprovalSql.InsertSlot, Arg.Any<object?>(), Arg.Any<CancellationToken>());
        Assert.AreEqual(1, outbox.ReceivedCalls().Count());
    }
}
