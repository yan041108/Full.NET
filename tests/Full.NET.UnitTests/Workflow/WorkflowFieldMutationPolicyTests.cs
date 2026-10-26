using System.Text.Json;
using Full.NET.Modules.Workflow.Domain;

namespace Full.NET.UnitTests.Workflow;

/// <summary>本地字段动作必须显式授权、按固定类型求值且不因赋值次序污染同一输入快照。</summary>
[TestClass]
public sealed class WorkflowFieldMutationPolicyTests
{
    private static readonly WorkflowFormSchema Schema = new(1, 1, [new("main", [
        new("first", "text", false, new Dictionary<string, JsonElement>()),
        new("second", "text", false, new Dictionary<string, JsonElement>()),
        new("required", "text", true, new Dictionary<string, JsonElement>()),
        new("count", "integer", false, new Dictionary<string, JsonElement>()),
        new("flag", "switch", false, new Dictionary<string, JsonElement>())])]);

    [TestMethod]
    public void Simultaneous_copies_read_one_snapshot_and_leave_the_original_unchanged()
    {
        var config = Json("""{"fieldPolicies":{"first":"editable","second":"editable"},"mutationPolicy":{"assignments":[{"fieldKey":"first","sourceFieldKey":"second"},{"fieldKey":"second","sourceFieldKey":"first"}]}}""");
        Assert.IsTrue(WorkflowFieldMutationPolicy.TryRead("data.modify", config, Schema, out var policy));
        var input = Values("""{"first":"A","second":"B","required":"kept"}""");
        Assert.IsTrue(policy!.TryApply(input, out var output));
        Assert.AreEqual("B", output!.Values["first"].GetString()); Assert.AreEqual("A", output.Values["second"].GetString());
        Assert.AreEqual("A", input["first"].GetString()); Assert.AreEqual("B", input["second"].GetString());
        Assert.AreEqual(2, output.Patch.Count);
    }

    [TestMethod]
    [DataRow("{\"fieldPolicies\":{},\"mutationPolicy\":{\"assignments\":[{\"fieldKey\":\"first\",\"value\":\"new\"}]}}")]
    [DataRow("{\"fieldPolicies\":{\"first\":\"readOnly\"},\"mutationPolicy\":{\"assignments\":[{\"fieldKey\":\"first\",\"value\":\"new\"}]}}")]
    [DataRow("{\"fieldPolicies\":{\"first\":\"editable\",\"second\":\"hidden\"},\"mutationPolicy\":{\"assignments\":[{\"fieldKey\":\"first\",\"sourceFieldKey\":\"second\"}]}}")]
    [DataRow("{\"fieldPolicies\":{\"first\":\"editable\",\"count\":\"readOnly\"},\"mutationPolicy\":{\"assignments\":[{\"fieldKey\":\"first\",\"sourceFieldKey\":\"count\"}]}}")]
    [DataRow("{\"fieldPolicies\":{\"count\":\"editable\"},\"mutationPolicy\":{\"assignments\":[{\"fieldKey\":\"count\",\"value\":\"1\"}]}}")]
    [DataRow("{\"fieldPolicies\":{\"first\":\"editable\"},\"mutationPolicy\":{\"assignments\":[{\"fieldKey\":\"first\",\"value\":\"new\",\"sourceFieldKey\":\"first\"}]}}")]
    [DataRow("{\"fieldPolicies\":{\"first\":\"editable\"},\"mutationPolicy\":{\"assignments\":[{\"fieldKey\":\"first\",\"value\":\"new\"},{\"fieldKey\":\"first\",\"value\":\"other\"}]}}")]
    [DataRow("{\"fieldPolicies\":{\"first\":\"editable\"},\"mutationPolicy\":{\"assignments\":[{\"fieldKey\":\"first\",\"value\":\"new\",\"script\":\"run\"}]}}")]
    [DataRow("{\"fieldPolicies\":{\"first\":\"editable\"},\"mutationPolicy\":{\"assignments\":[]}}")]
    public void Unknown_ambiguous_untyped_or_ungranted_assignments_are_rejected(string json) =>
        Assert.IsFalse(WorkflowFieldMutationPolicy.TryRead("data.modify", Json(json), Schema, out _));

    [TestMethod]
    public void Clear_cannot_remove_schema_or_node_required_fields()
    {
        Assert.IsFalse(WorkflowFieldMutationPolicy.TryRead("data.clear", Json("""{"fieldPolicies":{"required":"editable"},"mutationPolicy":{"fieldKeys":["required"]}}"""), Schema, out _));
        Assert.IsFalse(WorkflowFieldMutationPolicy.TryRead("data.clear", Json("""{"fieldPolicies":{"first":"required"},"mutationPolicy":{"fieldKeys":["first"]}}"""), Schema, out _));
        Assert.IsTrue(WorkflowFieldMutationPolicy.TryRead("data.clear", Json("""{"fieldPolicies":{"first":"editable"},"mutationPolicy":{"fieldKeys":["first"]}}"""), Schema, out var policy));
        Assert.IsTrue(policy!.TryApply(Values("""{"first":"old","required":"kept"}"""), out var output));
        Assert.AreEqual(JsonValueKind.Null, output!.Patch["first"].ValueKind);
    }

