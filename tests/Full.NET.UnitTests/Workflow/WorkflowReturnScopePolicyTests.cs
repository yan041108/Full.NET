using System.Text.Json;
using Full.NET.Modules.Workflow.Domain;
using Full.NET.Modules.Workflow.Persistence;

namespace Full.NET.UnitTests.Workflow;

/// <summary>以实际因果和fork身份裁定局部/整体退回，序号和同名历史不参与授权。</summary>
[TestClass]
public sealed class WorkflowReturnScopePolicyTests
{
    [TestMethod]
    public void Local_return_closes_only_the_current_branch_and_keeps_the_sibling_active()
    {
        var f = Parallel();
        Assert.IsTrue(Select(f, f.Left, out var selected));
        Assert.AreEqual(f.Fork.Id, selected!.ParallelJoinId); Assert.AreEqual("left", selected.ParallelBranchKey);
        CollectionAssert.AreEquivalent(new[] { f.Source.Id }, selected.ActiveStepIds.ToArray());
        CollectionAssert.AreEquivalent(new[] { f.Left.Id }, selected.InvalidatedStepIds.ToArray());
        Assert.HasCount(0, selected.ClosedForkIds);
        Assert.AreEqual("join", selected.NextNodeKey);
        Assert.IsFalse(selected.ExitsAtRootEnd);
    }

    [TestMethod]
    public void Return_before_an_open_fork_closes_its_actual_subtree_and_exits_at_the_join()
    {
        var f = Parallel();
        Assert.IsTrue(Select(f, f.Before, out var selected));
        Assert.IsNull(selected!.ParallelJoinId); Assert.IsNull(selected.ParallelBranchKey);
        CollectionAssert.AreEquivalent(new[] { f.Fork.Id }, selected.ClosedForkIds.ToArray());
        CollectionAssert.AreEquivalent(new[] { f.Source.Id, f.Right.Id }, selected.ActiveStepIds.ToArray());
        Assert.AreEqual("join", selected.Region.ExitNodeKey); Assert.AreEqual("after", selected.NextNodeKey);
        Assert.IsTrue(selected.InvalidatedStepIds.Contains(f.Before.Id));
    }

    [TestMethod]
    [DataRow("foreignRoot")]
    [DataRow("missingCause")]
    [DataRow("unbound")]
    [DataRow("wrongSourceKind")]
    [DataRow("cyclicContext")]
    [DataRow("unknownParent")]
    [DataRow("scopeMismatch")]
    public void Incomplete_or_foreign_history_cannot_be_promoted_to_return_authority(string defect)
    {
        var f = Parallel(); var steps = f.Steps.ToArray(); var forks = new[] { f.Fork }; var bindings = f.Bindings; var causes = f.Causes;
        if (defect == "foreignRoot") steps[0] = steps[0] with { InstanceId = Guid.CreateVersion7() };
        if (defect == "wrongSourceKind") steps = steps.Select(step => step.Id == f.Source.Id ? step with { NodeTypeKey = "notify.cc" } : step).ToArray();
        if (defect == "missingCause") causes = causes.Where(edge => edge.StepId != f.Left.Id).ToArray();
        if (defect == "unbound") bindings = bindings.Where(binding => binding.StepId != f.Before.Id).ToArray();
        if (defect == "cyclicContext") forks = [f.Fork with { ParentParallelJoinId = f.Fork.Id, ParentBranchKey = "left" }];
        if (defect == "unknownParent") forks = [f.Fork with { ParentParallelJoinId = Guid.CreateVersion7(), ParentBranchKey = "left" }];
        if (defect == "scopeMismatch") bindings = bindings.Select(binding => binding with { TenantScopeKey = "tenant:foreign" }).ToArray();
        Assert.IsFalse(WorkflowReturnScopePolicy.TrySelect(f.Instance, f.Plan, f.Source.Id, f.Before.Id, steps, forks, causes, bindings, out _));
    }

