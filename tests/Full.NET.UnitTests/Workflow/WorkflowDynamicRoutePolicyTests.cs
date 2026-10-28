using System.Text.Json;
using Full.NET.Modules.Workflow.Domain;

namespace Full.NET.UnitTests.Workflow;

/// <summary>规则顺序、无命中与决策失败是不同事实；任意目标或配置扩展必须拒绝。</summary>
[TestClass]
public sealed class WorkflowDynamicRoutePolicyTests
{
    [TestMethod]
    public void Rules_choose_the_first_match_and_keep_the_explicit_normal_fallback()
    {
        var config = Rules();
        Assert.IsTrue(WorkflowDynamicRoutePolicy.TryRead(config, Schema(), out var policy));
        Assert.IsTrue(policy!.TrySelect(Values("{\"amount\":5}"), out var selected)); Assert.AreEqual("retry", selected!.TargetKey);
        Assert.IsTrue(policy.TrySelect(Values("{\"amount\":0}"), out selected)); Assert.IsNull(selected);
        Assert.IsTrue(WorkflowDynamicRoutePolicy.TryRead(config, null, out var unbound));
        Assert.IsFalse(unbound!.TrySelect(Values("{\"amount\":5}"), out _));
    }

    [TestMethod]
    public void Decision_field_accepts_only_a_bound_machine_target_and_never_silently_falls_back()
    {
        var config = JsonSerializer.SerializeToElement(new { nextNodeKeys = new[] { "next" }, routePolicy = new { modeKey = "decision_field", decisionFieldKey = "decision",
            targets = new[] { new { targetKey = "retry", nodeKey = "review", regionKey = "review-round", maximumIterations = 3 } } } });
        Assert.IsTrue(WorkflowDynamicRoutePolicy.TryRead(config, Schema(), out var policy));
        Assert.IsTrue(policy!.TrySelect(Values("{\"decision\":\"retry\"}"), out var target)); Assert.AreEqual("review", target!.NodeKey);
        foreach (var json in new[] { "{}", "{\"decision\":null}", "{\"decision\":3}", "{\"decision\":\"unknown\"}", "{\"decision\":\"Retry\"}" })
            Assert.IsFalse(policy.TrySelect(Values(json), out _));
        var wrongSchema = Schema() with { Sections = [new("main", [new("decision", "text", false, new Dictionary<string, JsonElement>())])] };
        Assert.IsFalse(WorkflowDynamicRoutePolicy.TryRead(config, wrongSchema, out _));
    }

    [TestMethod]
    [DataRow(0, false)]
    [DataRow(1, true)]
    [DataRow(32, true)]
    [DataRow(33, false)]
    public void Iteration_budget_is_explicit_and_bounded(int maximum, bool accepted) =>
        Assert.AreEqual(accepted, WorkflowDynamicRoutePolicy.TryRead(Rules(maximum), Schema(), out _));

    [TestMethod]
    [DataRow("extra")]
    [DataRow("mode")]
    [DataRow("iterations")]
    [DataRow("duplicate")]
    [DataRow("regionLength")]
    public void Open_or_ambiguous_configuration_does_not_enter_the_route_protocol(string defect)
    {
        var json = Rules().GetRawText();
        json = defect switch {
            "extra" => json.Replace("\"modeKey\":\"rules\"", "\"modeKey\":\"rules\",\"url\":\"https://example.invalid\"", StringComparison.Ordinal),
            "mode" => json.Replace("\"rules\"", "\"script\"", StringComparison.Ordinal),
            "iterations" => json.Replace("\"maximumIterations\":3", "\"maximumIterations\":\"unlimited\"", StringComparison.Ordinal),
            "duplicate" => json.Replace("\"modeKey\":\"rules\"", "\"modeKey\":\"rules\",\"modeKey\":\"rules\"", StringComparison.Ordinal),
            _ => json.Replace("review-round", new string('r', 65), StringComparison.Ordinal) };
        using var document = JsonDocument.Parse(json);
        Assert.IsFalse(WorkflowDynamicRoutePolicy.TryRead(document.RootElement, Schema(), out _));
    }

    private static JsonElement Rules(int maximum = 3) => JsonSerializer.SerializeToElement(new { nextNodeKeys = new[] { "next" }, routePolicy = new { modeKey = "rules",
        targets = new[] { new { targetKey = "retry", nodeKey = "review", regionKey = "review-round", maximumIterations = maximum, condition = new { fieldKey = "amount", @operator = "greaterThan", value = 0 } },
            new { targetKey = "later", nodeKey = "laterReview", regionKey = "later-round", maximumIterations = maximum, condition = new { fieldKey = "amount", @operator = "greaterThan", value = 2 } } } } });
    internal static WorkflowFormSchema Schema() => new(1, 1, [new("main", [
        new("amount", "integer", false, new Dictionary<string, JsonElement>()),
        new("decision", "select", false, new Dictionary<string, JsonElement> { ["options"] = JsonSerializer.SerializeToElement(new[] { "retry", "later" }) })])]);
    internal static IReadOnlyDictionary<string, JsonElement> Values(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.EnumerateObject().ToDictionary(property => property.Name, property => property.Value.Clone(), StringComparer.Ordinal);
    }
}
