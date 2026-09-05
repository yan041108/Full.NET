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
        var writer = new WorkflowApprovalActivationWriter(
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
            CancellationToken.None);

        Assert.AreEqual(3, result.TodoIds.Count);
        await command.Received(1).ExecuteAsync(
            WorkflowSql.InsertApprovalStep,
            Arg.Is<object?>(value => HasValue(value, "ApprovalModeKey", "nOfM") &&
                HasValue(value, "RequiredApprovalCount", 2) &&
                HasValue(value, "ApprovalSlotCount", 3) &&
                HasValue(value, "AssignedUserId", null)),
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
