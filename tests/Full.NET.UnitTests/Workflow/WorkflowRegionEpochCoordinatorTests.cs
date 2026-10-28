using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using Full.NET.Abstractions.Ids;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Workflow.Domain;
using Full.NET.Modules.Workflow.Persistence;
using NSubstitute;

namespace Full.NET.UnitTests.Workflow;

/// <summary>区域进入与退出保持真实父代次；预算拒绝不写入，存储异常必须让外层事务回滚。</summary>
[TestClass]
public sealed class WorkflowRegionEpochCoordinatorTests
{
    [TestMethod]
    public async Task A_route_inside_a_return_creates_only_its_own_child_and_keeps_the_return_parent_open()
    {
        var data = Create(insideReturn: true);
        var result = await Enter(data); Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(data.Frames.Parent, Parameters(data, WorkflowRegionSql.InsertEpoch)["ParentEpochId"]);
        Assert.AreEqual(0, data.Commands.ReceivedCalls().Count(call => Equals(call.GetArguments()[0], WorkflowRegionSql.CloseEpoch)));
    }

    [TestMethod]
    public async Task A_normal_route_stays_inside_its_return_without_closing_any_epoch()
    {
        var data = Create(insideReturn: true);
        var result = await data.Coordinator.ExitAsync(data.Frames.Instance, data.CurrentEpoch, data.SourceStep, data.Plan, "after", null, DateTimeOffset.UtcNow, default);
        Assert.IsTrue(result.IsSuccess); Assert.AreEqual(data.Frames.Parent, result.ExecutionEpochId); Assert.AreEqual(0, data.Commands.ReceivedCalls().Count());
    }

    [TestMethod]
    public async Task A_route_child_exports_back_to_its_return_parent_without_fabricating_a_return_exit()
    {
        var data = Create(insideReturn: true); var child = data.Frames.Leaf; var parent = data.Frames.Parent; var now = DateTimeOffset.UtcNow;
        data.Frames.Epochs[child] = new(child, data.Frames.Instance.Id, "host", parent, Guid.CreateVersion7(), "review-round", 1, 2, "active", 1, now, null);
        data.Frames.Frames[child] = new(child, data.Frames.Instance.Id, "host", data.Frames.Instance.DefinitionVersionId, "review", "route", null, null, now);
        data.CurrentEpoch = child; data.Source = data.Source with { ExecutionEpochId = child };
        var result = await data.Coordinator.ExitAsync(data.Frames.Instance, child, data.SourceStep, data.Plan, "after", null, now, default);
        Assert.IsTrue(result.IsSuccess); Assert.AreEqual(parent, result.ExecutionEpochId);
        Assert.AreEqual(child, Parameters(data, WorkflowRegionSql.CloseEpoch)["ExecutionEpochId"]);
        Assert.IsFalse(data.Commands.ReceivedCalls().Any(call => Equals(call.GetArguments()[0], WorkflowReturnRoundSql.BindExit)));
    }

    [TestMethod]
    [DataRow("root")]
    [DataRow("hash")]
    [DataRow("source")]
    [DataRow("alreadyExited")]
    public async Task An_unverified_return_parent_is_never_treated_as_an_unbounded_root(string defect)
    {
        var data = Create(insideReturn: true); var round = data.ReturnRound!;
        data.ReturnRound = defect switch { "root" => round with { InstanceId = Guid.CreateVersion7() },
            "hash" => round with { RegionHash = new string('0', 64) }, "source" => round with { SourceStepId = Guid.CreateVersion7() },
            _ => round with { ExitWorkId = Guid.CreateVersion7(), Revision = 2 } };
        Assert.IsFalse((await Enter(data)).IsSuccess); Assert.AreEqual(0, data.Commands.ReceivedCalls().Count());
    }

