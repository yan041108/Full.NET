using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Full.NET.Abstractions.Ids;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Workflow.Domain;
using Full.NET.Modules.Workflow.Persistence;
using NSubstitute;

namespace Full.NET.UnitTests.Workflow;

/// <summary>真实来源、固定区域和唯一收据共同授权入出代次；拒绝路径不留下半个新尝试。</summary>
[TestClass]
public sealed class WorkflowReturnRoundCoordinatorTests
{
    [TestMethod]
    public async Task A_whole_reopen_records_the_closed_original_source_epoch_and_the_surviving_parent_separately()
    {
        var f = Fixture(); var fork = Guid.CreateVersion7(); var child = Guid.CreateVersion7();
        var draft = new WorkflowDefinitionDraft(1, [Node("start", "start", "target"), Node("target", "human.approval", "fork"),
            new("fork", "gateway.parallel", 1, JsonSerializer.SerializeToElement(new { gatewayRoleKey = "fork", joinNodeKey = "join", branches = new[] {
                new { branchKey = "left", nextNodeKey = "source" }, new { branchKey = "right", nextNodeKey = "other" } } })),
            Node("source", "human.approval", "join"), Node("other", "human.approval", "join"),
            new("join", "gateway.parallel", 1, JsonSerializer.SerializeToElement(new { gatewayRoleKey = "join", forkNodeKey = "fork", nextNodeKeys = new[] { "end" } })),
            Node("end", "end")]);
        Assert.IsTrue(WorkflowRuntimePlan.TryCreate(draft, out var plan));
        Assert.IsTrue(plan!.TryDescribeReturnRegion("target", "source", out var region, out var whole)); Assert.IsTrue(whole);
        f.Query.QuerySingleOrDefaultAsync<WorkflowRegionRouteSourceRecord>(WorkflowReturnRoundSql.FindStep, Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(f.Source with { ExecutionEpochId = child, ParallelJoinId = fork, ParallelBranchKey = "left" });
        var selected = new WorkflowReturnScopeSelection(region!, f.Instance.Id, null, null, [fork], [f.Source.Id], [f.Round.TargetStepId], "end", false, []);
        var result = await f.Coordinator.EnterAsync(f.Instance, plan, f.Source.Id, f.Round.TargetStepId, "target", f.Round.TargetSnapshotHash,
            new Dictionary<string, JsonElement>(), 3, 4, f.Now, default, selected);
        Assert.IsTrue(result.IsSuccess);
        await f.Commands.Received(1).ExecuteAsync(WorkflowReturnRoundSql.Insert,
            Arg.Is<object?>(p => Value(p, "ParentEpochId", f.Instance.Id) && Value(p, "EntrySourceEpochId", child) && Value(p, "SourceParallelJoinId", fork)), Arg.Any<CancellationToken>());
        await f.Commands.Received(1).ExecuteAsync(WorkflowRegionSql.InsertFrame,
            Arg.Is<object?>(p => p is IReadOnlyDictionary<string, object?> && ((IReadOnlyDictionary<string, object?>)p)["ParentForkActivationId"] == null), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task Return_before_an_exclusive_branch_freezes_the_common_end_as_the_round_exit()
    {
        var f = Fixture();
        var draft = new WorkflowDefinitionDraft(1, [Node("start", "start", "target"), Node("target", "human.approval", "choice"),
            new("choice", "gateway.exclusive", 1, JsonSerializer.SerializeToElement(new { nextNodeKeys = new[] { "source", "other" }, defaultNextNodeKey = "other",
                branches = new[] { new { branchKey = "selected", nextNodeKey = "source", condition = new { fieldKey = "title", @operator = "equals", value = "left" } } } })),
            Node("source", "human.approval", "end"), Node("other", "human.approval", "end"), Node("end", "end")]);
        Assert.IsTrue(WorkflowRuntimePlan.TryCreate(draft, new(1, 1, [new("main", [new("title", "text", false, new Dictionary<string, JsonElement>())])]), out var plan));
        var result = await EnterAsync(f with { Plan = plan! });
        Assert.IsTrue(result.IsSuccess);
        await f.Commands.Received(1).ExecuteAsync(WorkflowRegionSql.InsertFrame,
            Arg.Is<object?>(p => Value(p, "EntryNodeKey", "target") && Value(p, "ExitNodeKey", "end")), Arg.Any<CancellationToken>());
        await f.Commands.Received(1).ExecuteAsync(WorkflowReturnRoundSql.Insert,
            Arg.Is<object?>(p => Value(p, "NextNodeKey", "end")), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task A_real_private_end_closes_the_round_and_queues_one_parent_end_without_completing_the_root()
    {
        var f = Fixture(); var json = "[\"end\",\"source\",\"target\"]";
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
        var round = f.Round with { ExitNodeKey = "end", RegionNodesJson = json, RegionHash = hash };
        f.Query.QuerySingleOrDefaultAsync<WorkflowReturnRoundRecord>(WorkflowReturnRoundSql.Find, Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(round);
        f.Query.QuerySingleOrDefaultAsync<WorkflowRegionRouteSourceRecord>(WorkflowReturnRoundSql.FindStep, Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(f.Source with { Id = f.ExitStepId, ExecutionEpochId = round.Id, NodeKey = "end", NodeTypeKey = "end", StatusKey = "completed" });
        f.Query.QuerySingleOrDefaultAsync<WorkflowRegionFrameRecord>(WorkflowRegionSql.FindFrame, Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(new WorkflowRegionFrameRecord(round.Id, f.Instance.Id, "host", f.Instance.DefinitionVersionId, "target", "end", null, null, f.Now));
        f.Query.QuerySingleOrDefaultAsync<WorkflowExecutionEpochRecord>(WorkflowExecutionEpochSql.Find, Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            var values = (IReadOnlyDictionary<string, object?>)call.ArgAt<object>(1); var epoch = (Guid)values["ExecutionEpochId"]!;
            return epoch == f.Instance.Id ? new WorkflowExecutionEpochRecord(epoch, epoch, "host", null, null, "root", 0, 0, "active", 1, f.Now, null)
                : new WorkflowExecutionEpochRecord(round.Id, f.Instance.Id, "host", f.Instance.Id, f.Source.Id, "return." + hash[..40], 1, 1, "active", 1, f.Now, null);
        });
        var result = await ExitAsync(f);
        Assert.IsTrue(result!.IsSuccess); Assert.IsFalse(result.Value!.CompletesInstance);
        await f.Commands.Received(1).ExecuteAsync(WorkflowReturnRoundSql.InsertWork,
            Arg.Is<object?>(p => Value(p, "NodeKey", "end") && Value(p, "SourceStepId", f.ExitStepId) && Value(p, "ExecutionEpochId", f.Instance.Id)), Arg.Any<CancellationToken>());
        await f.Commands.DidNotReceive().ExecuteAsync(WorkflowSql.CompleteInstanceWithRevision, Arg.Any<object?>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task Entry_creates_a_new_epoch_and_exact_work_without_reusing_the_original_target()
    {
        var f = Fixture();
        var result = await EnterAsync(f);
        Assert.IsTrue(result.IsSuccess);
        Assert.AreNotEqual(f.Instance.Id, result.Value);
        await f.Commands.Received(1).ExecuteAsync(WorkflowRegionSql.InsertEpoch,
            Arg.Is<object?>(p => Value(p, "CauseStepId", f.Source.Id) && Value(p, "ParentEpochId", f.Instance.Id)), Arg.Any<CancellationToken>());
        await f.Commands.Received(1).ExecuteAsync(WorkflowReturnRoundSql.Insert,
            Arg.Is<object?>(p => Value(p, "TargetStepId", f.Round.TargetStepId) && Value(p, "TargetSnapshotHash", f.Round.TargetSnapshotHash)), Arg.Any<CancellationToken>());
        await f.Commands.Received(1).ExecuteAsync(WorkflowReturnRoundSql.InsertWork,
            Arg.Is<object?>(p => Value(p, "ExecutionEpochId", result.Value) && Value(p, "NodeKey", "target") && Value(p, "InputFormRevision", 3L)), Arg.Any<CancellationToken>());
        await f.Commands.DidNotReceive().ExecuteAsync(WorkflowActivitySql.InsertWork, Arg.Any<object?>(), Arg.Any<CancellationToken>());
        await f.Commands.DidNotReceive().ExecuteAsync(WorkflowSql.InsertTodo, Arg.Any<object?>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    [DataRow("sourceStatus")]
    [DataRow("sourceRoot")]
    [DataRow("sourceVersion")]
    [DataRow("sourceKind")]
    [DataRow("missingSource")]
    [DataRow("missingTarget")]
    [DataRow("snapshotHash")]
    [DataRow("formRevision")]
    [DataRow("sequence")]
    [DataRow("iterationZero")]
    [DataRow("iterationLimit")]
    [DataRow("activeRegion")]
    [DataRow("activityBudget")]
    [DataRow("missingEpoch")]
    public async Task Invalid_entry_is_rejected_before_any_epoch_or_receipt_is_written(string defect)
    {
        var f = Fixture();
        var source = defect switch
        {
            "sourceStatus" => f.Source with { StatusKey = "active" },
            "sourceRoot" => f.Source with { InstanceId = Guid.CreateVersion7() },
            "sourceVersion" => f.Source with { DefinitionVersionId = Guid.CreateVersion7() },
            "sourceKind" => f.Source with { NodeTypeKey = "end" },
            _ => f.Source
        };
        f.Query.QuerySingleOrDefaultAsync<WorkflowRegionRouteSourceRecord>(WorkflowReturnRoundSql.FindStep, Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(defect == "missingSource" ? null : source);
        if (defect is "iterationZero" or "iterationLimit") f.Query.QuerySingleOrDefaultAsync<int>(WorkflowRegionSql.NextIteration, Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(defect == "iterationZero" ? 0 : 33);
        if (defect == "activeRegion") f.Query.QuerySingleOrDefaultAsync<int>(WorkflowRegionSql.CountActiveRegion, Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(1);
        if (defect == "activityBudget") f.Query.QuerySingleOrDefaultAsync<int>(WorkflowActivitySql.CountActivations, Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(4096);
        if (defect == "missingEpoch") f.Query.QuerySingleOrDefaultAsync<WorkflowExecutionEpochRecord>(WorkflowExecutionEpochSql.Find, Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns((WorkflowExecutionEpochRecord?)null);
        var result = await f.Coordinator.EnterAsync(f.Instance, f.Plan, f.Source.Id, f.Round.TargetStepId,
            defect == "missingTarget" ? "missing" : "target", defect == "snapshotHash" ? "bad" : f.Round.TargetSnapshotHash,
            new Dictionary<string, JsonElement>(), defect == "formRevision" ? 0 : 3, defect == "sequence" ? 0 : 4, f.Now, default);
        Assert.IsFalse(result.IsSuccess);
        Assert.HasCount(0, f.Commands.ReceivedCalls().ToArray());
    }

    [TestMethod]
    [DataRow("epoch")]
    [DataRow("frame")]
    [DataRow("round")]
    [DataRow("work")]
    public async Task Partial_storage_failure_throws_so_the_root_transaction_must_roll_back(string stage)
    {
        var f = Fixture();
        var statement = stage switch { "epoch" => WorkflowRegionSql.InsertEpoch, "frame" => WorkflowRegionSql.InsertFrame,
            "round" => WorkflowReturnRoundSql.Insert, _ => WorkflowReturnRoundSql.InsertWork };
        f.Commands.ExecuteAsync(statement, Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(0);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => EnterAsync(f));
        await f.Commands.DidNotReceive().ExecuteAsync(WorkflowSql.InsertExecutionLog, Arg.Any<object?>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task Actual_completed_exit_exports_only_this_round_and_queues_the_original_parent_successor()
    {
        var f = Fixture(); SetExit(f);
        var result = await ExitAsync(f);
        Assert.IsTrue(result!.IsSuccess);
        Assert.IsNull(result.Value!.FirstTodoId);
        await f.Commands.Received(2).ExecuteAsync(WorkflowActivationBindingSql.Delete, Arg.Any<object?>(), Arg.Any<CancellationToken>());
        await f.Commands.Received(1).ExecuteAsync(WorkflowReturnRoundSql.BindExit,
            Arg.Is<object?>(p => Value(p, "ExitStepId", f.ExitStepId) && Value(p, "ExitSourceEpochId", f.Round.Id)), Arg.Any<CancellationToken>());
        await f.Commands.Received(1).ExecuteAsync(WorkflowRegionSql.CloseEpoch, Arg.Any<object?>(), Arg.Any<CancellationToken>());
        await f.Commands.Received(1).ExecuteAsync(WorkflowReturnRoundSql.InsertWork,
            Arg.Is<object?>(p => Value(p, "ExecutionEpochId", f.Instance.Id) && Value(p, "NodeKey", "end") && Value(p, "InputFormRevision", 3L)), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    [DataRow("hash")]
    [DataRow("next")]
    [DataRow("revision")]
    [DataRow("sourceStatus")]
    [DataRow("sourceEpoch")]
    [DataRow("sourceRoot")]
    [DataRow("frame")]
    [DataRow("children")]
    [DataRow("unsettled")]
    public async Task Exit_does_not_relabel_an_unproven_source_or_close_an_unsettled_round(string defect)
    {
        var f = Fixture(); SetExit(f);
        var round = defect switch { "hash" => f.Round with { RegionHash = new string('b', 64) }, "next" => f.Round with { NextNodeKey = "target" },
            "revision" => f.Round with { Revision = 2 }, _ => f.Round };
        f.Query.QuerySingleOrDefaultAsync<WorkflowReturnRoundRecord>(WorkflowReturnRoundSql.Find, Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(round);
        var source = f.Source with { Id = f.ExitStepId, ExecutionEpochId = f.Round.Id, StatusKey = "completed" };
        source = defect switch { "sourceStatus" => source with { StatusKey = "active" }, "sourceEpoch" => source with { ExecutionEpochId = Guid.CreateVersion7() },
            "sourceRoot" => source with { InstanceId = Guid.CreateVersion7() }, _ => source };
        f.Query.QuerySingleOrDefaultAsync<WorkflowRegionRouteSourceRecord>(WorkflowReturnRoundSql.FindStep, Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(source);
        if (defect == "frame") f.Query.QuerySingleOrDefaultAsync<WorkflowRegionFrameRecord>(WorkflowRegionSql.FindFrame, Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns((WorkflowRegionFrameRecord?)null);
        if (defect == "children") f.Query.QuerySingleOrDefaultAsync<int>(WorkflowRegionSql.CountActiveChildren, Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(1);
        if (defect == "unsettled") f.Query.QuerySingleOrDefaultAsync<int>(WorkflowRegionSql.CountUnsettled, Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(1);
        var result = await ExitAsync(f);
        Assert.IsNotNull(result); Assert.IsFalse(result.IsSuccess);
        Assert.HasCount(0, f.Commands.ReceivedCalls().ToArray());
    }

    [TestMethod]
    public async Task Original_returned_entry_source_is_not_mistaken_for_a_new_completed_exit()
    {
        var f = Fixture(); var work = f.Round.EntryWorkId;
        var trace = new WorkflowActivationTrace(f.Source.Id, kindKey: "return", returnProof: new(f.Round.Id, work, "target", f.Round.InputHash, 3));
        var result = await f.Coordinator.TryExitAsync(f.Instance, f.Round.Id, f.Plan, trace, new() { CurrentWorkId = work },
            null, null, new Dictionary<string, JsonElement>(), 4, f.Now, default);
        Assert.IsNull(result);
        Assert.HasCount(0, f.Commands.ReceivedCalls().ToArray());
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    public async Task Cascaded_exit_requires_the_exact_original_leased_return_receipt(int proved)
    {
        var f = Fixture(); SetExit(f); var child = Guid.CreateVersion7(); var work = Guid.CreateVersion7(); var childRound = Guid.CreateVersion7();
        f.Query.QuerySingleOrDefaultAsync<WorkflowRegionRouteSourceRecord>(WorkflowReturnRoundSql.FindStep, Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(f.Source with { Id = f.ExitStepId, ExecutionEpochId = child, StatusKey = "completed" });
        f.Query.QuerySingleOrDefaultAsync<int>(WorkflowReturnRoundSql.CountWorkProof, Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(proved);
        var trace = new WorkflowActivationTrace(f.ExitStepId, kindKey: "return", returnProof: new(childRound, work, "end", new string('c', 64), 3));
        var result = await f.Coordinator.TryExitAsync(f.Instance, f.Round.Id, f.Plan, trace, new() { CurrentWorkId = work }, null, null,
            new Dictionary<string, JsonElement>(), 4, f.Now, default);
        Assert.AreEqual(proved == 1, result!.IsSuccess);
        await f.Commands.Received(proved).ExecuteAsync(WorkflowReturnRoundSql.BindExit,
            Arg.Is<object?>(p => Value(p, "ExitSourceEpochId", child) && Value(p, "ParentReturnWorkId", work)), Arg.Any<CancellationToken>());
    }

    private static Task<Full.NET.Abstractions.Results.Result<Guid>> EnterAsync(Context f) => f.Coordinator.EnterAsync(f.Instance, f.Plan,
        f.Source.Id, f.Round.TargetStepId, "target", f.Round.TargetSnapshotHash, new Dictionary<string, JsonElement>(), 3, 4, f.Now, default);
    private static Task<Full.NET.Abstractions.Results.Result<WorkflowTransitionExecutionResult>?> ExitAsync(Context f) =>
        f.Coordinator.TryExitAsync(f.Instance, f.Round.Id, f.Plan, new(f.ExitStepId), new(), null, null, new Dictionary<string, JsonElement>(), 4, f.Now, default);
    private static void SetExit(Context f) => f.Query.QuerySingleOrDefaultAsync<WorkflowRegionRouteSourceRecord>(WorkflowReturnRoundSql.FindStep,
        Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(f.Source with { Id = f.ExitStepId, ExecutionEpochId = f.Round.Id, StatusKey = "completed" });
    private static bool Value(object? parameters, string key, object? expected) => parameters is IReadOnlyDictionary<string, object?> values && values.TryGetValue(key, out var value) && Equals(value, expected);

    private static Context Fixture()
    {
        var root = Guid.CreateVersion7(); var version = Guid.CreateVersion7(); var actor = Guid.CreateVersion7(); var now = DateTimeOffset.UtcNow;
        var instance = new WorkflowInstanceRecord(root, null, "host", "host", version, Guid.CreateVersion7(), "test", "return", null,
            "active", 1, actor, now, null, null, null, null, null, null);
        var source = new WorkflowRegionRouteSourceRecord(Guid.CreateVersion7(), root, root, "source", "human.approval", "returned", null, null, version);
        var region = "[\"source\",\"target\"]"; var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(region)));
        var round = new WorkflowReturnRoundRecord(Guid.CreateVersion7(), root, "host", version, root, source.Id, Guid.CreateVersion7(),
            "target", "source", "end", region, hash, new string('a', 64), null, null, Guid.CreateVersion7(), new string('b', 64), 3,
            null, null, null, null, 1, now, null, null, null, null, root);
        var draft = new WorkflowDefinitionDraft(1, [Node("start", "start", "target"), Node("target", "human.approval", "source"),
            Node("source", "human.approval", "end"), Node("end", "end")]);
        Assert.IsTrue(WorkflowRuntimePlan.TryCreate(draft, out var plan));
        var query = Substitute.For<IQueryExecutor>(); var command = Substitute.For<ICommandExecutor>(); var ids = Substitute.For<IIdGenerator>();
        ids.NewId().Returns(_ => Guid.CreateVersion7()); command.ExecuteAsync(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(1);
        query.QuerySingleOrDefaultAsync<WorkflowRegionRouteSourceRecord>(WorkflowReturnRoundSql.FindStep, Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(source);
        query.QuerySingleOrDefaultAsync<WorkflowReturnRoundRecord>(WorkflowReturnRoundSql.Find, Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(round);
        query.QuerySingleOrDefaultAsync<int>(WorkflowRegionSql.NextIteration, Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(1);
        query.QueryAsync<WorkflowActivationBindingRecord>(WorkflowActivationBindingSql.List, Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<WorkflowActivationBindingRecord>());
        query.QuerySingleOrDefaultAsync<WorkflowExecutionEpochRecord>(WorkflowExecutionEpochSql.Find, Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            var values = (IReadOnlyDictionary<string, object?>)call.ArgAt<object>(1); var epoch = (Guid)values["ExecutionEpochId"]!;
            return epoch == root ? new WorkflowExecutionEpochRecord(root, root, "host", null, null, "root", 0, 0, "active", 1, now, null)
                : new WorkflowExecutionEpochRecord(round.Id, root, "host", root, source.Id, "return." + hash[..40], 1, 1, "active", 1, now, null);
        });
        query.QuerySingleOrDefaultAsync<WorkflowRegionFrameRecord>(WorkflowRegionSql.FindFrame, Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(new WorkflowRegionFrameRecord(round.Id, root, "host", version, "target", "source", null, null, now));
        query.QuerySingleOrDefaultAsync<WorkflowFormSubmissionRecord>(WorkflowSql.FindFormSubmissionByInstance, Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(new WorkflowFormSubmissionRecord(Guid.CreateVersion7(), root, instance.FormVersionId!.Value, "{}", "internal", 3, actor, now));
        return new(instance, source, round, plan!, query, command, new(query, command, ids), Guid.CreateVersion7(), now);
    }
    private static WorkflowNodeDraft Node(string key, string type, params string[] next) => new(key, type, 1, JsonSerializer.SerializeToElement(new { nextNodeKeys = next }));
    private sealed record Context(WorkflowInstanceRecord Instance, WorkflowRegionRouteSourceRecord Source, WorkflowReturnRoundRecord Round,
        WorkflowRuntimePlan Plan, IQueryExecutor Query, ICommandExecutor Commands, WorkflowReturnRoundCoordinator Coordinator, Guid ExitStepId, DateTimeOffset Now);
}