    [TestMethod]
    public void A_completed_sibling_is_not_a_causal_target_even_when_explicitly_bound()
    {
        var f = Parallel(); var right = f.Right with { StatusKey = "completed" };
        var steps = f.Steps.Select(step => step.Id == right.Id ? right : step).ToArray();
        Assert.IsFalse(WorkflowReturnScopePolicy.TrySelect(f.Instance, f.Plan, f.Source.Id, right.Id, steps, [f.Fork], f.Causes,
            [..f.Bindings, Binding(f.Instance, right)], out _));
    }

    [TestMethod]
    [DataRow("integration.trigger")]
    [DataRow("integration.async_trigger")]
    [DataRow("business.command")]
    [DataRow("data.modify")]
    [DataRow("data.clear")]
    [DataRow("process.subprocess")]
    [DataRow("unknown.activity")]
    public void Effects_in_the_sibling_of_a_whole_reopen_are_not_hidden_by_the_source_ancestry(string effect)
    {
        var f = Parallel(); var steps = f.Steps.Select(step => step.Id == f.Right.Id ? step with { NodeTypeKey = effect } : step).ToArray();
        Assert.IsFalse(WorkflowReturnScopePolicy.TrySelect(f.Instance, f.Plan, f.Source.Id, f.Before.Id, steps, [f.Fork], f.Causes, f.Bindings, out _));
        Assert.IsTrue(WorkflowReturnScopePolicy.TrySelect(f.Instance, f.Plan, f.Source.Id, f.Left.Id, steps, [f.Fork], f.Causes, f.Bindings, out _));
    }

    [TestMethod]
    public void Whole_reopen_keeps_the_actual_outer_parent_distinct_from_the_child_source_epoch()
    {
        var f = Parallel(); var child = Guid.CreateVersion7();
        var steps = f.Steps.Select(step => step.Id == f.Source.Id ? step with { ExecutionEpochId = child } : step).ToArray();
        var bindings = f.Bindings.Select(binding => binding with { ExecutionEpochId = child }).ToArray();
        Assert.IsTrue(WorkflowReturnScopePolicy.TrySelect(f.Instance, f.Plan, f.Source.Id, f.Before.Id, steps, [f.Fork], f.Causes, bindings, out var selected));
        Assert.AreEqual(f.Instance.Id, selected!.ParentEpochId);
        var unproven = new WorkflowReturnScopeState(steps, [f.Fork], [], f.Causes, bindings);
        Assert.IsFalse(unproven.TrySelect(f.Instance, f.Plan, f.Source.Id, f.Before.Id, out _));
        var proven = unproven with { EpochLineage = new HashSet<Guid> { child, f.Instance.Id } };
        Assert.IsTrue(proven.TrySelect(f.Instance, f.Plan, f.Source.Id, f.Before.Id, out _));
    }

    [TestMethod]
    public void Exclusive_return_uses_the_unexecuted_common_end_from_the_fixed_definition()
    {
        var root = Guid.CreateVersion7(); var instance = Instance(root);
        var draft = new WorkflowDefinitionDraft(1, [Node("start", "start", new { nextNodeKeys = new[] { "before" } }),
            Human("before", "choice"), Node("choice", "gateway.exclusive", new { nextNodeKeys = new[] { "left", "right" }, defaultNextNodeKey = "right",
                branches = new[] { new { branchKey = "selected", nextNodeKey = "left", condition = new { fieldKey = "amount", @operator = "equals", value = 1 } } } }),
            Human("left", "end"), Human("right", "end"), Node("end", "end", new { })]);
        Assert.IsTrue(WorkflowRuntimePlan.TryCreate(draft, new(1, 1, [new("main", [new("amount", "integer", false, new Dictionary<string, JsonElement>())])]), out var plan));
        var before = Fact(instance, "before", "human.approval", "completed"); var gateway = Fact(instance, "choice", "gateway.exclusive", "completed");
        var source = Fact(instance, "left", "human.approval", "active"); var start = new WorkflowReturnStepFact(root, root, root, "start", "start", "completed", null, null);
        var edges = new[] { Cause(instance, before.Id, root), Cause(instance, gateway.Id, before.Id), Cause(instance, source.Id, gateway.Id) };
        Assert.IsTrue(WorkflowReturnScopePolicy.TrySelect(instance, plan!, source.Id, before.Id, [start, before, gateway, source], [], edges,
            [Binding(instance, start), Binding(instance, before), Binding(instance, gateway)], out var selected));
        Assert.IsTrue(selected!.ExitsAtRootEnd); Assert.AreEqual("end", selected.Region.ExitNodeKey); Assert.AreEqual("end", selected.NextNodeKey);
        Assert.HasCount(0, selected.ClosedForkIds);
    }

