using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Workflow.Domain;
using Full.NET.Modules.Workflow.Persistence;
using NSubstitute;

namespace Full.NET.UnitTests.Workflow;

/// <summary>验证多人审批节点激活时一人一席位、一人一待办并同事务发布提醒。</summary>
[TestClass]
public sealed class WorkflowApprovalActivationWriterTests
{
    [TestMethod]
    [DataRow("all", 3)]
    [DataRow("any", 3)]
    [DataRow("sequential", 1)]
    public async Task Task_activation_preserves_all_members_and_only_creates_real_todos(string mode, int realTodos)
    {
        var command = Substitute.For<ICommandExecutor>(); command.ExecuteAsync(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(1);
        var ids = Substitute.For<IIdGenerator>(); ids.NewId().Returns(_ => Guid.CreateVersion7()); var outbox = Substitute.For<IOutboxWriter>();
        var members = Enumerable.Range(0, 3).Select(_ => Guid.CreateVersion7()).ToArray();
        var writer = new WorkflowHumanActivityActivationWriter(command, ids, new(outbox));
        var result = await writer.WriteTaskAsync(Guid.CreateVersion7(), "host", "task", new(new(mode), members), 1,
            DateTimeOffset.UtcNow, null, "test", "task", null, null, "{}", new string('a', 64), default);
        Assert.HasCount(realTodos, result.TodoIds);
        await command.Received(3).ExecuteAsync(WorkflowTaskSql.InsertMember, Arg.Any<object?>(), Arg.Any<CancellationToken>());
        await command.Received(realTodos).ExecuteAsync(WorkflowSql.InsertTodo, Arg.Any<object?>(), Arg.Any<CancellationToken>());
        await command.DidNotReceive().ExecuteAsync(WorkflowSql.InsertApprovalSlot, Arg.Any<object?>(), Arg.Any<CancellationToken>());
        Assert.AreEqual(realTodos, outbox.ReceivedCalls().Count());
        if (mode == "sequential") await command.Received(2).ExecuteAsync(WorkflowTaskSql.InsertMember,
            Arg.Is<object?>(p => HasValue(p, "TodoId", null)), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Group_activation_freezes_two_independent_cohorts_and_activates_only_current_sequential_member(bool parallel)
    {
        var command = Substitute.For<ICommandExecutor>();
        command.ExecuteAsync(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(1);
        var ids = Substitute.For<IIdGenerator>(); ids.NewId().Returns(_ => Guid.CreateVersion7());
        var outbox = Substitute.For<IOutboxWriter>();
        var writer = new WorkflowHumanActivityActivationWriter(command, ids, new(outbox));
        var members = Enumerable.Range(0, 5).Select(_ => Guid.CreateVersion7()).ToArray();
        var policy = new WorkflowApprovalPolicy("group", members, 2, Groups:
            [new("first", "首组", new("any", members.Take(2).ToArray(), 1)),
             new("second", "顺序组", new("sequential", members.Skip(2).ToArray(), 3))]);
        var result = parallel
            ? await writer.WriteParallelAsync(Guid.CreateVersion7(), "host", "review", policy, Guid.Empty, 1,
                DateTimeOffset.UtcNow, null, "test", "group", Guid.CreateVersion7(), "left", default, "{}", new string('a', 64))
            : await writer.WriteAsync(Guid.CreateVersion7(), "host", "review", policy, Guid.Empty, 1,
                DateTimeOffset.UtcNow, null, "test", "group", default, "{}", new string('a', 64));
        Assert.HasCount(3, result.TodoIds);
        await command.Received(2).ExecuteAsync(WorkflowGroupApprovalSql.InsertGroup, Arg.Any<object?>(), Arg.Any<CancellationToken>());
        await command.Received(5).ExecuteAsync(WorkflowGroupApprovalSql.InsertSlot, Arg.Any<object?>(), Arg.Any<CancellationToken>());
        await command.Received(1).ExecuteAsync(WorkflowGroupApprovalSql.InsertStep,
            Arg.Is<object?>(p => HasValue(p, "ApprovalGroupCount", 2) && HasValue(p, "ApprovalSlotCount", 5) && HasValue(p, "RequiredApprovalCount", 2)), Arg.Any<CancellationToken>());
        for (var index = 3; index < 5; index++)
        {
            var position = index;
            await command.Received(1).ExecuteAsync(WorkflowGroupApprovalSql.InsertSlot,
                Arg.Is<object?>(p => HasValue(p, "AssigneeUserId", members[position]) && HasValue(p, "TodoId", null) && HasValue(p, "SequenceNo", position + 1)), Arg.Any<CancellationToken>());
        }
        Assert.AreEqual(3, outbox.ReceivedCalls().Count());
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    public async Task Sequential_binding_cas_controls_capture_and_reliable_reminder(int affected)
    {
        var command = Substitute.For<ICommandExecutor>();
        command.ExecuteAsync(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(1);
        command.ExecuteAsync(WorkflowSequentialApprovalSql.BindTodo, Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(affected);
        var ids = Substitute.For<IIdGenerator>(); var newTodo = Guid.CreateVersion7(); ids.NewId().Returns(newTodo);
        var outbox = Substitute.For<IOutboxWriter>(); var now = DateTimeOffset.UtcNow; var actor = Guid.CreateVersion7(); var instanceId = Guid.CreateVersion7();
        var previous = new WorkflowTodoRuntimeRecord(Guid.CreateVersion7(), instanceId, Guid.CreateVersion7(), actor, "active", now, null, null,
            1, "review", 1, "sequential", 3, 3, null, null, null);
        var instance = new WorkflowInstanceRecord(instanceId, null, "host", "host", Guid.CreateVersion7(), Guid.CreateVersion7(), "test", "one",
            null, "active", 1, actor, now, null, null, null, null, null, null);
        var slot = new WorkflowPendingApprovalSlotRecord(Guid.CreateVersion7(), Guid.CreateVersion7(), 2, 1);
        var values = new Dictionary<string, System.Text.Json.JsonElement> { ["first"] = System.Text.Json.JsonSerializer.SerializeToElement("after-first") };
        var result = await new WorkflowHumanActivityActivationWriter(command, ids, new WorkflowNotificationOutboxPublisher(outbox))
            .ActivateSequentialAsync(slot, previous, instance, "host", null, values, now, default);
        Assert.AreEqual(affected == 1, result.IsSuccess);
        if (affected == 1) Assert.AreEqual(newTodo, result.Value);
        await command.Received(affected).ExecuteAsync(WorkflowTodoViewSql.Capture,
            Arg.Is<object?>(p => HasValue(p, "Id", newTodo) && HasValue(p, "InputJson", "{\"first\":\"after-first\"}")), Arg.Any<CancellationToken>());
        Assert.AreEqual(affected, outbox.ReceivedCalls().Count());
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Sequential_activation_freezes_all_slots_but_creates_only_the_first_real_todo(bool parallel)
    {
        var command = Substitute.For<ICommandExecutor>();
        command.ExecuteAsync(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(1);
        var outbox = Substitute.For<IOutboxWriter>(); var ids = Substitute.For<IIdGenerator>();
        ids.NewId().Returns(_ => Guid.CreateVersion7());
        var writer = new WorkflowHumanActivityActivationWriter(command, ids, new WorkflowNotificationOutboxPublisher(outbox));
        var users = Enumerable.Range(0, 3).Select(_ => Guid.CreateVersion7()).ToArray();
        var instance = Guid.CreateVersion7(); var now = DateTimeOffset.UtcNow;
        var result = parallel
            ? await writer.WriteParallelAsync(instance, "host", "review", new("sequential", users, 3), Guid.Empty,
                1, now, null, "test", "one", Guid.CreateVersion7(), "left", default)
            : await writer.WriteAsync(instance, "host", "review", new("sequential", users, 3), Guid.Empty,
                1, now, null, "test", "one", default);
        Assert.HasCount(1, result.TodoIds);
        await command.Received(1).ExecuteAsync(WorkflowSql.InsertTodo,
            Arg.Is<object?>(p => HasValue(p, "AssigneeUserId", users[0])), Arg.Any<CancellationToken>());
        await command.Received(3).ExecuteAsync(WorkflowSequentialApprovalSql.InsertSlot, Arg.Any<object?>(), Arg.Any<CancellationToken>());
        for (var index = 1; index < 3; index++)
        {
            var expected = index;
            await command.Received(1).ExecuteAsync(WorkflowSequentialApprovalSql.InsertSlot,
                Arg.Is<object?>(p => HasValue(p, "AssigneeUserId", users[expected]) && HasValue(p, "TodoId", null) && HasValue(p, "SequenceNo", expected+1)),
                Arg.Any<CancellationToken>());
        }
        Assert.AreEqual(1, outbox.ReceivedCalls().Count());
    }

    /// <summary>N-of-M 激活必须创建单一步骤、三个席位和三个待办。</summary>
    [TestMethod]
    public async Task Multi_approval_activation_creates_one_slot_and_todo_per_approver()
    {
        var command = Substitute.For<ICommandExecutor>();
        command.ExecuteAsync(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(1);
        var outbox = Substitute.For<IOutboxWriter>();
        var ids = new Queue<Guid>(Enumerable.Range(0, 7).Select(_ => Guid.CreateVersion7()));
        var idGenerator = Substitute.For<IIdGenerator>();
        idGenerator.NewId().Returns(_ => ids.Dequeue());
        var users = new[] { Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7() };
        var writer = new WorkflowHumanActivityActivationWriter(
            command,
            idGenerator,
            new WorkflowNotificationOutboxPublisher(outbox));

        var result = await writer.WriteAsync(
            Guid.CreateVersion7(),
            "tenant:sample",
            "finance-review",
            new WorkflowApprovalPolicy("nOfM", users, 2),
            Guid.CreateVersion7(),
            4,
            DateTimeOffset.UtcNow,
            null,
            "expense",
            "EXP-001",
            CancellationToken.None, "{\"schemaVersion\":1}", new string('a', 64));

        Assert.AreEqual(3, result.TodoIds.Count);
        await command.Received(1).ExecuteAsync(
            WorkflowSql.InsertApprovalStep,
            Arg.Is<object?>(value => HasValue(value, "ApprovalModeKey", "nOfM") &&
                HasValue(value, "RequiredApprovalCount", 2) &&
                HasValue(value, "ApprovalSlotCount", 3) &&
                HasValue(value, "AssignedUserId", null) &&
                HasValue(value, "AssigneeSnapshotJson", "{\"schemaVersion\":1}") &&
                HasValue(value, "AssigneeSnapshotHash", new string('a', 64))),
            Arg.Any<CancellationToken>());
        await command.Received(3).ExecuteAsync(
            WorkflowSql.InsertTodo, Arg.Any<object?>(), Arg.Any<CancellationToken>());
        await command.Received(3).ExecuteAsync(
            WorkflowSql.InsertApprovalSlot, Arg.Any<object?>(), Arg.Any<CancellationToken>());
        Assert.AreEqual(3, outbox.ReceivedCalls().Count());
    }

    /// <summary>读取匿名参数属性并与期望值比较。</summary>
    /// <param name="value">Dapper 参数对象。</param>
    /// <param name="name">属性名称。</param>
    /// <param name="expected">期望值。</param>
    /// <returns>属性存在且值相等时返回 <see langword="true"/>。</returns>
    private static bool HasValue(object? value, string name, object? expected)
    {
        if (value is IReadOnlyDictionary<string, object?> values)
        {
            return values.TryGetValue(name, out var actual) && Equals(actual, expected);
        }

        return Equals(value?.GetType().GetProperty(name)?.GetValue(value), expected);
    }
}
