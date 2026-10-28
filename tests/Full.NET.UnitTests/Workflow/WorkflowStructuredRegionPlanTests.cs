using System.Text.Json;
using Full.NET.Modules.Workflow.Domain;

namespace Full.NET.UnitTests.Workflow;

/// <summary>区域引用保留正常DAG，不用放宽环检测或共享节点签名来容许非法跳转。</summary>
[TestClass]
public sealed class WorkflowStructuredRegionPlanTests
{
    [TestMethod]
    public void Return_within_branch_keeps_its_actual_exit_and_does_not_close_siblings()
    {
        var (draft, edges) = Parallel();
        Assert.IsTrue(WorkflowStructuredRegionPlan.TryCreate(draft, edges, out var plan));
        Assert.IsTrue(plan!.TryDescribeReturnRegion("left", "leftNext", out var region, out var whole));
        Assert.IsFalse(whole);
        CollectionAssert.AreEquivalent(new[] { "left", "leftNext" }, region!.NodeKeys.ToArray());
        Assert.IsFalse(plan.TryDescribeReturnRegion("right", "leftNext", out _, out _));
        Assert.IsFalse(plan.TryDescribeReturnRegion("after", "leftNext", out _, out _));
        Assert.IsFalse(plan.TryDescribeReturnRegion("missing", "leftNext", out _, out _));
    }

    [TestMethod]
    public void Return_before_an_open_fork_exits_only_after_the_entire_original_region()
    {
        var (draft, edges) = Parallel();
        draft = draft with { Nodes = [..draft.Nodes, Node("before", "human.approval", new { nextNodeKeys = new[] { "fork" } })] };
        edges["start"] = ["before"]; edges["before"] = ["fork"];
        Assert.IsTrue(WorkflowStructuredRegionPlan.TryCreate(draft, edges, out var plan));
        Assert.IsTrue(plan!.TryDescribeReturnRegion("before", "leftNext", out var region, out var whole));
        Assert.IsTrue(whole);
        Assert.AreEqual("join", region!.ExitNodeKey);
        CollectionAssert.AreEquivalent(new[] { "before", "fork", "left", "leftNext", "right", "join" }, region.NodeKeys.ToArray());
        Assert.IsFalse(region.NodeKeys.Contains("after"));
        Assert.IsFalse(plan.TryDescribeReturnRegion("left", "right", out _, out _));
    }

    [TestMethod]
    public void Linear_region_preserves_references_and_freezes_the_normal_graph()
    {
        var (draft, edges) = Linear();
        Assert.IsTrue(WorkflowStructuredRegionPlan.TryCreate(draft, edges, out var plan));
        Assert.IsTrue(plan!.TryDescribeRegion("a", "c", out var region));
        CollectionAssert.AreEquivalent(new[] { "a", "b", "c" }, region!.NodeKeys.ToArray());
        Assert.IsTrue(plan.CanForwardTo("a", "c")); Assert.IsFalse(plan.CanForwardTo("c", "a"));
        Assert.IsFalse(plan.TryDescribeRegion("a", "a", out _)); Assert.IsFalse(plan.CanForwardTo("a", "missing"));
        edges["a"][0] = "end";
        Assert.IsTrue(plan.TryDescribeRegion("a", "c", out _));
    }

    [TestMethod]
    public void Complete_fork_can_be_reopened_but_sibling_and_boundary_entry_are_rejected()
    {
        var (draft, edges) = Parallel();
        Assert.IsTrue(WorkflowStructuredRegionPlan.TryCreate(draft, edges, out var plan));
        Assert.IsTrue(plan!.TryDescribeRegion("fork", "after", out var region));
        CollectionAssert.AreEquivalent(new[] { "fork", "left", "leftNext", "right", "join", "after" }, region!.NodeKeys.ToArray());
        Assert.IsTrue(plan.TryDescribeRegion("left", "leftNext", out _));
        Assert.IsTrue(plan.CanForwardTo("left", "leftNext"));
        Assert.IsFalse(plan.CanForwardTo("left", "right")); Assert.IsFalse(plan.CanForwardTo("left", "after"));
        Assert.IsFalse(plan.CanForwardTo("fork", "join")); Assert.IsFalse(plan.TryDescribeRegion("join", "after", out _));
        Assert.IsFalse(plan.TryDescribeRegion("left", "after", out _)); Assert.IsFalse(plan.TryDescribeRegion("fork", "leftNext", out _));
    }

