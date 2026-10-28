using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Workflow.Domain;
using Full.NET.Modules.Workflow.Persistence;
using NSubstitute;

namespace Full.NET.UnitTests.Workflow;

/// <summary>到达领取池只冻结候选及SLA，待实际领取后才产生个人待办与提醒。</summary>
[TestClass]
public sealed class WorkflowClaimActivationTests
{
    [TestMethod]
    public async Task Arrival_creates_a_pool_without_todos_or_assignment_events()
    {
        var command = Substitute.For<ICommandExecutor>(); var ids = Substitute.For<IIdGenerator>();
        var outbox = Substitute.For<IOutboxWriter>();
        command.ExecuteAsync(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(1);
        ids.NewId().Returns(_ => Guid.CreateVersion7());
        var result = await new WorkflowHumanActivityActivationWriter(command, ids, new(outbox)).WriteClaimAsync(
            Guid.CreateVersion7(), "host", Guid.CreateVersion7(), "claim", new(new(true), [Guid.CreateVersion7(), Guid.CreateVersion7()]),
            1, DateTimeOffset.UtcNow, new(60, 10, 3, null, null), null, null, "{}", "hash", default);
        Assert.AreNotEqual(Guid.Empty, result);
        await command.Received(1).ExecuteAsync(WorkflowClaimSql.InsertPool, Arg.Any<object?>(), Arg.Any<CancellationToken>());
        await command.Received(2).ExecuteAsync(WorkflowClaimSql.InsertCandidate, Arg.Any<object?>(), Arg.Any<CancellationToken>());
        await command.Received(1).ExecuteAsync(WorkflowActivationBindingSql.BindStep, Arg.Any<object?>(), Arg.Any<CancellationToken>());
        Assert.IsFalse(command.ReceivedCalls().Any(call=>Equals(call.GetArguments()[0],WorkflowSql.InsertTodo)));
        Assert.AreEqual(0, outbox.ReceivedCalls().Count());
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(21)]
    [DataRow(2)]
    public async Task Invalid_or_duplicate_candidates_write_nothing(int count)
    {
        var command = Substitute.For<ICommandExecutor>(); var ids = Substitute.For<IIdGenerator>();
        var outbox = Substitute.For<IOutboxWriter>(); var user = Guid.CreateVersion7();
        var members = count == 2 ? new[] { user, user } : Enumerable.Range(0, count).Select(_ => Guid.CreateVersion7()).ToArray();
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            new WorkflowHumanActivityActivationWriter(command, ids, new(outbox)).WriteClaimAsync(Guid.CreateVersion7(), "host", Guid.CreateVersion7(), "claim",
                new(new(true), members), 1, DateTimeOffset.UtcNow, null, null, null, "{}", "hash", default));
        Assert.AreEqual(0, command.ReceivedCalls().Count());
    }
}
