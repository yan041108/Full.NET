using System.Text.Json;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Workflow.Domain;
using Full.NET.Modules.Workflow.Features;
using Full.NET.Modules.Workflow.Persistence;
using NSubstitute;

namespace Full.NET.UnitTests.Workflow;

/// <summary>固定路径显式携带终点，真实End有绑定和因果且消耗活动预算，不能用布尔值猜测身份。</summary>
[TestClass]
public sealed class WorkflowEndActivationTests
{
    [TestMethod]
    [DataRow("left", "finish-left")]
    [DataRow("right", "finish-right")]
    public void Selected_terminal_identity_is_preserved_when_a_definition_has_multiple_ends(string value, string expected)
    {
        var draft = new WorkflowDefinitionDraft(1, [Node("start", "start", new { nextNodeKeys = new[] { "review" } }),
            Node("review", "human.approval", new { nextNodeKeys = new[] { "choice" } }),
            Node("choice", "gateway.exclusive", new { nextNodeKeys = new[] { "finish-left", "finish-right" }, defaultNextNodeKey = "finish-right",
                branches = new[] { new { branchKey = "left", nextNodeKey = "finish-left", condition = new { fieldKey = "title", @operator = "equals", value = "left" } } } }),
            Node("finish-left", "end", new { }), Node("finish-right", "end", new { })]);
        Assert.IsTrue(WorkflowRuntimePlan.TryCreate(draft, new(1, 1, [new("main", [new("title", "text", false, new Dictionary<string, JsonElement>())])]), out var plan));
        Assert.IsTrue(plan!.TryResolveAfterHuman("review", new Dictionary<string, JsonElement> { ["title"] = JsonSerializer.SerializeToElement(value) }, out var transition));
        Assert.IsTrue(transition.CompletesInstance); Assert.AreEqual(expected, transition.TerminalNodeKey);
        CollectionAssert.AreEqual(new[] { "choice" }, transition.AutomaticNodes.Select(node => node.NodeKey).ToArray());
    }