    [TestMethod]
    public void Condition_reuses_bound_gateway_semantics_and_false_result_has_no_patch()
    {
        var config = Json("""{"fieldPolicies":{"first":"editable","flag":"readOnly"},"mutationPolicy":{"fieldKeys":["first"],"condition":{"fieldKey":"flag","operator":"equals","value":true}}}""");
        Assert.IsTrue(WorkflowFieldMutationPolicy.TryRead("data.clear", config, Schema, out var policy));
        Assert.IsTrue(policy!.TryApply(Values("""{"first":"old","required":"kept","flag":false}"""), out var skipped));
        Assert.IsFalse(skipped!.Matched); Assert.AreEqual(0, skipped.Patch.Count); Assert.AreEqual("old", skipped.Values["first"].GetString());
        Assert.IsTrue(policy.TryApply(Values("""{"first":"old","required":"kept","flag":true}"""), out var matched)); Assert.IsTrue(matched!.Matched);
        Assert.IsTrue(WorkflowFieldMutationPolicy.TryRead("data.clear", config, null, out var unbound));
        Assert.IsFalse(unbound!.TryApply(Values("""{"required":"kept"}"""), out _));
    }

    [TestMethod]
    public void Missing_or_invalid_copied_value_never_partially_applies_the_batch()
    {
        var config = Json("""{"fieldPolicies":{"first":"editable","required":"required"},"mutationPolicy":{"assignments":[{"fieldKey":"first","value":"changed"},{"fieldKey":"required","sourceFieldKey":"first"}]}}""");
        Assert.IsTrue(WorkflowFieldMutationPolicy.TryRead("data.modify", config, Schema, out var policy));
        var input = Values("""{"required":"kept"}"""); Assert.IsFalse(policy!.TryApply(input, out var output)); Assert.IsNull(output);
        Assert.AreEqual("kept", input["required"].GetString()); Assert.IsFalse(input.ContainsKey("first"));
    }

    private static JsonElement Json(string value) { using var document = JsonDocument.Parse(value); return document.RootElement.Clone(); }

    [TestMethod]
    public void Durable_field_wait_does_not_evaluate_downstream_condition_until_updated_values_are_available()
    {
        WorkflowNodeDraft Node(string key, string kind, object config) => new(key, kind, 1, JsonSerializer.SerializeToElement(config));
        var draft = new WorkflowDefinitionDraft(1, [
            Node("start", "start", new { nextNodeKeys = new[] { "modify" } }),
            Node("modify", "data.modify", new { nextNodeKeys = new[] { "route" }, fieldPolicies = new { flag = "editable" }, mutationPolicy = new { assignments = new[] { new { fieldKey = "flag", value = true } } } }),
            Node("route", "gateway.exclusive", new { nextNodeKeys = new[] { "yes", "no" }, branches = new[] { new { branchKey = "yes", nextNodeKey = "yes", condition = new { fieldKey = "flag", @operator = "equals", value = true } } }, defaultNextNodeKey = "no" }),
            Node("yes", "control.empty", new { nextNodeKeys = new[] { "end" } }), Node("no", "control.empty", new { nextNodeKeys = new[] { "end" } }), Node("end", "end", new { })], "automation");
        Assert.IsTrue(WorkflowDefinitionCompiler.Compile(draft, Schema).IsSuccess);
        Assert.IsTrue(WorkflowRuntimePlan.TryCreate(draft, Schema, out var plan));
        var input = Values("""{"required":"kept","flag":false}""");
        Assert.IsTrue(plan!.TryResolveStart(input, out var waiting));
        Assert.IsFalse(waiting.CompletesInstance); Assert.IsNull(waiting.NextHumanNodeKey);
        Assert.HasCount(0, waiting.AutomaticNodes);
        Assert.IsInstanceOfType<WorkflowFieldWaitPlan>(waiting.AutomaticWait);
        Assert.IsTrue(((WorkflowFieldWaitPlan)waiting.AutomaticWait!).Policy.TryApply(input, out var output));
        Assert.IsTrue(plan.TryResolveAfterAutomaticWait("modify", output!.Values, null, out var next));
        CollectionAssert.AreEqual(new[] { "route", "yes" }, next.AutomaticNodes.Select(node => node.NodeKey).ToArray());
        Assert.IsTrue(next.CompletesInstance);
    }
    private static IReadOnlyDictionary<string, JsonElement> Values(string value) => Json(value).EnumerateObject()
        .ToDictionary(item => item.Name, item => item.Value.Clone(), StringComparer.Ordinal);
}
