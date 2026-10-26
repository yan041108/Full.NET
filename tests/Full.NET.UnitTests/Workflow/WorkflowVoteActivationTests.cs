using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Workflow.Domain;
using Full.NET.Modules.Workflow.Persistence;
using NSubstitute;

namespace Full.NET.UnitTests.Workflow;

/// <summary>每个候选有自己的票据和输入视图，投票激活不创建审批步骤或席位。</summary>
[TestClass]
public sealed class WorkflowVoteActivationTests
{
    [TestMethod]
    public async Task Candidates_receive_independent_ballots_and_views()
    {
        var command = Substitute.For<ICommandExecutor>(); var ids = Substitute.For<IIdGenerator>();
        command.ExecuteAsync(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(1);
        ids.NewId().Returns(_ => Guid.CreateVersion7()); var outbox = Substitute.For<IOutboxWriter>();
        var form = Guid.CreateVersion7();
        var result = await new WorkflowHumanActivityActivationWriter(command, ids, new(outbox)).WriteVoteAsync(Guid.CreateVersion7(), "host", "vote",
            new(new(form, 100, 67, true, 60, null, null), [Guid.CreateVersion7(), Guid.CreateVersion7()]),
            new(Guid.CreateVersion7(), form, "{}", "hash", "active"), 1, DateTimeOffset.UtcNow, null, "vote", "one", null, null, "{}", "hash", default);
        Assert.AreEqual(2, result.TodoIds.Count); Assert.AreEqual(2, result.TodoIds.Distinct().Count());
        await command.Received(2).ExecuteAsync(WorkflowVoteSql.InsertBallot, Arg.Any<object?>(), Arg.Any<CancellationToken>());
        await command.Received(2).ExecuteAsync(WorkflowVoteSql.CaptureView, Arg.Any<object?>(), Arg.Any<CancellationToken>());
        await command.DidNotReceive().ExecuteAsync(WorkflowSql.InsertApprovalStep, Arg.Any<object?>(), Arg.Any<CancellationToken>());
        Assert.AreEqual(2, outbox.ReceivedCalls().Count());
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Invalid_members_or_form_binding_write_nothing(bool wrongForm)
    {
        var command = Substitute.For<ICommandExecutor>(); var ids = Substitute.For<IIdGenerator>(); var outbox = Substitute.For<IOutboxWriter>();
        var form = Guid.CreateVersion7(); var user = Guid.CreateVersion7();
        var writer = new WorkflowHumanActivityActivationWriter(command, ids, new(outbox));
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => writer.WriteVoteAsync(Guid.CreateVersion7(), "host", "vote",
            new(new(form, 100, 67, true, null, null, null), wrongForm ? [user] : [user, user]),
            new(Guid.CreateVersion7(), wrongForm ? Guid.CreateVersion7() : form, "{}", "hash", "active"),
            1, DateTimeOffset.UtcNow, null, "vote", "one", null, null, "{}", "hash", default));
        Assert.AreEqual(0, command.ReceivedCalls().Count()); Assert.AreEqual(0, outbox.ReceivedCalls().Count());
    }
}