    [TestMethod]
    public void Shared_node_before_the_declared_join_cannot_hide_sibling_branch_crossing()
    {
        var (draft, edges) = Parallel();
        edges["right"] = ["leftNext"];
        Assert.IsFalse(WorkflowStructuredRegionPlan.TryCreate(draft, edges, out _));
    }

    [TestMethod]
    public void Join_must_pop_its_actual_matching_fork()
    {
        var (draft, edges) = Parallel();
        draft = draft with { Nodes = draft.Nodes.Select(node => node.NodeKey == "join"
            ? Node("join", "gateway.parallel", new { gatewayRoleKey = "join", forkNodeKey = "foreign", nextNodeKeys = new[] { "after" } }) : node).ToArray() };
        Assert.IsFalse(WorkflowStructuredRegionPlan.TryCreate(draft, edges, out _));
    }

    [TestMethod]
    [DataRow("roleNumber", "3")]
    [DataRow("roleNull", "null")]
    [DataRow("roleBoolean", "true")]
    [DataRow("forkNumber", "3")]
    public void Malformed_gateway_identity_is_rejected_without_throwing(string defect, string invalidValue)
    {
        var (draft, edges) = Parallel(); var key = defect == "forkNumber" ? "join" : "fork";
        var nodes = draft.Nodes.Select(node => {
            if (node.NodeKey != key) return node;
            var json = node.Config.GetRawText().Replace(defect == "forkNumber" ? "\"forkNodeKey\":\"fork\"" : "\"gatewayRoleKey\":\"fork\"",
                defect == "forkNumber" ? "\"forkNodeKey\":" + invalidValue : "\"gatewayRoleKey\":" + invalidValue, StringComparison.Ordinal);
            using var document = JsonDocument.Parse(json); return node with { Config = document.RootElement.Clone() };
        }).ToArray();
        Assert.IsFalse(WorkflowStructuredRegionPlan.TryCreate(draft with { Nodes = nodes }, edges, out _));
    }

    [TestMethod]
    public void Inclusive_default_branch_keeps_its_own_scope_and_reopens_with_the_whole_fork()
    {
        var (draft, edges) = Parallel();
        draft = draft with { Nodes = draft.Nodes.Select(node => node.NodeKey == "fork"
            ? Node("fork", "gateway.inclusive", new { gatewayRoleKey = "fork", joinNodeKey = "join", defaultNextNodeKey = "right",
                branches = new[] { new { branchKey = "selected", nextNodeKey = "left", condition = new { fieldKey = "amount", @operator = "equals", value = 1 } } } })
            : node.NodeKey == "join" ? node with { NodeTypeKey = "gateway.inclusive" } : node).ToArray() };
        Assert.IsTrue(WorkflowStructuredRegionPlan.TryCreate(draft, edges, out var plan));
        Assert.IsTrue(plan!.TryDescribeRegion("fork", "after", out _));
        Assert.IsFalse(plan.CanForwardTo("left", "right")); Assert.IsFalse(plan.CanForwardTo("right", "left"));
    }

    [TestMethod]
    public void Exclusive_branches_share_only_the_common_postdominator_scope()
    {
        var (draft, edges) = Parallel();
        draft = draft with { Nodes = draft.Nodes.Where(node => node.NodeKey != "join").Select(node => node.NodeKey == "fork"
            ? Node("fork", "gateway.exclusive", new { nextNodeKeys = new[] { "left", "right" }, defaultNextNodeKey = "right",
                branches = new[] { new { branchKey = "selected", nextNodeKey = "left", condition = new { fieldKey = "amount", @operator = "equals", value = 1 } } } }) : node).ToArray() };
        edges.Remove("join"); edges["leftNext"] = ["after"]; edges["right"] = ["after"];
        Assert.IsTrue(WorkflowStructuredRegionPlan.TryCreate(draft, edges, out var plan));
        Assert.IsTrue(plan!.TryDescribeRegion("fork", "after", out _));
        Assert.IsFalse(plan.CanForwardTo("left", "right")); Assert.IsFalse(plan.CanForwardTo("left", "after"));
        Assert.IsTrue(plan.TryDescribeRegion("left", "leftNext", out _));
        Assert.IsFalse(plan.TryDescribeRegion("left", "after", out _));
    }

    [TestMethod]
    [DataRow("cycle")]
    [DataRow("dangling")]
    [DataRow("unreachable")]
    [DataRow("earlySink")]
    [DataRow("duplicateEdge")]
    public void Invalid_normal_graph_is_rejected_before_any_recursive_region_walk(string defect)
    {
        var (draft, edges) = Linear();
        switch (defect)
        {
            case "cycle": edges["c"] = ["a", "end"]; break;
            case "dangling": edges["c"] = ["missing"]; break;
            case "unreachable": edges["start"] = ["b"]; break;
            case "earlySink": edges["b"] = []; break;
            case "duplicateEdge": edges["a"] = ["b", "b"]; break;
        }
        Assert.IsFalse(WorkflowStructuredRegionPlan.TryCreate(draft, edges, out _));
    }