    [TestMethod]
    public async Task End_writer_creates_a_real_step_binding_and_exact_parent_before_the_cursor_advances()
    {
        var (writer, commands, ids) = Writer(); var source = Guid.CreateVersion7(); var trace = new WorkflowActivationTrace(source);
        var next = await writer.WriteAsync(Guid.CreateVersion7(), "host", Guid.CreateVersion7(), [new("finish", "end", [], null)], 7,
            DateTimeOffset.UtcNow, trace: trace);
        Assert.AreEqual(8L, next); Assert.AreNotEqual(source, trace.LastStepId);
        await commands.Received(1).ExecuteAsync(WorkflowEndSql.InsertStep, Arg.Is<object?>(p => Value(p, "NodeKey", "finish") && Value(p, "Id", trace.LastStepId)), Arg.Any<CancellationToken>());
        await commands.Received(1).ExecuteAsync(WorkflowActivationBindingSql.BindStep, Arg.Is<object?>(p => Value(p, "StepId", trace.LastStepId)), Arg.Any<CancellationToken>());
        await commands.Received(1).ExecuteAsync(WorkflowCausalitySql.InsertEdge,
            Arg.Is<object?>(p => Value(p, "StepId", trace.LastStepId) && Value(p, "ParentStepId", source)), Arg.Any<CancellationToken>());
        Assert.AreEqual(3, ids.ReceivedCalls().Count());
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Missing_source_or_failed_step_insert_cannot_leave_a_completed_end(bool storageFailure)
    {
        var (writer, commands, _) = Writer(); var source = Guid.CreateVersion7(); var trace = new WorkflowActivationTrace(source);
        if (storageFailure) commands.ExecuteAsync(WorkflowEndSql.InsertStep, Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(0);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => writer.WriteAsync(Guid.CreateVersion7(), "host", Guid.CreateVersion7(),
            [new("finish", "end", [], null)], 1, DateTimeOffset.UtcNow, trace: storageFailure ? trace : null));
        Assert.AreEqual(source, trace.LastStepId);
        await commands.DidNotReceive().ExecuteAsync(WorkflowActivationBindingSql.BindStep, Arg.Any<object?>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("review")]
    public async Task A_boolean_completion_without_a_valid_fixed_end_is_rejected_before_writes(string? terminal)
    {
        var f = Runtime();
        var result = await f.Executor.ExecuteAsync(f.Instance, new WorkflowManagementScope(null, "host", "host"), f.Instance.Id, f.Plan,
            new(null, true, [], terminalNodeKey: terminal), new Dictionary<string, JsonElement>(), 1, DateTimeOffset.UtcNow, f.Instance.StartedById,
            sourceStepId: Guid.CreateVersion7());
        Assert.IsFalse(result.IsSuccess);
        Assert.HasCount(0, f.Commands.ReceivedCalls().ToArray());
    }

    [TestMethod]
    public async Task Terminal_consumes_the_same_budget_and_defers_its_exact_machine_key_when_the_transaction_is_full()
    {
        var f = Runtime(); Assert.IsTrue(f.Plan.TryResolveAfterHuman("review", new Dictionary<string, JsonElement>(), out var transition));
        var source = Guid.CreateVersion7();
        var result = await f.Executor.ExecuteAsync(f.Instance, new(null, "host", "host"), f.Instance.Id, f.Plan, transition,
            new Dictionary<string, JsonElement>(), 1, DateTimeOffset.UtcNow, f.Instance.StartedById,
            budget: new() { Remaining = 0, TotalRemaining = 10 }, sourceStepId: source);
        Assert.IsTrue(result.IsSuccess); Assert.IsFalse(result.Value!.CompletesInstance);
        await f.Commands.Received(1).ExecuteAsync(WorkflowActivitySql.InsertWork,
            Arg.Is<object?>(p => Value(p, "NodeKey", "finish") && Value(p, "SourceStepId", source)), Arg.Any<CancellationToken>());
        await f.Commands.DidNotReceive().ExecuteAsync(WorkflowEndSql.InsertStep, Arg.Any<object?>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task Normal_root_completion_writes_the_selected_end_before_closing_epochs()
    {
        var f = Runtime(); Assert.IsTrue(f.Plan.TryResolveAfterHuman("review", new Dictionary<string, JsonElement>(), out var transition));
        var result = await f.Executor.ExecuteAsync(f.Instance, new(null, "host", "host"), f.Instance.Id, f.Plan, transition,
            new Dictionary<string, JsonElement>(), 1, DateTimeOffset.UtcNow, f.Instance.StartedById, sourceStepId: Guid.CreateVersion7());
        Assert.IsTrue(result.IsSuccess); Assert.IsTrue(result.Value!.CompletesInstance);
        await f.Commands.Received(1).ExecuteAsync(WorkflowEndSql.InsertStep, Arg.Is<object?>(p => Value(p, "NodeKey", "finish")), Arg.Any<CancellationToken>());
        await f.Commands.Received(1).ExecuteAsync(WorkflowExecutionEpochSql.CloseInstance, Arg.Any<object?>(), Arg.Any<CancellationToken>());
    }

    private static (WorkflowAutomaticTransitionWriter Writer, ICommandExecutor Commands, IIdGenerator Ids) Writer()
    {
        var query = Substitute.For<IQueryExecutor>(); var commands = Substitute.For<ICommandExecutor>(); var ids = Substitute.For<IIdGenerator>();
        commands.ExecuteAsync(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(1); ids.NewId().Returns(_ => Guid.CreateVersion7());
        return (new(commands, ids, new(query, commands, ids)), commands, ids);
    }
    private static RuntimeContext Runtime()
    {
        var root = Guid.CreateVersion7(); var actor = Guid.CreateVersion7(); var query = Substitute.For<IQueryExecutor>(); var commands = Substitute.For<ICommandExecutor>();
        var ids = Substitute.For<IIdGenerator>(); ids.NewId().Returns(_ => Guid.CreateVersion7()); commands.ExecuteAsync(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(1);
        var instance = new WorkflowInstanceRecord(root, null, "host", "host", Guid.CreateVersion7(), Guid.CreateVersion7(), "test", "end", null,
            "active", 1, actor, DateTimeOffset.UtcNow, null, null, null, null, null, null);
        var draft = new WorkflowDefinitionDraft(1, [Node("start", "start", new { nextNodeKeys = new[] { "review" } }),
            Node("review", "human.approval", new { nextNodeKeys = new[] { "finish" } }), Node("finish", "end", new { })]);
        Assert.IsTrue(WorkflowRuntimePlan.TryCreate(draft, out var plan));
        query.QuerySingleOrDefaultAsync<WorkflowFormSubmissionRecord>(WorkflowSql.FindFormSubmissionByInstance, Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(new WorkflowFormSubmissionRecord(Guid.CreateVersion7(), root, instance.FormVersionId!.Value, "{}", "internal", 2, actor, DateTimeOffset.UtcNow));
        return new(instance, plan!, WorkflowTodoManagementTestDependencies.CreateTransitionExecutor(query, commands, ids, Substitute.For<IOutboxWriter>()), commands);
    }
    private static bool Value(object? parameters, string key, object? expected) => parameters is IReadOnlyDictionary<string, object?> values && values.TryGetValue(key, out var value) && Equals(value, expected);
    private static WorkflowNodeDraft Node(string key, string kind, object config) => new(key, kind, 1, JsonSerializer.SerializeToElement(config));
    private sealed record RuntimeContext(WorkflowInstanceRecord Instance, WorkflowRuntimePlan Plan, WorkflowRuntimeTransitionExecutor Executor, ICommandExecutor Commands);
}