    [TestMethod]
    [DataRow("pending")]
    [DataRow("leased")]
    [DataRow("faulted")]
    public void Sibling_work_is_preserved_locally_and_closed_only_when_its_actual_fork_is_reopened(string status)
    {
        var f = Parallel(); var work = new WorkflowReturnWorkFact(Guid.CreateVersion7(), f.Instance.Id, f.Instance.Id, f.Fork.Id, "right", "right", status);
        Assert.IsTrue(WorkflowReturnScopePolicy.TrySelect(f.Instance, f.Plan, f.Source.Id, f.Left.Id, f.Steps, [f.Fork], f.Causes, f.Bindings, out var local, [work]));
        Assert.HasCount(0, local!.ClosedWorkIds);
        Assert.IsTrue(WorkflowReturnScopePolicy.TrySelect(f.Instance, f.Plan, f.Source.Id, f.Before.Id, f.Steps, [f.Fork], f.Causes, f.Bindings, out var whole, [work]));
        CollectionAssert.AreEqual(new[] { work.Id }, whole!.ClosedWorkIds.ToArray());
    }

    [TestMethod]
    [DataRow("ownBranch")]
    [DataRow("outerFrame")]
    [DataRow("foreignRoot")]
    [DataRow("invalidStatus")]
    public void Unsettled_or_untrusted_work_cannot_disappear_from_a_local_round(string defect)
    {
        var f = Parallel(); var work = new WorkflowReturnWorkFact(Guid.CreateVersion7(), f.Instance.Id, f.Instance.Id,
            defect == "outerFrame" ? null : f.Fork.Id, defect == "outerFrame" ? null : "left", "source", "pending");
        if (defect == "foreignRoot") work = work with { InstanceId = Guid.CreateVersion7() };
        if (defect == "invalidStatus") work = work with { StatusKey = "completed" };
        Assert.IsFalse(WorkflowReturnScopePolicy.TrySelect(f.Instance, f.Plan, f.Source.Id, f.Left.Id, f.Steps, [f.Fork], f.Causes, f.Bindings, out _, [work]));
    }

    [TestMethod]
    public void An_active_outer_producer_blocks_the_round_while_an_active_sibling_does_not()
    {
        var f = Parallel(); var producer = Fact(f.Instance, "after", "human.approval", "active");
        Assert.IsTrue(Select(f, f.Left, out _));
        Assert.IsFalse(WorkflowReturnScopePolicy.TrySelect(f.Instance, f.Plan, f.Source.Id, f.Left.Id, [..f.Steps, producer], [f.Fork], f.Causes, f.Bindings, out _));
    }

