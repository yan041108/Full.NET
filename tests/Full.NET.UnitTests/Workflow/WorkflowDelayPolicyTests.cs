using System.Text.Json;
using Full.NET.Modules.Workflow.Domain;

namespace Full.NET.UnitTests.Workflow;

/// <summary>延时配置和等待边界不能提前求值后继、接受歧义时间或借用人工待办。</summary>
[TestClass]
public sealed class WorkflowDelayPolicyTests
{
    [TestMethod]
    [DataRow("second", 2L)]
    [DataRow("minute", 120L)]
    [DataRow("hour", 7200L)]
    [DataRow("day", 172800L)]
    public void Relative_wall_clock_units_are_resolved_once(string unit, long seconds)
    {
        Assert.IsTrue(WorkflowDelayPolicy.TryRead(Config(new { modeKey = "relative", timeZoneKey = "UTC", duration = 2, unitKey = unit }), null, out var policy));
        var now = DateTimeOffset.Parse("2026-10-26T00:00:00Z");
        Assert.IsTrue(policy!.TryResolve(new Dictionary<string, JsonElement>(), now, out var due));
        Assert.AreEqual(now.AddSeconds(seconds), due);
    }

    [TestMethod]
    [DataRow("{\"modeKey\":\"relative\",\"timeZoneKey\":\"UTC\",\"duration\":0,\"unitKey\":\"second\"}")]
    [DataRow("{\"modeKey\":\"relative\",\"timeZoneKey\":\"UTC\",\"duration\":367,\"unitKey\":\"day\"}")]
    [DataRow("{\"modeKey\":\"relative\",\"timeZoneKey\":\"UTC\",\"duration\":9223372036854775807,\"unitKey\":\"day\"}")]
    [DataRow("{\"modeKey\":\"relative\",\"timeZoneKey\":\"UTC\",\"duration\":1.5,\"unitKey\":\"second\"}")]
    [DataRow("{\"modeKey\":\"relative\",\"timeZoneKey\":\"UTC\",\"duration\":\"1\",\"unitKey\":\"second\"}")]
    [DataRow("{\"modeKey\":\"relative\",\"modeKey\":\"absolute\",\"timeZoneKey\":\"UTC\",\"duration\":1,\"unitKey\":\"second\"}")]
    [DataRow("{\"modeKey\":\"relative\",\"timeZoneKey\":\"unknown/zone\",\"duration\":1,\"unitKey\":\"second\"}")]
    [DataRow("{\"modeKey\":\"relative\",\"timeZoneKey\":\"UTC\",\"duration\":1,\"unitKey\":\"business_day\"}")]
    [DataRow("{\"modeKey\":\"absolute\",\"timeZoneKey\":\"UTC\",\"at\":\"2026-10-26T01:00:00\"}")]
    [DataRow("{\"modeKey\":\"absolute\",\"timeZoneKey\":\"UTC\",\"at\":\"2026-10-26T01:00:00Z\",\"fieldKey\":\"when\"}")]
    [DataRow("{\"modeKey\":\"relative\",\"timeZoneKey\":\"UTC\",\"duration\":1,\"unitKey\":\"second\",\"calendarId\":\"unfixed\"}")]
    public void Ambiguous_unbounded_or_unknown_configuration_is_rejected(string json) =>
        Assert.IsFalse(WorkflowDelayPolicy.TryRead(JsonSerializer.SerializeToElement(new { delayPolicy = JsonSerializer.Deserialize<JsonElement>(json) }), null, out _));

    [TestMethod]
    public void Absolute_field_binds_datetime_schema_and_requires_offset_at_activation()
    {
        var config = Config(new { modeKey = "absolute", timeZoneKey = "UTC", fieldKey = "when" });
        WorkflowFormSchema Schema(string type) => new(1, 1, [new("main", [new("when", type, false, new Dictionary<string, JsonElement>())])]);
        Assert.IsFalse(WorkflowDelayPolicy.TryRead(config, Schema("text"), out _));
        Assert.IsTrue(WorkflowDelayPolicy.TryRead(config, Schema("datetime"), out var policy));
        Assert.IsFalse(policy!.TryResolve(new Dictionary<string, JsonElement>(), DateTimeOffset.UtcNow, out _));
        Assert.IsFalse(policy.TryResolve(new Dictionary<string, JsonElement> { ["when"] = JsonSerializer.SerializeToElement("2026-10-26T01:00:00") }, DateTimeOffset.UtcNow, out _));
        Assert.IsTrue(policy.TryResolve(new Dictionary<string, JsonElement> { ["when"] = JsonSerializer.SerializeToElement("2026-10-26T08:00:00+08:00") }, DateTimeOffset.UtcNow, out var due));
        Assert.AreEqual(DateTimeOffset.Parse("2026-10-26T00:00:00Z"), due);
    }

    [TestMethod]
    public void Automatic_wait_has_no_todo_and_resolves_successor_only_after_wakeup()
    {
        var draft = new WorkflowDefinitionDraft(1, [
            Node("start", "start", new { nextNodeKeys = new[] { "delay" } }),
            Node("delay", "timer.delay", new { nextNodeKeys = new[] { "route" }, delayPolicy = new { modeKey = "relative", timeZoneKey = "UTC", duration = 1, unitKey = "second" } }),
            Node("route", "gateway.exclusive", new { nextNodeKeys = new[] { "yes", "no" }, branches = new[] { new { branchKey = "yes", nextNodeKey = "yes", condition = new { fieldKey = "flag", @operator = "equals", value = true } } }, defaultNextNodeKey = "no" }),
            Node("yes", "control.empty", new { nextNodeKeys = new[] { "end" } }),
            Node("no", "control.empty", new { nextNodeKeys = new[] { "end" } }), Node("end", "end", new { })], "automation");
        var schema = new WorkflowFormSchema(1, 1, [new("main", [new("flag", "switch", false, new Dictionary<string, JsonElement>())])]);
        Assert.IsTrue(WorkflowRuntimePlan.TryCreate(draft, schema, out var plan));
        Assert.IsTrue(plan!.TryResolveStart(out var transition));
        Assert.IsNotNull(transition.AutomaticWait);
        Assert.AreEqual("delay", transition.AutomaticWait.NodeKey);
        Assert.IsNull(transition.NextHumanNodeKey); Assert.IsFalse(transition.CompletesInstance);
        Assert.HasCount(0, transition.AutomaticNodes);
        var values = new Dictionary<string, JsonElement> { ["flag"] = JsonSerializer.SerializeToElement(true) };
        Assert.IsTrue(plan.TryResolveAfterAutomaticWait("delay", values, null, out var after));
        Assert.IsTrue(after.CompletesInstance); Assert.IsTrue(after.AutomaticNodes.Any(node => node.NodeKey == "yes"));
        Assert.IsFalse(plan.TryResolveAfterAutomaticWait("start", values, null, out _));
    }

    private static JsonElement Config(object policy) => JsonSerializer.SerializeToElement(new { delayPolicy = policy });
    private static WorkflowNodeDraft Node(string key, string type, object config) => new(key, type, 1, JsonSerializer.SerializeToElement(config));
}