    [TestMethod]
    [DataRow(128, true)]
    [DataRow(129, false)]
    public void Normal_graph_node_budget_is_checked_before_building_regions(int count, bool accepted)
    {
        var nodes = Enumerable.Range(0, count).Select(index => Node("node" + index,
            index == 0 ? "start" : index == count - 1 ? "end" : "control.empty")).ToArray();
        var edges = Enumerable.Range(0, count).ToDictionary(index => "node" + index,
            index => index == count - 1 ? Array.Empty<string>() : new[] { "node" + (index + 1) });
        Assert.AreEqual(accepted, WorkflowStructuredRegionPlan.TryCreate(new(1, nodes, "automation"), edges, out _));
    }

    [TestMethod]
    public void Oversized_successor_array_is_rejected_before_it_is_copied()
    {
        var (draft, edges) = Linear(); edges["start"] = Enumerable.Repeat("a", 513).ToArray();
        Assert.IsFalse(WorkflowStructuredRegionPlan.TryCreate(draft, edges, out _));
    }

    [TestMethod]
    [DataRow(8, true)]
    [DataRow(9, false)]
    public void Nested_region_frames_have_an_explicit_depth_budget(int depth, bool accepted)
    {
        var nodes = new List<WorkflowNodeDraft> { Node("start", "start"), Node("end", "end") };
        var edges = new Dictionary<string, string[]>(StringComparer.Ordinal) { ["start"] = ["fork0"], ["end"] = [] };
        for (var index = 0; index < depth; index++)
        {
            var fork = "fork" + index; var join = "join" + index; var right = "right" + index;
            var left = index == depth - 1 ? "left" : "fork" + (index + 1);
            nodes.Add(Node(fork, "gateway.parallel", new { gatewayRoleKey = "fork", joinNodeKey = join,
                branches = new[] { new { branchKey = "left", nextNodeKey = left }, new { branchKey = "right", nextNodeKey = right } } }));
            nodes.Add(Node(join, "gateway.parallel", new { gatewayRoleKey = "join", forkNodeKey = fork, nextNodeKeys = new[] { index == 0 ? "end" : "join" + (index - 1) } }));
            nodes.Add(Node(right, "human.approval"));
            edges[fork] = [left, right]; edges[right] = [join]; edges[join] = [index == 0 ? "end" : "join" + (index - 1)];
        }
        nodes.Add(Node("left", "human.approval")); edges["left"] = ["join" + (depth - 1)];
        Assert.AreEqual(accepted, WorkflowStructuredRegionPlan.TryCreate(new(1, nodes), edges, out _));
    }

    private static (WorkflowDefinitionDraft Draft, Dictionary<string, string[]> Edges) Linear() =>
        (new(1, [Node("start", "start"), Node("a", "human.approval"), Node("b", "control.empty"), Node("c", "human.approval"), Node("end", "end")]),
            new(StringComparer.Ordinal) { ["start"] = ["a"], ["a"] = ["b"], ["b"] = ["c"], ["c"] = ["end"], ["end"] = [] });

    private static (WorkflowDefinitionDraft Draft, Dictionary<string, string[]> Edges) Parallel() =>
        (new(1, [Node("start", "start"), Node("fork", "gateway.parallel", new { gatewayRoleKey = "fork", joinNodeKey = "join",
            branches = new[] { new { branchKey = "left", nextNodeKey = "left" }, new { branchKey = "right", nextNodeKey = "right" } } }),
            Node("left", "human.approval"), Node("leftNext", "human.approval"), Node("right", "human.approval"),
            Node("join", "gateway.parallel", new { gatewayRoleKey = "join", forkNodeKey = "fork", nextNodeKeys = new[] { "after" } }),
            Node("after", "human.approval"), Node("end", "end")]),
            new(StringComparer.Ordinal) { ["start"] = ["fork"], ["fork"] = ["left", "right"], ["left"] = ["leftNext"], ["leftNext"] = ["join"], ["right"] = ["join"], ["join"] = ["after"], ["after"] = ["end"], ["end"] = [] });

    private static WorkflowNodeDraft Node(string key, string kind, object? config = null) => new(key, kind, 1, JsonSerializer.SerializeToElement(config ?? new { }));
}
