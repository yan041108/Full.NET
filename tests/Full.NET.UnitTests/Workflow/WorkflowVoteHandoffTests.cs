using System.Text.Json;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Workflow.Contracts;
using Full.NET.Modules.Workflow.Domain;
using Full.NET.Modules.Workflow.Persistence;
using NSubstitute;

namespace Full.NET.UnitTests.Workflow;

/// <summary>投票转办只重绑同一张待投选票；历史、固定分母和截止均保持真实。</summary>
[TestClass]
public sealed class WorkflowVoteHandoffTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Handoff_keeps_ballot_identity_and_captures_a_new_private_view(bool administrator)
    {
        var f = Create(); var actor = administrator ? Guid.CreateVersion7() : f.Todo.AssigneeUserId;
        var authority = administrator ? WorkflowTodoHandoffAuthority.AdministrativeReassignment : WorkflowTodoHandoffAuthority.PersonalTransfer;
        var plan = await f.Writer.ResolveAsync(f.Todo, f.Instance, new(null, "host", "host"), actor, Guid.CreateVersion7(), authority, default);
        Assert.IsTrue(plan.IsSuccess, plan.Error?.Code); Assert.AreEqual(f.Ballot.Id, plan.Value!.VoteBallot!.Id);
        Assert.IsNull(plan.Value.ApprovalSlot); Assert.IsNull(plan.Value.TaskMember);
        var next = await f.Writer.WriteAsync(plan.Value, f.Todo, f.Instance, new(null, "host", "host"), new Dictionary<string, JsonElement>(), 3, f.Now, default);
        Assert.IsTrue(next.IsSuccess, next.Error?.Code);
        await f.Commands.Received(1).ExecuteAsync(WorkflowVoteSql.TransferBallot,
            Arg.Is<object?>(p => Has(p, "Id", f.Ballot.Id) && Has(p, "NewTodoId", next.Value)), Arg.Any<CancellationToken>());
        await f.Commands.Received(1).ExecuteAsync(WorkflowVoteSql.CaptureView,
            Arg.Is<object?>(p => Has(p, "TodoId", next.Value)), Arg.Any<CancellationToken>());
        await f.Commands.Received(1).ExecuteAsync(WorkflowTodoTransferSql.Insert,
            Arg.Is<object?>(p => Has(p, "VoteBallotId", f.Ballot.Id) && Has(p, "ActorUserId", actor) &&
                Has(p, "AuthorizationKey", administrator ? WorkflowPermissions.InstancesRecover : WorkflowPermissions.TodosTransfer)), Arg.Any<CancellationToken>());
        await f.Commands.DidNotReceive().ExecuteAsync(WorkflowVoteSql.InsertBallot, Arg.Any<object?>(), Arg.Any<CancellationToken>());
        await f.Commands.DidNotReceive().ExecuteAsync(WorkflowVoteSql.SetViewOutput, Arg.Any<object?>(), Arg.Any<CancellationToken>());
        await f.Commands.DidNotReceive().ExecuteAsync(WorkflowTodoTransferSql.RebindSlot, Arg.Any<object?>(), Arg.Any<CancellationToken>());
        Assert.AreEqual(1, f.Outbox.ReceivedCalls().Count());
    }

    [TestMethod]
    public async Task Existing_candidate_including_an_already_submitted_member_cannot_receive_second_ballot()
    {
        var f = Create();
        f.Query.QuerySingleOrDefaultAsync<int>(WorkflowVoteSql.CountOtherMember, Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(1);
        var result = await f.Writer.ResolveAsync(f.Todo, f.Instance, new(null, "host", "host"), Guid.CreateVersion7(), Guid.CreateVersion7(),
            WorkflowTodoHandoffAuthority.AdministrativeReassignment, default);
        Assert.AreEqual(WorkflowErrorCodes.TodoAssigneeUnchanged, result.Error?.Code);
        Assert.AreEqual(0, f.Commands.ReceivedCalls().Count());
    }

    [TestMethod]
    public async Task Deadline_reached_after_planning_prevents_all_writes()
    {
        var f = Create();
        var plan = await f.Writer.ResolveAsync(f.Todo, f.Instance, new(null, "host", "host"), f.Todo.AssigneeUserId, Guid.CreateVersion7(),
            WorkflowTodoHandoffAuthority.PersonalTransfer, default);
        Assert.IsTrue(plan.IsSuccess);
        var result = await f.Writer.WriteAsync(plan.Value!, f.Todo, f.Instance, new(null, "host", "host"),
            new Dictionary<string, JsonElement>(), 3, f.Now.AddMinutes(1), default);
        Assert.AreEqual(WorkflowErrorCodes.RevisionConflict, result.Error?.Code);
        Assert.AreEqual(0, f.Commands.ReceivedCalls().Count());
    }

    [TestMethod]
    public async Task Submitted_ballot_is_not_reassigned_as_an_approval_fallback()
    {
        var f = Create();
        f.Query.QuerySingleOrDefaultAsync<WorkflowVoteBallotRecord>(WorkflowVoteSql.FindBallot, Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(f.Ballot with { StatusKey = "submitted", ChoiceKey = "yes" });
        var result = await f.Writer.ResolveAsync(f.Todo, f.Instance, new(null, "host", "host"), f.Todo.AssigneeUserId, Guid.CreateVersion7(),
            WorkflowTodoHandoffAuthority.PersonalTransfer, default);
        Assert.IsFalse(result.IsSuccess);
        await f.Query.DidNotReceive().QuerySingleOrDefaultAsync<WorkflowApprovalSlotRecord>(WorkflowSql.FindApprovalSlotForReassignment,
            Arg.Any<object?>(), Arg.Any<CancellationToken>());
    }

    private static bool Has(object? value, string key, object? expected) => value is IReadOnlyDictionary<string, object?> parameters &&
        parameters.TryGetValue(key, out var actual) && Equals(actual, expected);
    private static Fixture Create()
    {
        var now = DateTimeOffset.UtcNow; var instanceId = Guid.CreateVersion7(); var step = Guid.CreateVersion7(); var actor = Guid.CreateVersion7();
        var todo = new WorkflowTodoRuntimeRecord(Guid.CreateVersion7(), instanceId, step, actor, "active", now,
            null, null, 1, "vote", 3, null, null, null, null, null, null) { NodeTypeKey = WorkflowHumanActivityKinds.Vote };
        var instance = new WorkflowInstanceRecord(instanceId, null, "host", "host", Guid.CreateVersion7(), Guid.CreateVersion7(),
            "vote", "one", null, "active", 2, actor, now, null, null, null, null, null, null);
        var ballot = new WorkflowVoteBallotRecord(Guid.CreateVersion7(), step, todo.Id, actor, "pending", null, "{}", "{}", 1);
        var query = Substitute.For<IQueryExecutor>(); var commands = Substitute.For<ICommandExecutor>();
        commands.ExecuteAsync(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(1);
        query.QuerySingleOrDefaultAsync<WorkflowVoteBallotRecord>(WorkflowVoteSql.FindBallot, Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(ballot);
        query.QuerySingleOrDefaultAsync<WorkflowVoteRoundRecord>(WorkflowVoteSql.FindRound, Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(new WorkflowVoteRoundRecord(step, instanceId, Guid.CreateVersion7(), 3, 100, 67, true, now.AddMinutes(1), "active", 1, null, null, null, null));
        var ids = Substitute.For<IIdGenerator>(); ids.NewId().Returns(_ => Guid.CreateVersion7()); var outbox = Substitute.For<IOutboxWriter>();
        var notifications = new WorkflowNotificationOutboxPublisher(outbox);
        return new(new(query, commands, ids, new(commands, ids, notifications), notifications), query, commands, outbox, todo, instance, ballot, now);
    }
    private sealed record Fixture(WorkflowTodoHandoffWriter Writer, IQueryExecutor Query, ICommandExecutor Commands,
        IOutboxWriter Outbox, WorkflowTodoRuntimeRecord Todo, WorkflowInstanceRecord Instance, WorkflowVoteBallotRecord Ballot, DateTimeOffset Now);
}