    [TestMethod]
    public void Inherited_outer_targets_do_not_authorize_a_child_round_to_swallow_its_still_open_parent()
    {
        var instance = Instance(Guid.CreateVersion7()); var child = Guid.CreateVersion7();
        var draft = new WorkflowDefinitionDraft(1, [Node("start", "start", new { nextNodeKeys = new[] { "before" } }),
            Human("before", "target"), Human("target", "source"), Human("source", "end"), Node("end", "end", new { })]);
        Assert.IsTrue(WorkflowRuntimePlan.TryCreate(draft, out var plan));
        var start = new WorkflowReturnStepFact(instance.Id, instance.Id, instance.Id, "start", "start", "completed", null, null);
        var before = Fact(instance, "before", "human.approval", "completed");
        var target = Fact(instance, "target", "human.approval", "completed") with { ExecutionEpochId = child };
        var source = Fact(instance, "source", "human.approval", "active") with { ExecutionEpochId = child };
        WorkflowReturnStepFact[] steps = [start, before, target, source];
        WorkflowStepCauseRecord[] causes = [Cause(instance, before.Id, start.Id), Cause(instance, target.Id, before.Id), Cause(instance, source.Id, target.Id)];
        var bindings = new[] { start, before, target }.Select(step => Binding(instance, step) with { ExecutionEpochId = child }).ToArray();
        var frame = new WorkflowRegionFrameRecord(child, instance.Id, "host", instance.DefinitionVersionId, "target", "source", null, null, DateTimeOffset.UtcNow);
        var state = new WorkflowReturnScopeState(steps, [], [], causes, bindings, frame);
        Assert.IsTrue(WorkflowReturnScopePolicy.TrySelect(instance, plan!, source.Id, before.Id, steps, [], causes, bindings, out _));
        Assert.IsTrue(state.TrySelect(instance, plan!, source.Id, target.Id, out _));
        Assert.IsFalse(state.TrySelect(instance, plan!, source.Id, before.Id, out _));
    }

    [TestMethod]
    [DataRow(false, "completed", true)]
    [DataRow(true, "completed", false)]
    [DataRow(false, "active", false)]
    public void Return_requires_every_route_reference_to_stay_inside_its_region_and_rejects_an_unsettled_decision(bool escapes, string routeStatus, bool allowed)
    {
        var instance = Instance(Guid.CreateVersion7());
        var draft = new WorkflowDefinitionDraft(1, [Node("start", "start", new { nextNodeKeys = new[] { "before" } }), Human("before", "route"),
            Node("route", "control.route", new { nextNodeKeys = new[] { "source" }, routePolicy = new { modeKey = "rules", targets = new[] {
                new { targetKey = "chosen", nodeKey = escapes ? "end" : "before", regionKey = "retry", maximumIterations = 2,
                    condition = new { fieldKey = "title", @operator = "equals", value = "retry" } } } } }), Human("source", "end"), Node("end", "end", new { })], "automation");
        var schema = new WorkflowFormSchema(1, 1, [new("main", [new("title", "text", false, new Dictionary<string, JsonElement>())])]);
        Assert.IsTrue(WorkflowRuntimePlan.TryCreate(draft, schema, out var plan));
        var start = new WorkflowReturnStepFact(instance.Id, instance.Id, instance.Id, "start", "start", "completed", null, null);
        var before = Fact(instance, "before", "human.approval", "completed"); var route = Fact(instance, "route", "control.route", routeStatus);
        var source = Fact(instance, "source", "human.approval", "active");
        Assert.AreEqual(allowed, WorkflowReturnScopePolicy.TrySelect(instance, plan!, source.Id, before.Id, [start, before, route, source], [],
            [Cause(instance, before.Id, start.Id), Cause(instance, route.Id, before.Id), Cause(instance, source.Id, route.Id)],
            routeStatus == "completed" ? [Binding(instance, start), Binding(instance, before), Binding(instance, route)] : [Binding(instance, start), Binding(instance, before)], out _));
    }

