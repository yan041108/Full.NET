using System.Text.Json;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Results;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Workflow.Domain;
using Full.NET.Modules.Workflow.Features;
using Full.NET.Modules.Workflow.Persistence;
using NSubstitute;

namespace Full.NET.UnitTests.Workflow;

/// <summary>系统处置保持真实节点轨迹，不能制造个人待办；挂起输入和后台首次回执不能被终态清理破坏。</summary>
[TestClass]
public sealed class WorkflowAssigneeHandlingExecutionTests
{
    [TestMethod]
    [DataRow("tenant", false)]
    [DataRow("tenant", true)]
    [DataRow("kind", false)]
    [DataRow("kind", true)]
    [DataRow("anchor", false)]
    [DataRow("anchor", true)]
    public async Task A_foreign_scope_cannot_dispatch_even_with_an_already_bound_budget(string mismatch, bool bound)
    {
        var draft = new WorkflowDefinitionDraft(1,
            [new("start", "start", 1, JsonSerializer.SerializeToElement(new { nextNodeKeys = new[] { "review" } })),
             new("review", "human.approval", 1, JsonSerializer.SerializeToElement(new { nextNodeKeys = new[] { "end" } })),
             new("end", "end", 1, JsonSerializer.SerializeToElement(new { }))], "automation");
        Assert.IsTrue(WorkflowRuntimePlan.TryCreate(draft, out var plan)); Assert.IsTrue(plan!.TryResolveStart(out var start));
        var query = Substitute.For<IQueryExecutor>(); var command = Substitute.For<ICommandExecutor>(); var instance = Instance();
        var executor = WorkflowTodoManagementTestDependencies.CreateTransitionExecutor(query, command, Substitute.For<IIdGenerator>(), Substitute.For<IOutboxWriter>());
        query.ClearReceivedCalls(); command.ClearReceivedCalls();
        WorkflowManagementScope scope = mismatch switch { "tenant" => new(Guid.CreateVersion7(), "host", "host"),
            "kind" => new(null, "tenant", "host"), _ => new(null, "host", "tenant:foreign") };
        var budget = new WorkflowDispatchBudget { TotalRemaining = 16 };
        if (bound) Assert.IsTrue(budget.TryBindExecutionEpoch(instance.Id));
        var result = await executor.ExecuteAsync(instance, scope, instance.Id, plan, start, new Dictionary<string, JsonElement>(), 1,
            instance.StartedAtUtc, instance.StartedById, budget: budget);
        Assert.IsFalse(result.IsSuccess); Assert.AreEqual(0, query.ReceivedCalls().Count()); Assert.AreEqual(0, command.ReceivedCalls().Count());
    }

    [TestMethod]
    [DataRow("auto_pass", "completed")]
    [DataRow("skip", "completed")]
    [DataRow("auto_reject", "rejected")]
    [DataRow("suspend", "suspended")]
    public async Task System_outcome_has_no_fake_human_todo_and_preserves_its_actual_status(string action, string expected)
    {
        var draft = new WorkflowDefinitionDraft(1,
            [new("start", "start", 1, JsonSerializer.SerializeToElement(new { nextNodeKeys = new[] { "review" } })),
             new("review", "human.approval", 1, JsonSerializer.SerializeToElement(new { nextNodeKeys = new[] { "end" } })),
             new("end", "end", 1, JsonSerializer.SerializeToElement(new { }))], "automation");
        Assert.IsTrue(WorkflowRuntimePlan.TryCreate(draft, out var plan)); Assert.IsTrue(plan!.TryResolveStart(out var start));
        var query = Substitute.For<IQueryExecutor>(); var command = Substitute.For<ICommandExecutor>();
        command.ExecuteAsync(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(1);
        var ids = Substitute.For<IIdGenerator>(); ids.NewId().Returns(_ => Guid.CreateVersion7());
        var instance = Instance(); var workId = Guid.CreateVersion7();
        query.QuerySingleOrDefaultAsync<WorkflowFormSubmissionRecord>(WorkflowSql.FindFormSubmissionByInstance, Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(new WorkflowFormSubmissionRecord(Guid.CreateVersion7(), instance.Id, instance.FormVersionId!.Value, "{}", "internal", 7, instance.StartedById, instance.StartedAtUtc));
        var prepared = new WorkflowPreparedHumanActivities(new Dictionary<string, Result<WorkflowHumanActivityAssignees>>
        { ["review"] = Result<WorkflowHumanActivityAssignees>.Success(new(null, Guid.Empty, "{}", "hash", Handling: new(action, "empty"))) });
        var executor = WorkflowTodoManagementTestDependencies.CreateTransitionExecutor(query, command, ids, Substitute.For<IOutboxWriter>());
        var result = await executor.ExecuteAsync(instance, new(null, "host", "host"), instance.Id, plan, start, new Dictionary<string, JsonElement>(),
            3, instance.StartedAtUtc, instance.StartedById, budget: new() { CurrentWorkId = workId }, preparedAssignees: prepared,
            sourceStepId:Guid.CreateVersion7());
        Assert.IsTrue(result.IsSuccess); Assert.AreEqual(expected, result.Value!.InstanceStatusKey); Assert.IsNull(result.Value.FirstTodoId);
        Assert.AreEqual(expected == "completed" ? 5L : 4L, result.Value.NextExecutionSequence);
        await command.Received(expected == "completed" ? 1 : 0).ExecuteAsync(WorkflowEndSql.InsertStep,
            Arg.Is<object?>(value => Has(value, "NodeKey", "end")), Arg.Any<CancellationToken>());
        await command.Received(1).ExecuteAsync(WorkflowAssigneeHandlingSql.InsertStep,
            Arg.Is<object?>(value => Has(value, "NodeTypeKey", "human.approval") && Has(value, "NodeKey", "review")), Arg.Any<CancellationToken>());
        Assert.IsFalse(command.ReceivedCalls().Any(call => call.GetArguments()[0] is SqlStatement statement && statement.Name == "workflow.todo.insert"));
        if (action == "suspend")
            await command.Received(1).ExecuteAsync(WorkflowActivitySql.InsertWork,
                Arg.Is<object?>(value => Has(value, "NodeKey", "review") && Has(value, "InputFormRevision", 7L) && Has(value, "ExecutionSequence", 4L)), Arg.Any<CancellationToken>());
        if (action == "auto_reject")
            await command.Received(1).ExecuteAsync(WorkflowAssigneeHandlingSql.CancelOtherWork,
                Arg.Is<object?>(value => Has(value, "CurrentWorkId", workId)), Arg.Any<CancellationToken>());
    }

    private static bool Has(object? value, string key, object? expected) => value is IReadOnlyDictionary<string, object?> data && data.TryGetValue(key, out var actual) && Equals(actual, expected);
    private static WorkflowInstanceRecord Instance() => new(Guid.CreateVersion7(), null, "host", "host", Guid.CreateVersion7(), Guid.CreateVersion7(),
        "test", "one", null, "active", 1, Guid.CreateVersion7(), DateTimeOffset.UtcNow, null, null, null, null, null, null);
}
