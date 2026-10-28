using System.Text.Json;
using Full.NET.Modules.Workflow.Domain;

namespace Full.NET.UnitTests.Workflow;

/// <summary>目标引用必须绑定真实结构区域，人审证明也要覆盖跳转出口。</summary>
[TestClass]
public sealed class WorkflowDynamicRoutePlanTests
{
    [TestMethod]
    public void Backward_target_compiles_as_a_bounded_region_and_keeps_the_normal_exit()
    {
        var (draft, edges) = Chain();
        Assert.IsTrue(WorkflowDynamicRoutePlan.TryCompile(draft, edges, WorkflowDynamicRoutePolicyTests.Schema(), out var plan));
        var route = plan!.Routes["route"];
        Assert.AreEqual("end", route.NormalNextNodeKey); Assert.AreEqual(3, route.Targets[0].Target.MaximumIterations);
        Assert.IsTrue(route.Targets[0].Reenters);
        CollectionAssert.AreEquivalent(new[] { "review", "route" }, route.Targets[0].Region!.NodeKeys.ToArray());
    }

    [TestMethod]
    public void Forward_reference_stays_in_the_same_structured_scope()
    {
        var (draft, edges) = Chain();
        draft = draft with { Nodes = draft.Nodes.Select(node => node.NodeKey == "route" ? Route("route", "skip", "target", "forward") : node)
            .Append(Node("skip", "human.approval")).Append(Node("target", "human.approval")).ToArray() };
        edges["route"] = ["skip"]; edges["skip"] = ["target"]; edges["target"] = ["end"];
        Assert.IsTrue(WorkflowDynamicRoutePlan.TryCompile(draft, edges, WorkflowDynamicRoutePolicyTests.Schema(), out var plan));
        Assert.IsFalse(plan!.Routes["route"].Targets[0].Reenters); Assert.IsNull(plan.Routes["route"].Targets[0].Region);
    }

    [TestMethod]
    public void A_forward_reference_cannot_bypass_the_required_human_decision()
    {
        var draft = new WorkflowDefinitionDraft(1, [Node("start", "start"), Route("route", "review", "end", "forward"), Node("review", "human.approval"), Node("end", "end")]);
        var edges = new Dictionary<string, string[]> { ["start"] = ["route"], ["route"] = ["review"], ["review"] = ["end"], ["end"] = [] };
        Assert.IsFalse(WorkflowDynamicRoutePlan.TryCompile(draft, edges, WorkflowDynamicRoutePolicyTests.Schema(), out _));
        Assert.IsTrue(WorkflowDynamicRoutePlan.TryCompile(draft with { WorkflowKindKey = "automation" }, edges, WorkflowDynamicRoutePolicyTests.Schema(), out _));
    }

    [TestMethod]
    [DataRow("root")]
    [DataRow("returnRegion")]
    [DataRow("missing")]
    [DataRow("same")]
    public void Reserved_missing_or_self_targets_cannot_become_regions(string defect)
    {
        var (draft, edges) = Chain();
        draft = draft with { Nodes = draft.Nodes.Select(node => node.NodeKey == "route"
            ? Route("route", "end", defect == "missing" ? "missing" : defect == "same" ? "route" : "review", defect == "root" ? "root" : defect == "returnRegion" ? "return.reserved" : "round") : node).ToArray() };
        Assert.IsFalse(WorkflowDynamicRoutePlan.TryCompile(draft, edges, WorkflowDynamicRoutePolicyTests.Schema(), out _));
    }

    [TestMethod]
    public void Partial_region_overlap_is_rejected_but_nested_regions_have_unique_boundaries()
    {
        var nodes = new[] { Node("start", "start"), Node("review", "human.approval"), Node("second", "human.approval"),
            Route("inner", "outer", "review", "inner-round"), Route("outer", "end", "second", "outer-round"), Node("end", "end") };
        var edges = new Dictionary<string, string[]> { ["start"] = ["review"], ["review"] = ["second"], ["second"] = ["inner"], ["inner"] = ["outer"], ["outer"] = ["end"], ["end"] = [] };
        Assert.IsFalse(WorkflowDynamicRoutePlan.TryCompile(new(1, nodes), edges, WorkflowDynamicRoutePolicyTests.Schema(), out _));
        nodes[3] = Route("inner", "outer", "second", "inner-round"); nodes[4] = Route("outer", "end", "review", "outer-round");
        Assert.IsTrue(WorkflowDynamicRoutePlan.TryCompile(new(1, nodes), edges, WorkflowDynamicRoutePolicyTests.Schema(), out var plan));
        Assert.AreEqual(2, plan!.Routes.Count);
        nodes[4] = Route("outer", "end", "review", "inner-round");
        Assert.IsFalse(WorkflowDynamicRoutePlan.TryCompile(new(1, nodes), edges, WorkflowDynamicRoutePolicyTests.Schema(), out _));
    }

    [TestMethod]
    public void Route_metadata_on_another_node_type_is_rejected()
    {
        var (draft, edges) = Chain();
        draft = draft with { Nodes = draft.Nodes.Select(node => node.NodeKey == "route" ? node with { NodeTypeKey = "control.empty" } : node).ToArray() };
        Assert.IsFalse(WorkflowDynamicRoutePlan.TryCompile(draft, edges, WorkflowDynamicRoutePolicyTests.Schema(), out _));
    }

    [TestMethod]
    [DataRow("leave")]
    [DataRow("enter")]
    public void Other_route_references_cannot_create_an_extra_region_entry_or_exit(string direction)
    {
        var outside = direction == "enter";
        var nodes = new[] { Node("start", "start"), Node("review", "human.approval"), Node("middle", "human.approval"),
            Route("forward", outside ? "review" : "loopExit", outside ? "middle" : "after", "forward-region"),
            Route("loopExit", "after", "review", "loop-region"), Node("after", "human.approval"), Node("end", "end") };
        var edges = new Dictionary<string, string[]> { ["start"] = [outside ? "forward" : "review"],
            ["review"] = ["middle"], ["middle"] = [outside ? "loopExit" : "forward"],
            ["forward"] = [outside ? "review" : "loopExit"], ["loopExit"] = ["after"], ["after"] = ["end"], ["end"] = [] };
        Assert.IsFalse(WorkflowDynamicRoutePlan.TryCompile(new(1, nodes), edges, WorkflowDynamicRoutePolicyTests.Schema(), out _));
    }

    private static (WorkflowDefinitionDraft Draft, Dictionary<string, string[]> Edges) Chain() =>
        (new(1, [Node("start", "start"), Node("review", "human.approval"), Route("route", "end", "review", "review-round"), Node("end", "end")]),
            new() { ["start"] = ["review"], ["review"] = ["route"], ["route"] = ["end"], ["end"] = [] });
    private static WorkflowNodeDraft Route(string key, string normal, string target, string region) => Node(key, WorkflowDynamicRoutePolicy.NodeType,
        new { nextNodeKeys = new[] { normal }, routePolicy = new { modeKey = "rules", targets = new[] { new { targetKey = "retry", nodeKey = target, regionKey = region, maximumIterations = 3,
            condition = new { fieldKey = "amount", @operator = "greaterThan", value = 0 } } } } });
    private static WorkflowNodeDraft Node(string key, string kind, object? config = null) => new(key, kind, 1, JsonSerializer.SerializeToElement(config ?? new { }));
}