    private static bool Select(Context f, WorkflowReturnStepFact target, out WorkflowReturnScopeSelection? selected) =>
        WorkflowReturnScopePolicy.TrySelect(f.Instance, f.Plan, f.Source.Id, target.Id, f.Steps, [f.Fork], f.Causes, f.Bindings, out selected);
    private static Context Parallel()
    {
        var instance = Instance(Guid.CreateVersion7()); var forkId = Guid.CreateVersion7();
        var draft = new WorkflowDefinitionDraft(1, [Node("start", "start", new { nextNodeKeys = new[] { "before" } }), Human("before", "fork"),
            Node("fork", "gateway.parallel", new { gatewayRoleKey = "fork", joinNodeKey = "join", branches = new[] {
                new { branchKey = "left", nextNodeKey = "left" }, new { branchKey = "right", nextNodeKey = "right" } } }),
            Human("left", "source"), Human("source", "join"), Human("right", "join"),
            Node("join", "gateway.parallel", new { gatewayRoleKey = "join", forkNodeKey = "fork", nextNodeKeys = new[] { "after" } }), Human("after", "end"), Node("end", "end", new { })]);
        Assert.IsTrue(WorkflowRuntimePlan.TryCreate(draft, out var plan));
        var start = new WorkflowReturnStepFact(instance.Id, instance.Id, instance.Id, "start", "start", "completed", null, null);
        var before = Fact(instance, "before", "human.approval", "completed"); var fork = Fact(instance, "fork", "gateway.parallel", "completed");
        var left = Fact(instance, "left", "human.approval", "completed", forkId, "left");
        var source = Fact(instance, "source", "human.approval", "active", forkId, "left");
        var right = Fact(instance, "right", "human.approval", "active", forkId, "right");
        return new(instance, plan!, before, left, source, right, new(forkId, instance.Id, instance.Id, "fork", "join", "waiting", null, null),
            [start, before, fork, left, source, right], [Cause(instance, before.Id, start.Id), Cause(instance, fork.Id, before.Id), Cause(instance, left.Id, fork.Id),
                Cause(instance, source.Id, left.Id), Cause(instance, right.Id, fork.Id)],
            [Binding(instance, start), Binding(instance, before), Binding(instance, fork), Binding(instance, left)]);
    }
    private static WorkflowInstanceRecord Instance(Guid root) => new(root, null, "host", "host", Guid.CreateVersion7(), Guid.CreateVersion7(), "test", "return.scope", null,
        "active", 1, Guid.CreateVersion7(), DateTimeOffset.UtcNow, null, null, null, null, null, null);
    private static WorkflowReturnStepFact Fact(WorkflowInstanceRecord instance, string node, string kind, string status, Guid? fork = null, string? branch = null) =>
        new(Guid.CreateVersion7(), instance.Id, instance.Id, node, kind, status, fork, branch);
    private static WorkflowActivationBindingRecord Binding(WorkflowInstanceRecord instance, WorkflowReturnStepFact step) =>
        new(Guid.CreateVersion7(), instance.Id, "host", instance.Id, step.NodeKey, step.Id, step.ExecutionEpochId, DateTimeOffset.UtcNow);
    private static WorkflowStepCauseRecord Cause(WorkflowInstanceRecord instance, Guid step, Guid parent) =>
        new(Guid.CreateVersion7(), instance.Id, "host", step, parent, "sequence", DateTimeOffset.UtcNow);
    private static WorkflowNodeDraft Human(string key, string next) => Node(key, "human.approval", new { nextNodeKeys = new[] { next } });
    private static WorkflowNodeDraft Node(string key, string kind, object config) => new(key, kind, 1, JsonSerializer.SerializeToElement(config));
    private sealed record Context(WorkflowInstanceRecord Instance, WorkflowRuntimePlan Plan, WorkflowReturnStepFact Before, WorkflowReturnStepFact Left,
        WorkflowReturnStepFact Source, WorkflowReturnStepFact Right, WorkflowReturnForkFact Fork, IReadOnlyList<WorkflowReturnStepFact> Steps,
        IReadOnlyList<WorkflowStepCauseRecord> Causes, IReadOnlyList<WorkflowActivationBindingRecord> Bindings);
}
