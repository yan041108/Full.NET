using System.Text.Json;
using Full.NET.Abstractions.Ids;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Workflow.Domain;
using Full.NET.Modules.Workflow.Persistence;
using NSubstitute;

namespace Full.NET.UnitTests.Workflow;

/// <summary>未知选择不能退回默认边；已固化输入和唯一后继必须在同一根事务应用。</summary>
[TestClass]
public sealed class WorkflowRouteDecisionExecutorTests
{
    [TestMethod]
    public async Task Root_activity_budget_cannot_be_bypassed_by_the_dedicated_route_worker()
    {
        var data = Create();
        data.Frames.Query.QuerySingleOrDefaultAsync<int>(WorkflowActivitySql.CountActivations, Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(WorkflowActivityScheduler.InstanceActivityBudget);
        Assert.AreEqual("workflow.activity.budget_exhausted", (await Activate(data)).FailureKey);
        Assert.AreEqual(0, data.Commands.ReceivedCalls().Count());
    }

    [TestMethod]
    public async Task Exhausted_region_is_held_once_for_explicit_resolution_without_creating_another_round()
    {
        var data = Create(amount: 1);
        data.Frames.Query.QuerySingleOrDefaultAsync<int>(WorkflowRegionSql.NextIteration, Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(4);
        var result = await Activate(data);
        Assert.IsTrue(result.DecisionUnknown);
        Assert.AreEqual("workflow.activity.region_iteration_exhausted", result.FailureKey);
        Assert.IsTrue(Writes(data.Commands, WorkflowRouteSql.HoldSelection));
        Assert.IsTrue(Writes(data.Commands, WorkflowRouteSql.HoldStep));
        Assert.IsFalse(Writes(data.Commands, WorkflowRegionSql.InsertEpoch));
        Assert.IsFalse(Writes(data.Commands, WorkflowRouteSql.InsertWork));
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task Holding_a_rejected_selection_is_atomic(bool stepConflict)
    {
        var data = Create(amount: 1);
        data.Frames.Query.QuerySingleOrDefaultAsync<int>(WorkflowRegionSql.NextIteration, Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(4);
        data.Commands.ExecuteAsync(stepConflict ? WorkflowRouteSql.HoldStep : WorkflowRouteSql.HoldSelection, Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(0);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(async () => { _ = await Activate(data); });
    }

    [TestMethod]
    public async Task Unknown_selection_freezes_original_input_and_does_not_schedule_a_default()
    {
        var data = Create(unknown: true);
        var result = await Activate(data);
        Assert.IsTrue(result.DecisionUnknown);
        Assert.IsFalse(result.Applied);
        Assert.IsNull(result.FailureKey);
        var write = Parameters(data.Commands, WorkflowRouteSql.Insert);
        Assert.AreEqual("decision_unknown", write["StatusKey"]);
        Assert.AreEqual(data.Work.InputJson, write["InputJson"]);
        Assert.IsNull(write["NextWorkId"]);
        Assert.IsFalse(Writes(data.Commands, WorkflowRouteSql.InsertWork));
        Assert.IsFalse(Writes(data.Commands, WorkflowRegionSql.InsertEpoch));
    }

    [TestMethod]
    public async Task No_matching_rule_commits_the_published_normal_edge_and_one_receipted_work()
    {
        var data = Create();
        var result = await Activate(data);
        Assert.IsTrue(result.Applied, result.FailureKey ?? "decision_unknown");
        Assert.IsFalse(result.DecisionUnknown);
        Assert.AreEqual(data.Frames.Instance.Id, Parameters(data.Commands, WorkflowRouteSql.BindContinuation)["TargetEpochId"]);
        var work = Parameters(data.Commands, WorkflowRouteSql.InsertWork);
        Assert.AreEqual(data.Work.Id, work["RouteDecisionId"]);
        Assert.AreEqual(data.Work.Id, work["SourceStepId"]);
        Assert.AreEqual("region", work["CauseKindKey"]);
        Assert.AreEqual("end", work["NodeKey"]);
        Assert.AreEqual(1, data.Commands.ReceivedCalls().Count(call => Equals(call.GetArguments()[0], WorkflowRouteSql.MarkApplied)));
    }

    [TestMethod]
    public async Task Unknown_retry_does_not_reevaluate_changed_values_or_create_another_step()
    {
        var data = Create(unknown: true);
        Assert.IsTrue((await Activate(data)).DecisionUnknown);
        data.Commands.ClearReceivedCalls();
        Assert.IsTrue((await Activate(data)).DecisionUnknown);
        Assert.AreEqual(0, data.Commands.ReceivedCalls().Count());
    }

    [TestMethod]
    [DataRow("scope")]
    [DataRow("root")]
    [DataRow("epoch")]
    [DataRow("hash")]
    [DataRow("json")]
    [DataRow("suspended")]
    public async Task Invalid_activation_cannot_write_any_route_facts(string defect)
    {
        var data = Create();
        var work = defect switch
        {
            "scope" => data.Work with { TenantScopeKey = "tenant:other" },
            "root" => data.Work with { InstanceId = Guid.CreateVersion7() },
            "epoch" => data.Work with { ExecutionEpochId = Guid.CreateVersion7() },
            "hash" => data.Work with { InputHash = new string('0', 64) },
            "json" => data.Work with { InputJson = "{}" },
            _ => data.Work,
        };
        var instance = defect == "suspended" ? data.Frames.Instance with { StatusKey = "suspended" } : data.Frames.Instance;
        var result = await data.Executor.ActivateAsync(instance, work, data.Plan, data.Config, data.Values,
            new(instance.Id, null, null), 1, DateTimeOffset.UtcNow, default);
        Assert.IsNotNull(result.FailureKey);
        Assert.AreEqual(0, data.Commands.ReceivedCalls().Count());
    }

    [TestMethod]
    [DataRow("version")]
    [DataRow("policy")]
    [DataRow("inputRevision")]
    [DataRow("applied")]
    public async Task Existing_decision_must_match_its_original_activation(string defect)
    {
        var data = Create(unknown: true);
        Assert.IsTrue((await Activate(data)).DecisionUnknown);
        data.Frozen = defect switch
        {
            "version" => data.Frozen! with { DefinitionVersionId = Guid.CreateVersion7() },
            "policy" => data.Frozen! with { PolicyHash = "changed" },
            "inputRevision" => data.Frozen! with { InputFormRevision = 2 },
            _ => data.Frozen! with { StatusKey = "applied" },
        };
        data.Commands.ClearReceivedCalls();
        Assert.IsNotNull((await Activate(data)).FailureKey);
        Assert.AreEqual(0, data.Commands.ReceivedCalls().Count());
    }

    [TestMethod]
    [DataRow("step")]
    [DataRow("decision")]
    [DataRow("continuation")]
    [DataRow("work")]
    [DataRow("receipt")]
    public async Task Storage_conflict_requires_outer_transaction_rollback(string phase)
    {
        var data = Create();
        var statement = phase switch
        {
            "step" => WorkflowRouteSql.InsertStep,
            "decision" => WorkflowRouteSql.Insert,
            "continuation" => WorkflowRouteSql.BindContinuation,
            "work" => WorkflowRouteSql.InsertWork,
            _ => WorkflowRouteSql.MarkApplied,
        };
        data.Commands.ExecuteAsync(statement, Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(0);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(async () => { _ = await Activate(data); });
    }

    private static Task<WorkflowRouteApplication> Activate(RouteCase data) => data.Executor.ActivateAsync(data.Frames.Instance,
        data.Work, data.Plan, data.Config, data.Values, new(data.Frames.Instance.Id, null, null), 1, DateTimeOffset.UtcNow, default);
    private static bool Writes(ICommandExecutor command, SqlStatement sql) => command.ReceivedCalls().Any(call => Equals(call.GetArguments()[0], sql));
    private static IReadOnlyDictionary<string, object?> Parameters(ICommandExecutor command, SqlStatement sql) =>
        (IReadOnlyDictionary<string, object?>)command.ReceivedCalls().Single(call => Equals(call.GetArguments()[0], sql)).GetArguments()[1]!;

    private static RouteCase Create(bool unknown = false, int amount = 0)
    {
        var frames = WorkflowExecutionFrameScopeTests.Create();
        frames.Epochs.Remove(frames.Parent); frames.Epochs.Remove(frames.Leaf); frames.Frames.Clear();
        var commands = Substitute.For<ICommandExecutor>();
        commands.ExecuteAsync(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(1);
        var ids = Substitute.For<IIdGenerator>(); ids.NewId().Returns(_ => Guid.CreateVersion7());
        var condition = JsonSerializer.SerializeToElement(new { fieldKey = "amount", @operator = "greaterThan", value = 0 });
        var fields = WorkflowDynamicRoutePolicyTests.Schema().Sections.SelectMany(section => section.Fields).ToDictionary(field => field.FieldKey, StringComparer.Ordinal);
        Assert.IsTrue(WorkflowExclusiveGatewayConfiguration.TryReadCondition(condition, fields, out var compiledCondition));
        var target = new WorkflowRouteTarget("retry", "review", "round", 3, compiledCondition);
        var policy = new WorkflowDynamicRoutePolicy(unknown ? "decision_field" : "rules", unknown ? "choice" : null, [target]);
        var plan = new WorkflowDynamicRoutePlan(new Dictionary<string, WorkflowCompiledRoute>
        {
            ["route"] = new("end", policy, [new(target, true, new("review", "route", new[] { "review", "route" }))]),
        });
        var config = JsonSerializer.SerializeToElement(new { nodeName = "route" });
        var values = new Dictionary<string, JsonElement> { ["amount"] = JsonSerializer.SerializeToElement(amount) };
        var input = WorkflowActivityScheduler.SerializeInput(values);
        var work = new WorkflowActivityWorkRecord(Guid.CreateVersion7(), frames.Instance.Id, "host", "route", input,
            WorkflowRouteDecisionExecutor.Hash(input), 1, 1, null, null, 0, 0, frames.Instance.Id);
        var executor = new WorkflowRouteDecisionExecutor(frames.Query, commands, ids, new(commands, ids), new(frames.Query, commands), new(frames.Query, commands, ids));
        var data = new RouteCase(frames, commands, executor, work, plan, config, values);
        frames.Query.QuerySingleOrDefaultAsync<WorkflowRouteDecisionRecord>(WorkflowRouteSql.Find, Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(_ => data.Frozen);
        commands.ExecuteAsync(WorkflowRouteSql.Insert, Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            var p = (IReadOnlyDictionary<string, object?>)call.ArgAt<object>(1);
            data.Frozen = new(work.Id, frames.Instance.Id, "host", frames.Instance.Id, frames.Instance.DefinitionVersionId, "route", input,
                work.InputHash, 1, (string)p["PolicyHash"]!, (string)p["StatusKey"]!, (string?)p["SelectedTargetKey"], (string?)p["NextNodeKey"],
                null, (Guid?)p["NextWorkId"], null, null, null, null, 1, DateTimeOffset.UtcNow, null);
            return 1;
        });
        frames.Query.QuerySingleOrDefaultAsync<WorkflowRegionRouteSourceRecord>(WorkflowRegionSql.FindRouteSource, Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(new WorkflowRegionRouteSourceRecord(work.Id, frames.Instance.Id, frames.Instance.Id, "route", "control.route", "completed", null, null, frames.Instance.DefinitionVersionId));
        frames.Query.QuerySingleOrDefaultAsync<WorkflowFormSubmissionRecord>(WorkflowSql.FindFormSubmissionByInstance, Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(new WorkflowFormSubmissionRecord(Guid.CreateVersion7(), frames.Instance.Id, Guid.CreateVersion7(), input, "internal", 1, Guid.CreateVersion7(), DateTimeOffset.UtcNow));
        return data;
    }

    private sealed class RouteCase(WorkflowExecutionFrameScopeTests.FrameCase frames, ICommandExecutor commands, WorkflowRouteDecisionExecutor executor,
        WorkflowActivityWorkRecord work, WorkflowDynamicRoutePlan plan, JsonElement config, IReadOnlyDictionary<string, JsonElement> values)
    {
        public WorkflowExecutionFrameScopeTests.FrameCase Frames { get; } = frames;
        public ICommandExecutor Commands { get; } = commands;
        public WorkflowRouteDecisionExecutor Executor { get; } = executor;
        public WorkflowActivityWorkRecord Work { get; } = work;
        public WorkflowDynamicRoutePlan Plan { get; } = plan;
        public JsonElement Config { get; } = config;
        public IReadOnlyDictionary<string, JsonElement> Values { get; } = values;
        public WorkflowRouteDecisionRecord? Frozen { get; set; }
    }
}
