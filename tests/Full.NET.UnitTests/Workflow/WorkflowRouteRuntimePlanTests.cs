using System.Text.Json;
using Full.NET.Modules.Workflow.Domain;

namespace Full.NET.UnitTests.Workflow;

/// <summary>目录准入后路由仍只产生固定输入等待点，不把路由引用混入正常DAG。</summary>
[TestClass]
public sealed class WorkflowRouteRuntimePlanTests
{
    [TestMethod]
    public void Published_route_is_a_durable_boundary_with_a_schema_bound_plan()
    {
        var draft = Draft();
        Assert.IsTrue(WorkflowRuntimePlan.TryCreate(draft, WorkflowDynamicRoutePolicyTests.Schema(), out var plan));
        var values = WorkflowDynamicRoutePolicyTests.Values("{\"amount\":0}");
        Assert.IsTrue(plan!.TryResolveStart(values, out var transition));
        Assert.IsInstanceOfType<WorkflowRouteWaitPlan>(transition.AutomaticWait);
        Assert.AreEqual("review", transition.AutomaticNodes.Single().NodeKey);
        Assert.IsTrue(plan.DynamicRoutes.Routes["route"].Policy.TrySelect(values, out var target)); Assert.IsNull(target);
        Assert.IsTrue(plan.TryResolveContinuation("route", values, null, out var resumed));
        Assert.AreEqual(0, resumed.AutomaticNodes.Count); Assert.IsInstanceOfType<WorkflowRouteWaitPlan>(resumed.AutomaticWait);
    }

    [TestMethod]
    public void A_route_reference_does_not_disable_cycle_or_unknown_property_validation()
    {
        var draft = Draft();
        var cyclic = draft with { Nodes = draft.Nodes.Select(node => node.NodeKey == "route" ? node with {
            Config = JsonSerializer.SerializeToElement(new { nextNodeKeys = new[] { "review" }, routePolicy = node.Config.GetProperty("routePolicy") }) } : node).ToArray() };
        Assert.IsFalse(WorkflowRuntimePlan.TryCreate(cyclic, WorkflowDynamicRoutePolicyTests.Schema(), out _));
        var open = draft with { Nodes = draft.Nodes.Select(node => node.NodeKey == "route" ? node with {
            Config = JsonSerializer.SerializeToElement(new { nextNodeKeys = new[] { "end" }, routePolicy = node.Config.GetProperty("routePolicy"), script = "return target" }) } : node).ToArray() };
        Assert.IsFalse(WorkflowRuntimePlan.TryCreate(open, WorkflowDynamicRoutePolicyTests.Schema(), out _));
    }

    private static WorkflowDefinitionDraft Draft() => new(1, [Node("start", "start", new { nextNodeKeys = new[] { "review" } }),
        Node("review", "control.empty", new { nextNodeKeys = new[] { "route" } }), Node("route", "control.route", new {
            nextNodeKeys = new[] { "end" }, routePolicy = new { modeKey = "rules", targets = new[] { new { targetKey = "retry", nodeKey = "review", regionKey = "round", maximumIterations = 2,
                condition = new { fieldKey = "amount", @operator = "greaterThan", value = 0 } } } } }), Node("end", "end", new { })], "automation");
    private static WorkflowNodeDraft Node(string key, string type, object config) => new(key, type, 1, JsonSerializer.SerializeToElement(config));
}
