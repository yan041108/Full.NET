using System.Text.Json;
using Full.NET.Modules.Workflow.Domain;
using Full.NET.Modules.Workflow.Features.ManageDefinitions;
using Full.NET.Modules.Workflow.Features.ManageOperations;
using Full.NET.Modules.Workflow.Persistence;

namespace Full.NET.UnitTests.Workflow;

/// <summary>仿真按固定类型求值并保留人工/外部屏障，运营不将将来、暂停或失败工作标成可领取。</summary>
[TestClass]
public sealed class WorkflowR5Tests
{
    [TestMethod]
    [DataRow(0, "review")]
    [DataRow(9, "review")]
    [DataRow(10, "approve")]
    [DataRow(100, "approve")]
    public void Simulation_uses_fixed_integer_schema_and_ordered_runtime_gateway(int amount, string expected)
    {
        var (draft, schema) = Assets();
        Assert.IsTrue(WorkflowSimulationService.TryEvaluate(draft, schema, new Dictionary<string, JsonElement> { ["amount"] = JsonSerializer.SerializeToElement(amount) }, out _, out var transition));
        Assert.AreEqual(expected, transition.NextHumanNodeKey); Assert.IsFalse(transition.CompletesInstance);
        Assert.AreEqual(amount >= 10 ? "large" : "default", transition.AutomaticNodes.Single().OutcomeKey);
    }

    [TestMethod]
    public void Simulation_rejects_wrong_scalar_unknown_or_required_missing_values()
    {
        var (draft, schema) = Assets();
        foreach (var json in new[] { "{}", "{\"amount\":\"10\"}", "{\"amount\":101}", "{\"amount\":10,\"tenantId\":1}" })
        {
            var values = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json)!;
            Assert.IsFalse(WorkflowSimulationService.TryEvaluate(draft, schema, values, out _, out _));
        }
        var timer = new WorkflowDefinitionDraft(1, [Node("start", "start", new { nextNodeKeys = new[] { "delay" } }),
            Node("delay", "timer.delay", new { nextNodeKeys = new[] { "end" }, delayPolicy = new { modeKey = "relative", timeZoneKey = "UTC", duration = 60, unitKey = "second" } }), Node("end", "end", new { })], "automation");
        Assert.IsTrue(WorkflowSimulationService.TryEvaluate(timer, schema, new Dictionary<string, JsonElement> { ["amount"] = JsonSerializer.SerializeToElement(10) }, out _, out var waiting));
        Assert.IsNotNull(waiting.AutomaticWait); Assert.AreEqual("delay", waiting.AutomaticWait.NodeKey); Assert.IsFalse(waiting.CompletesInstance);
        var automatic = new WorkflowDefinitionDraft(1, [Node("start", "start", new { nextNodeKeys = new[] { "end" } }), Node("end", "end", new { })], "automation");
        Assert.IsTrue(WorkflowSimulationService.TryEvaluate(automatic, schema, new Dictionary<string, JsonElement> { ["amount"] = JsonSerializer.SerializeToElement(10) }, out _, out var result));
        Assert.IsTrue(result.CompletesInstance); Assert.AreEqual("end", result.TerminalNodeKey);
    }

    [TestMethod]
    [DataRow("active", "pending", -1, null, true)]
    [DataRow("active", "pending", 1, null, false)]
    [DataRow("active", "leased", -1, -1, true)]
    [DataRow("active", "leased", -1, 1, false)]
    [DataRow("active", "leased", -1, null, false)]
    [DataRow("suspended", "pending", -1, null, false)]
    [DataRow("active", "faulted", -1, null, false)]
    public void Operations_readiness_preserves_clock_pause_lease_and_fault_boundaries(string instance, string status, int dueSeconds, int? leaseSeconds, bool expected)
    {
        var now = DateTimeOffset.Parse("2026-10-29T00:00:00Z");
        var row = new WorkflowOperationsWorkRecord(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), "r5", "next", status, instance,
            now.AddSeconds(dueSeconds), leaseSeconds is { } seconds ? now.AddSeconds(seconds) : null, 1);
        Assert.AreEqual(expected, WorkflowOperationsService.IsReady(row, now));
    }

    private static (WorkflowDefinitionDraft, WorkflowFormSchema) Assets()
    {
        var schema = new WorkflowFormSchema(1, 1, [new("main", [new("amount", "integer", true, new Dictionary<string, JsonElement> {
            ["minimum"] = JsonSerializer.SerializeToElement(0), ["maximum"] = JsonSerializer.SerializeToElement(100) })])]);
        var draft = new WorkflowDefinitionDraft(1, [Node("start", "start", new { nextNodeKeys = new[] { "gateway" } }),
            Node("gateway", "gateway.exclusive", new { nextNodeKeys = new[] { "approve", "review" }, branches = new[] { new { branchKey = "large", nextNodeKey = "approve", condition = new { fieldKey = "amount", @operator = "greaterThanOrEqual", value = 10 } } }, defaultNextNodeKey = "review" }),
            Node("approve", "human.approval", new { nextNodeKeys = new[] { "end" } }), Node("review", "human.approval", new { nextNodeKeys = new[] { "end" } }), Node("end", "end", new { })]);
        return (draft, schema);
    }
    private static WorkflowNodeDraft Node(string key, string type, object config) => new(key, type, 1, JsonSerializer.SerializeToElement(config));
}
