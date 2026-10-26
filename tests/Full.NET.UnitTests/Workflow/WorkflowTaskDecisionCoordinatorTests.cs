using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Workflow.Domain;
using Full.NET.Modules.Workflow.Features;
using Full.NET.Modules.Workflow.Persistence;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Full.NET.UnitTests.Workflow;

/// <summary>办理提交只更新任务事实，顺序提交不提前激活其他成员，也不写审批票。</summary>
[TestClass]
public sealed class WorkflowTaskDecisionCoordinatorTests
{
    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer, "all", 1, 2, false)]
    [DataRow(DatabaseProvider.MySql, "all", 1, 2, false)]
    [DataRow(DatabaseProvider.SqlServer, "any", 1, 2, true)]
    [DataRow(DatabaseProvider.MySql, "any", 1, 2, true)]
    [DataRow(DatabaseProvider.SqlServer, "all", 3, 0, true)]
    public async Task Task_completion_uses_submission_counts_not_approval_votes(DatabaseProvider provider, string mode, int completed, int pending, bool expected)
    {
        var f = Create(provider, mode, new(completed, pending, 0)); var result = await f.Run();
        Assert.IsTrue(result.IsSuccess); Assert.AreEqual(expected, result.Value!.CompletesActivity);
        await f.Command.Received(expected ? 1 : 0).ExecuteAsync(WorkflowTaskSql.CompleteTask, Arg.Any<object?>(), Arg.Any<CancellationToken>());
        await f.Command.Received(expected ? 1 : 0).ExecuteAsync(WorkflowTaskSql.CancelMembers, Arg.Any<object?>(), Arg.Any<CancellationToken>());
        await f.Command.DidNotReceive().ExecuteAsync(WorkflowSql.DecideApprovalSlotWithRevision, Arg.Any<object?>(), Arg.Any<CancellationToken>());
        Assert.AreEqual(0, f.Outbox.ReceivedCalls().Count());
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    public async Task Task_state_cas_failure_cannot_close_other_members(int affected)
    {
        var f = Create(DatabaseProvider.SqlServer, "any", new(1, 2, 0));
        f.Command.ExecuteAsync(WorkflowTaskSql.CompleteTask, Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(affected);
        var result = await f.Run(); Assert.AreEqual(affected == 1, result.IsSuccess);
        await f.Command.Received(affected).ExecuteAsync(WorkflowTaskSql.CancelMemberTodos, Arg.Any<object?>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer)]
    [DataRow(DatabaseProvider.MySql)]
    public async Task Sequential_task_activates_only_the_next_frozen_member(DatabaseProvider provider)
    {
        var f = Create(provider, "sequential", new(1, 2, 0));
        f.Query.QuerySingleOrDefaultAsync<WorkflowPendingTaskMemberRecord>(provider == DatabaseProvider.SqlServer ? WorkflowTaskSql.FindNextSqlServer : WorkflowTaskSql.FindNextMySql,
            Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(new WorkflowPendingTaskMemberRecord(Guid.CreateVersion7(), Guid.CreateVersion7(), 2, 1));
        var result = await f.Run(); Assert.IsTrue(result.IsSuccess); Assert.IsNotNull(result.Value!.NextTodoId);
        await f.Command.Received(1).ExecuteAsync(WorkflowTaskSql.BindTodo, Arg.Any<object?>(), Arg.Any<CancellationToken>());
        await f.Command.Received(1).ExecuteAsync(WorkflowTodoViewSql.Capture, Arg.Any<object?>(), Arg.Any<CancellationToken>());
        Assert.AreEqual(1, f.Outbox.ReceivedCalls().Count());
    }

    [TestMethod]
    [DataRow("all", 0, 3, 0)]
    [DataRow("all", 1, 1, 1)]
    [DataRow("unknown", 1, 2, 0)]
    public async Task Missing_or_cancelled_member_facts_do_not_complete_a_running_task(string mode, int completed, int pending, int cancelled)
    {
        var f = Create(DatabaseProvider.SqlServer, mode, new(completed, pending, cancelled));
        Assert.IsFalse((await f.Run()).IsSuccess); Assert.AreEqual(0, f.Command.ReceivedCalls().Count());
    }

    private static Fixture Create(DatabaseProvider provider, string mode, WorkflowTaskTallyRecord counts)
    {
        var query = Substitute.For<IQueryExecutor>(); var command = Substitute.For<ICommandExecutor>();
        command.ExecuteAsync(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(1);
        var ids = Substitute.For<IIdGenerator>(); ids.NewId().Returns(_ => Guid.CreateVersion7()); var outbox = Substitute.For<IOutboxWriter>();
        var now = DateTimeOffset.UtcNow; var instance = new WorkflowInstanceRecord(Guid.CreateVersion7(), null, "host", "host", Guid.CreateVersion7(), Guid.CreateVersion7(),
            "test", "task", null, "active", 1, Guid.CreateVersion7(), now, null, null, null, null, null, null);
        var todo = new WorkflowTodoRuntimeRecord(Guid.CreateVersion7(), instance.Id, Guid.CreateVersion7(), Guid.CreateVersion7(), "active", now, null, null, 1,
            "task", 1, null, null, null, null, null, null, WorkflowHumanActivityKinds.Task);
        var task = new WorkflowTaskContextRecord(Guid.CreateVersion7(), todo.StepId, 1, 1, mode, 3, mode == "any" ? 1 : 3, 1);
        query.QuerySingleOrDefaultAsync<WorkflowTaskTallyRecord>(provider == DatabaseProvider.SqlServer ? WorkflowTaskSql.TallySqlServer : WorkflowTaskSql.TallyMySql,
            Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(counts);
        var coordinator = new WorkflowTaskDecisionCoordinator(query, command, new(command, ids, new(outbox)), Options.Create(new DatabaseOptions { Provider = provider }));
        return new(coordinator, query, command, outbox, instance, todo, task);
    }
    private sealed record Fixture(WorkflowTaskDecisionCoordinator Service, IQueryExecutor Query, ICommandExecutor Command, IOutboxWriter Outbox,
        WorkflowInstanceRecord Instance, WorkflowTodoRuntimeRecord Todo, WorkflowTaskContextRecord Task)
    {
        public Task<Full.NET.Abstractions.Results.Result<WorkflowTaskDecision>> Run() => Service.ResolveAsync(Task, Todo, Instance,
            new WorkflowManagementScope(null, "host", "host"), new Dictionary<string, System.Text.Json.JsonElement>(), null, DateTimeOffset.UtcNow, default);
    }
}
