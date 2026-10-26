using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Workflow.Contracts;
using Full.NET.Modules.Workflow.Domain;
using Full.NET.Modules.Workflow.Features;
using Full.NET.Modules.Workflow.Persistence;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Full.NET.UnitTests.Workflow;

/// <summary>当前组的个人赞成票不能越过组内门槛，也不能直接计入组间门槛。</summary>
[TestClass]
public sealed class WorkflowGroupApprovalDecisionCoordinatorTests
{
    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer)]
    [DataRow(DatabaseProvider.MySql)]
    public async Task Individual_votes_above_root_threshold_do_not_approve_an_unfinished_group(DatabaseProvider provider)
    {
        var f = Create(provider, "all", 5, 10, new(3, 0, 7), new(0, 0, 2));
        var result = await f.Run();
        Assert.IsTrue(result.IsSuccess); Assert.AreEqual(0L, result.Value!.Tally.ApprovedCount);
        Assert.AreEqual(2, result.Value.GroupCount); Assert.IsNull(result.Value.NextTodoId);
        await f.Command.DidNotReceive().ExecuteAsync(WorkflowGroupApprovalSql.CompleteGroup, Arg.Any<object?>(), Arg.Any<CancellationToken>());
        Assert.AreEqual(0, f.Outbox.ReceivedCalls().Count());
    }

    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer)]
    [DataRow(DatabaseProvider.MySql)]
    public async Task Completed_any_group_cancels_only_its_remaining_members_and_counts_one_group(DatabaseProvider provider)
    {
        var f = Create(provider, "any", 1, 3, new(1, 0, 2), new(1, 0, 1));
        var result = await f.Run();
        Assert.IsTrue(result.IsSuccess); Assert.AreEqual(1L, result.Value!.Tally.ApprovedCount);
        await f.Command.Received(1).ExecuteAsync(WorkflowGroupApprovalSql.CompleteGroup,
            Arg.Is<object?>(p => Has(p, "StatusKey", "approved") && Has(p, "GroupId", f.Group.Id)), Arg.Any<CancellationToken>());
        await f.Command.Received(1).ExecuteAsync(WorkflowGroupApprovalSql.CancelGroupTodos,
            Arg.Is<object?>(p => Has(p, "GroupId", f.Group.Id)), Arg.Any<CancellationToken>());
        await f.Command.Received(1).ExecuteAsync(WorkflowGroupApprovalSql.CancelGroupSlots, Arg.Any<object?>(), Arg.Any<CancellationToken>());
        await f.Command.DidNotReceive().ExecuteAsync(WorkflowSql.CancelPendingApprovalTodosByStep, Arg.Any<object?>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    public async Task Group_state_cas_failure_stops_cleanup_and_next_activation(int affected)
    {
        var f = Create(DatabaseProvider.SqlServer, "all", 2, 2, new(2, 0, 0), new(1, 0, 1));
        f.Command.ExecuteAsync(WorkflowGroupApprovalSql.CompleteGroup, Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(affected);
        var result = await f.Run(); Assert.AreEqual(affected == 1, result.IsSuccess);
        await f.Command.Received(affected).ExecuteAsync(WorkflowGroupApprovalSql.CancelGroupSlots, Arg.Any<object?>(), Arg.Any<CancellationToken>());
        Assert.AreEqual(0, f.Outbox.ReceivedCalls().Count());
    }

    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer)]
    [DataRow(DatabaseProvider.MySql)]
    public async Task Sequential_group_activates_its_next_frozen_member_and_no_other_group(DatabaseProvider provider)
    {
        var f = Create(provider, "sequential", 3, 3, new(1, 0, 2), new(0, 0, 2));
        var next = new WorkflowPendingGroupApprovalSlotRecord(Guid.CreateVersion7(), Guid.CreateVersion7(), 4, 1, f.Group.Id);
        f.Query.QuerySingleOrDefaultAsync<WorkflowPendingGroupApprovalSlotRecord>(provider == DatabaseProvider.SqlServer ? WorkflowGroupApprovalSql.FindNextSqlServer : WorkflowGroupApprovalSql.FindNextMySql,
            Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(next);
        var result = await f.Run(); Assert.IsTrue(result.IsSuccess); Assert.IsNotNull(result.Value!.NextTodoId);
        await f.Command.Received(1).ExecuteAsync(WorkflowGroupApprovalSql.BindTodo,
            Arg.Is<object?>(p => Has(p, "GroupId", f.Group.Id) && Has(p, "Id", next.Id)), Arg.Any<CancellationToken>());
        Assert.AreEqual(1, f.Outbox.ReceivedCalls().Count());
    }

    [TestMethod]
    [DataRow("other_active")]
    [DataRow("missing")]
    [DataRow("other_group")]
    [DataRow("failed_bind")]
    public async Task Invalid_next_group_activation_returns_failure_without_input_or_notification(string scenario)
    {
        var f = Create(DatabaseProvider.SqlServer, "sequential", 3, 3, new(1, 0, 2), new(0, 0, 2));
        if (scenario == "other_active") f.Query.QuerySingleOrDefaultAsync<int>(WorkflowGroupApprovalSql.CountActiveTodos, Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(1);
        if (scenario != "missing") f.Query.QuerySingleOrDefaultAsync<WorkflowPendingGroupApprovalSlotRecord>(WorkflowGroupApprovalSql.FindNextSqlServer,
            Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(new WorkflowPendingGroupApprovalSlotRecord(Guid.CreateVersion7(), Guid.CreateVersion7(), 3, 1,
                scenario == "other_group" ? Guid.CreateVersion7() : f.Group.Id));
        if (scenario == "failed_bind") f.Command.ExecuteAsync(WorkflowGroupApprovalSql.BindTodo, Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(0);
        var result = await f.Run(); Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(0, f.Outbox.ReceivedCalls().Count());
        await f.Command.DidNotReceive().ExecuteAsync(WorkflowTodoViewSql.Capture, Arg.Any<object?>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task Root_already_converging_does_not_create_a_late_sequential_todo()
    {
        var f = Create(DatabaseProvider.SqlServer, "sequential", 3, 3, new(1, 0, 2), new(1, 0, 1), rootRequired: 1);
        var result = await f.Run(); Assert.IsTrue(result.IsSuccess); Assert.IsNull(result.Value!.NextTodoId);
        await f.Command.DidNotReceive().ExecuteAsync(WorkflowSql.InsertTodo, Arg.Any<object?>(), Arg.Any<CancellationToken>());
    }

    private static Fixture Create(DatabaseProvider provider, string mode, int required, int count,
        WorkflowApprovalTallyRecord members, WorkflowApprovalTallyRecord groups, int rootRequired = 2)
    {
        var query = Substitute.For<IQueryExecutor>(); var command = Substitute.For<ICommandExecutor>();
        command.ExecuteAsync(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(1);
        var instance = new WorkflowInstanceRecord(Guid.CreateVersion7(), null, "host", "host", Guid.CreateVersion7(), Guid.CreateVersion7(),
            "test", "group", null, "active", 1, Guid.CreateVersion7(), DateTimeOffset.UtcNow, null, null, null, null, null, null);
        var todo = new WorkflowTodoRuntimeRecord(Guid.CreateVersion7(), instance.Id, Guid.CreateVersion7(), Guid.CreateVersion7(), "active", DateTimeOffset.UtcNow,
            null, null, 1, "review", 1, "group", rootRequired, count + 1, null, null, null);
        var group = new WorkflowApprovalGroupContextRecord(Guid.CreateVersion7(), "one", null, mode, required, count, 1, 2);
        query.QuerySingleOrDefaultAsync<WorkflowApprovalGroupContextRecord>(WorkflowGroupApprovalSql.FindForTodo, Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(group);
        query.QuerySingleOrDefaultAsync<WorkflowApprovalTallyRecord>(provider == DatabaseProvider.SqlServer ? WorkflowGroupApprovalSql.TallySlotsSqlServer : WorkflowGroupApprovalSql.TallySlotsMySql,
            Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(members);
        query.QuerySingleOrDefaultAsync<WorkflowApprovalTallyRecord>(provider == DatabaseProvider.SqlServer ? WorkflowGroupApprovalSql.TallyGroupsSqlServer : WorkflowGroupApprovalSql.TallyGroupsMySql,
            Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(groups);
        var ids = Substitute.For<IIdGenerator>(); ids.NewId().Returns(_ => Guid.CreateVersion7());
        var outbox = Substitute.For<IOutboxWriter>();
        var service = new WorkflowGroupApprovalDecisionCoordinator(query, command, new(command, ids, new(outbox)), Options.Create(new DatabaseOptions { Provider = provider }));
        return new(service, query, command, outbox, instance, todo, group);
    }
    private static bool Has(object? p, string key, object expected) => p is IReadOnlyDictionary<string, object?> values && values.TryGetValue(key, out var value) && Equals(value, expected);
    private sealed record Fixture(WorkflowGroupApprovalDecisionCoordinator Service, IQueryExecutor Query, ICommandExecutor Command,
        IOutboxWriter Outbox, WorkflowInstanceRecord Instance, WorkflowTodoRuntimeRecord Todo, WorkflowApprovalGroupContextRecord Group)
    {
        public Task<Full.NET.Abstractions.Results.Result<WorkflowGroupedApprovalDecision>> Run() =>
            Service.ResolveAsync(Todo, Instance, new WorkflowManagementScope(null, "host", "host"),
                new Dictionary<string, System.Text.Json.JsonElement>(), null, DateTimeOffset.UtcNow, CancellationToken.None);
    }
}