    [TestMethod]
    public async Task First_entry_creates_a_new_child_and_fixed_boundary_without_closing_the_root()
    {
        var data = Create(); var result = await Enter(data);
        Assert.IsTrue(result.IsSuccess); Assert.AreEqual(data.NewEpoch, result.ExecutionEpochId);
        var epoch = Parameters(data, WorkflowRegionSql.InsertEpoch);
        Assert.AreEqual(data.Frames.Instance.Id, epoch["ParentEpochId"]); Assert.AreEqual(1, epoch["Depth"]); Assert.AreEqual(1, epoch["IterationNumber"]);
        Assert.AreEqual(data.SourceStep, epoch["CauseStepId"]);
        var frame = Parameters(data, WorkflowRegionSql.InsertFrame);
        Assert.AreEqual("review", frame["EntryNodeKey"]); Assert.AreEqual("route", frame["ExitNodeKey"]); Assert.IsNull(frame["ParentForkActivationId"]);
        Assert.IsFalse(data.Commands.ReceivedCalls().Any(call => Equals(call.GetArguments()[0], WorkflowRegionSql.CloseEpoch)));
    }

    [TestMethod]
    public async Task Reentry_creates_a_sibling_with_the_next_iteration_instead_of_resetting_the_parent()
    {
        var data = Create(); ActiveRound(data); data.Frames.Query.QuerySingleOrDefaultAsync<int>(WorkflowRegionSql.NextIteration, Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(2);
        Assert.IsTrue((await Enter(data)).IsSuccess);
        var values = Parameters(data, WorkflowRegionSql.InsertEpoch);
        Assert.AreEqual(data.Frames.Instance.Id, values["ParentEpochId"]); Assert.AreEqual(1, values["Depth"]); Assert.AreEqual(2, values["IterationNumber"]);
        Assert.AreEqual(data.Frames.Parent, Parameters(data, WorkflowRegionSql.CloseEpoch)["ExecutionEpochId"]);
    }

    [TestMethod]
    public async Task Exhausted_iterations_do_not_close_the_current_round_or_write_another_frame()
    {
        var data = Create(); ActiveRound(data); data.Frames.Query.QuerySingleOrDefaultAsync<int>(WorkflowRegionSql.NextIteration, Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(4);
        var result = await Enter(data); Assert.IsFalse(result.IsSuccess); Assert.AreEqual("workflow.activity.region_iteration_exhausted", result.FailureKey);
        Assert.AreEqual(0, data.Commands.ReceivedCalls().Count());
    }

    [TestMethod]
    [DataRow("activeChild")]
    [DataRow("work")]
    [DataRow("otherRound")]
    public async Task Unsettled_or_duplicate_active_regions_are_not_replaced(string fact)
    {
        var data = Create();
        if (fact != "otherRound") ActiveRound(data);
        var statement = fact == "activeChild" ? WorkflowRegionSql.CountActiveChildren : fact == "work" ? WorkflowRegionSql.CountUnsettled : WorkflowRegionSql.CountActiveRegion;
        data.Frames.Query.QuerySingleOrDefaultAsync<int>(statement, Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(1);
        var result = await Enter(data); Assert.IsFalse(result.IsSuccess); Assert.AreEqual("workflow.activity.region_unsettled", result.FailureKey);
        Assert.AreEqual(0, data.Commands.ReceivedCalls().Count());
    }

    [TestMethod]
    public async Task Normal_exit_closes_the_child_and_explicitly_returns_its_real_parent()
    {
        var data = Create(); ActiveRound(data);
        var result = await data.Coordinator.ExitAsync(data.Frames.Instance, data.CurrentEpoch, data.SourceStep, data.Plan, "end", null, DateTimeOffset.UtcNow, default);
        Assert.IsTrue(result.IsSuccess); Assert.AreEqual(data.Frames.Instance.Id, result.ExecutionEpochId);
        Assert.AreEqual(data.Frames.Parent, Parameters(data, WorkflowRegionSql.CloseEpoch)["ExecutionEpochId"]);
        Assert.AreEqual(1, data.Commands.ReceivedCalls().Count(call=>Equals(call.GetArguments()[0],WorkflowRegionSql.CloseEpoch)));
    }

    [TestMethod]
    public async Task Normal_root_exit_keeps_its_original_epoch_without_inventing_a_parent()
    {
        var data = Create();
        var result = await data.Coordinator.ExitAsync(data.Frames.Instance, data.CurrentEpoch, data.SourceStep, data.Plan, "end", null, DateTimeOffset.UtcNow, default);
        Assert.IsTrue(result.IsSuccess); Assert.AreEqual(data.Frames.Instance.Id, result.ExecutionEpochId); Assert.AreEqual(0, data.Commands.ReceivedCalls().Count());
    }

    [TestMethod]
    [DataRow("wrongStep")]
    [DataRow("wrongEpoch")]
    [DataRow("wrongVersion")]
    [DataRow("active")]
    [DataRow("wrongType")]
    public async Task Unproven_route_sources_do_not_authorize_epoch_writes(string mismatch)
    {
        var data = Create(); data.Source = mismatch switch {
            "wrongStep" => data.Source with { Id = Guid.CreateVersion7() }, "wrongEpoch" => data.Source with { ExecutionEpochId = Guid.CreateVersion7() },
            "wrongVersion" => data.Source with { DefinitionVersionId = Guid.CreateVersion7() }, "active" => data.Source with { StatusKey = "active" },
            _ => data.Source with { NodeTypeKey = "human.approval" } };
        Assert.IsFalse((await Enter(data)).IsSuccess); Assert.AreEqual(0, data.Commands.ReceivedCalls().Count());
    }

    [TestMethod]
    [DataRow("epoch")]
    [DataRow("frame")]
    [DataRow("close")]
    public async Task Storage_conflicts_throw_instead_of_committing_a_partial_epoch_change(string stage)
    {
        var data = Create(); if (stage == "close") ActiveRound(data);
        var statement = stage == "epoch" ? WorkflowRegionSql.InsertEpoch : stage == "frame" ? WorkflowRegionSql.InsertFrame : WorkflowRegionSql.CloseEpoch;
        data.Commands.ExecuteAsync(statement, Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(0);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(async () => { _ = await Enter(data); });
    }

    [TestMethod]
    public async Task A_target_that_is_not_in_the_fixed_route_plan_cannot_enter_a_region()
    {
        var data = Create(); var target = data.Target with { Target = data.Target.Target with { NodeKey = "foreign" } };
        var result = await data.Coordinator.EnterAsync(data.Frames.Instance, data.CurrentEpoch, data.SourceStep, data.Plan, target, null, DateTimeOffset.UtcNow, default);
        Assert.IsFalse(result.IsSuccess); Assert.AreEqual(0, data.Commands.ReceivedCalls().Count());
    }

    [TestMethod]
    public async Task A_previous_source_step_cannot_open_another_epoch_even_after_its_old_region_closed()
    {
        var data = Create();
        data.Frames.Query.QuerySingleOrDefaultAsync<int>(WorkflowRegionSql.CountSourceEntries, Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(1);
        var result = await Enter(data);
        Assert.IsFalse(result.IsSuccess); Assert.AreEqual("workflow.activity.region_source_already_applied", result.FailureKey);
        Assert.AreEqual(0, data.Commands.ReceivedCalls().Count());
    }

    [TestMethod]
    public async Task Enter_inherits_only_completed_bindings_outside_the_region_and_keeps_original_step_identity()
    {
        var data=Create(); var root=data.Frames.Instance.Id; var outside=Guid.CreateVersion7();
        data.Frames.Query.QueryAsync<WorkflowActivationBindingRecord>(WorkflowActivationBindingSql.List,Arg.Any<object?>(),Arg.Any<CancellationToken>())
            .Returns(new WorkflowActivationBindingRecord[] {
                new(Guid.CreateVersion7(),root,"host",root,"start",outside,root,DateTimeOffset.UtcNow),
                new(Guid.CreateVersion7(),root,"host",root,"review",Guid.CreateVersion7(),root,DateTimeOffset.UtcNow) });
        Assert.IsTrue((await Enter(data)).IsSuccess);
        var copied=Parameters(data,WorkflowActivationBindingSql.Insert);
        Assert.AreEqual("start",copied["NodeKey"]); Assert.AreEqual(outside,copied["StepId"]);
        Assert.AreEqual(root,copied["SourceEpochId"]); Assert.AreEqual(data.NewEpoch,copied["ExecutionEpochId"]);
    }

    [TestMethod]
    public async Task Exit_clears_every_parent_region_binding_then_exports_only_this_round_actual_steps()
    {
        var data=Create(); ActiveRound(data); var root=data.Frames.Instance.Id; var step=Guid.CreateVersion7();
        data.Frames.Query.QueryAsync<WorkflowActivationBindingRecord>(WorkflowActivationBindingSql.List,Arg.Any<object?>(),Arg.Any<CancellationToken>())
            .Returns(new WorkflowActivationBindingRecord[] {
                new(Guid.CreateVersion7(),root,"host",data.CurrentEpoch,"review",step,data.CurrentEpoch,DateTimeOffset.UtcNow),
                new(Guid.CreateVersion7(),root,"host",data.CurrentEpoch,"start",Guid.CreateVersion7(),root,DateTimeOffset.UtcNow) });
        var result=await data.Coordinator.ExitAsync(data.Frames.Instance,data.CurrentEpoch,data.SourceStep,data.Plan,"end",null,DateTimeOffset.UtcNow,default);
        Assert.IsTrue(result.IsSuccess);
        var deleted=data.Commands.ReceivedCalls().Where(call=>Equals(call.GetArguments()[0],WorkflowActivationBindingSql.Delete))
            .Select(call=>(IReadOnlyDictionary<string,object?>)call.GetArguments()[1]!).ToArray();
        CollectionAssert.AreEquivalent(new[]{"review","route"},deleted.Select(values=>(string)values["NodeKey"]!).ToArray());
        Assert.IsTrue(deleted.All(values=>Equals(values["ExecutionEpochId"],root)));
        var copied=Parameters(data,WorkflowActivationBindingSql.Insert);
        Assert.AreEqual(step,copied["StepId"]); Assert.AreEqual(data.CurrentEpoch,copied["SourceEpochId"]); Assert.AreEqual(root,copied["ExecutionEpochId"]);
    }

    [TestMethod]
    public async Task An_inherited_binding_write_conflict_requires_outer_transaction_rollback()
    {
        var data=Create(); var root=data.Frames.Instance.Id;
        data.Frames.Query.QueryAsync<WorkflowActivationBindingRecord>(WorkflowActivationBindingSql.List,Arg.Any<object?>(),Arg.Any<CancellationToken>())
            .Returns(new WorkflowActivationBindingRecord[] { new(Guid.CreateVersion7(),root,"host",root,"start",Guid.CreateVersion7(),root,DateTimeOffset.UtcNow) });
        data.Commands.ExecuteAsync(WorkflowActivationBindingSql.Insert,Arg.Any<object?>(),Arg.Any<CancellationToken>()).Returns(0);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(async()=>{_=await Enter(data);});
    }

    private static Task<WorkflowRegionEpochChange> Enter(RouteCase data) => data.Coordinator.EnterAsync(data.Frames.Instance, data.CurrentEpoch,
        data.SourceStep, data.Plan, data.Target, null, DateTimeOffset.UtcNow, default);
    private static IReadOnlyDictionary<string, object?> Parameters(RouteCase data, SqlStatement statement) =>
        (IReadOnlyDictionary<string, object?>)data.Commands.ReceivedCalls().Single(call => Equals(call.GetArguments()[0], statement)).GetArguments()[1]!;
    private static void ActiveRound(RouteCase data)
    {
        var id = data.Frames.Parent; var root = data.Frames.Instance.Id; var now = DateTimeOffset.UtcNow;
        data.Frames.Epochs[id] = new(id, root, "host", root, Guid.CreateVersion7(), "review-round", 1, 1, "active", 1, now, null);
        data.Frames.Frames[id] = new(id, root, "host", data.Frames.Instance.DefinitionVersionId, "review", "route", null, null, now);
        data.CurrentEpoch = id; data.Source = data.Source with { ExecutionEpochId = id };
    }
    private static RouteCase Create(bool insideReturn = false)
    {
        var frames = WorkflowExecutionFrameScopeTests.Create(); frames.Epochs.Remove(frames.Parent); frames.Epochs.Remove(frames.Leaf); frames.Frames.Clear();
        WorkflowNodeDraft Node(string key, string type, object? config = null) => new(key, type, 1, JsonSerializer.SerializeToElement(config ?? new { }));
        var route = Node("route", WorkflowDynamicRoutePolicy.NodeType, new { nextNodeKeys = new[] { insideReturn ? "after" : "end" }, routePolicy = new { modeKey = "rules", targets = new[] { new { targetKey = "retry", nodeKey = "review", regionKey = "review-round", maximumIterations = 3,
            condition = new { fieldKey = "amount", @operator = "greaterThan", value = 0 } } } } });
        var nodes = new List<WorkflowNodeDraft> { Node("start", "start"), Node("review", "human.approval"), route, Node("end", "end") };
        var edges = new Dictionary<string, string[]> { ["start"] = ["review"], ["review"] = ["route"], ["route"] = [insideReturn ? "after" : "end"], ["end"] = [] };
        if (insideReturn) { nodes.Add(Node("after", "human.approval")); edges.Add("after", ["end"]); }
        Assert.IsTrue(WorkflowDynamicRoutePlan.TryCompile(new(1, nodes), edges, WorkflowDynamicRoutePolicyTests.Schema(), out var plan));
        var commands = Substitute.For<ICommandExecutor>(); commands.ExecuteAsync(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(1);
        var ids = Substitute.For<IIdGenerator>(); var next = Guid.CreateVersion7(); ids.NewId().Returns(next);
        var step = Guid.CreateVersion7(); var source = new WorkflowRegionRouteSourceRecord(step, frames.Instance.Id, frames.Instance.Id, "route", WorkflowDynamicRoutePolicy.NodeType, "completed", null, null, frames.Instance.DefinitionVersionId);
        var data = new RouteCase(frames, plan!, commands, new(frames.Query, commands, ids), step, next, source);
        frames.Query.QuerySingleOrDefaultAsync<WorkflowRegionRouteSourceRecord>(WorkflowRegionSql.FindRouteSource, Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(_ => data.Source);
        frames.Query.QuerySingleOrDefaultAsync<int>(WorkflowRegionSql.NextIteration, Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(1);
        frames.Query.QuerySingleOrDefaultAsync<int>(WorkflowRegionSql.CountActiveRegion, Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(call => {
            var values = (IReadOnlyDictionary<string, object?>)call.ArgAt<object>(1);
            return frames.Epochs.Values.Count(epoch => epoch.ParentEpochId == (Guid)values["ParentEpochId"]! && epoch.RegionKey == (string)values["RegionKey"]! && epoch.StatusKey == "active");
        });
        if (insideReturn)
        {
            var parent = frames.Parent; var root = frames.Instance.Id; var originalSource = Guid.CreateVersion7(); var now = DateTimeOffset.UtcNow;
            var json = "[\"after\",\"review\",\"route\"]"; var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
            frames.Epochs[parent] = new(parent, root, "host", root, originalSource, "return." + hash[..40], 1, 1, "active", 1, now, null);
            frames.Frames[parent] = new(parent, root, "host", frames.Instance.DefinitionVersionId, "review", "after", null, null, now);
            data.ReturnRound = new(parent, root, "host", frames.Instance.DefinitionVersionId, root, originalSource, Guid.CreateVersion7(), "review", "after", "end",
                json, hash, new string('a', 64), null, null, Guid.CreateVersion7(), new string('b', 64), 1, null, null, null, null, 1, now, null, null, null, null, root);
            data.CurrentEpoch = parent; data.Source = data.Source with { ExecutionEpochId = parent };
            frames.Query.QuerySingleOrDefaultAsync<WorkflowReturnRoundRecord>(WorkflowReturnRoundSql.Find, Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(_ => data.ReturnRound);
        }
        return data;
    }
    private sealed class RouteCase(WorkflowExecutionFrameScopeTests.FrameCase frames, WorkflowDynamicRoutePlan plan, ICommandExecutor commands,
        WorkflowRegionEpochCoordinator coordinator, Guid sourceStep, Guid newEpoch, WorkflowRegionRouteSourceRecord source)
    {
        public WorkflowExecutionFrameScopeTests.FrameCase Frames { get; } = frames;
        public WorkflowDynamicRoutePlan Plan { get; } = plan;
        public WorkflowCompiledRouteTarget Target => Plan.Routes["route"].Targets.Single();
        public ICommandExecutor Commands { get; } = commands;
        public WorkflowRegionEpochCoordinator Coordinator { get; } = coordinator;
        public Guid SourceStep { get; } = sourceStep;
        public Guid NewEpoch { get; } = newEpoch;
        public WorkflowReturnRoundRecord? ReturnRound { get; set; }
        public Guid CurrentEpoch { get; set; } = frames.Instance.Id;
        public WorkflowRegionRouteSourceRecord Source { get; set; } = source;
    }
}
