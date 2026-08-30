using System.Text.Json;
using Full.NET.Modules.Workflow.Domain;

namespace Full.NET.UnitTests.Workflow;

[TestClass]
public sealed class WorkflowNodeFieldPolicyTests
{
    [TestMethod]
    public void CreateView_removes_hidden_fields_from_schema_submission_and_visible_policies()
    {
        var schema = Schema();
        var policy = Resolve(schema);
        var values = ParseObject("{\"reason\":\"original\",\"secret\":\"classified\",\"decision\":\"yes\"}");

        var result = policy.CreateView(schema, values);

        CollectionAssert.AreEqual(
            new[] { "reason", "decision" },
            result.Schema.Sections.SelectMany(section => section.Fields)
                .Select(field => field.FieldKey).ToArray());
        CollectionAssert.AreEquivalent(
            new[] { "reason", "decision" },
            result.Values.Keys.ToArray());
        CollectionAssert.AreEquivalent(
            new[] { "reason", "decision" },
            result.FieldPolicies.Keys.ToArray());
    }

    [TestMethod]
    [DataRow("{\"reason\":\"changed\"}")]
    [DataRow("{\"secret\":\"leaked\"}")]
    [DataRow("{\"unknown\":\"value\"}")]
    public void TryApplyPatch_rejects_read_only_hidden_and_unknown_fields(string patchJson)
    {
        var schema = Schema();
        var policy = Resolve(schema);

        var accepted = policy.TryApplyPatch(
            schema,
            ParseObject("{\"reason\":\"original\",\"secret\":\"classified\",\"decision\":\"yes\"}"),
            ParseElement(patchJson),
            out _);

        Assert.IsFalse(accepted);
    }

    [TestMethod]
    public void TryApplyPatch_enforces_node_required_policy_after_merge()
    {
        var schema = Schema();
        var policy = Resolve(schema);
        var values = ParseObject("{\"reason\":\"original\",\"secret\":\"classified\"}");

        Assert.IsFalse(policy.TryApplyPatch(schema, values, ParseElement("{}"), out _));
        Assert.IsTrue(policy.TryApplyPatch(
            schema, values, ParseElement("{\"decision\":\"approved\"}"), out var patched));
        Assert.AreEqual("approved", patched!["decision"].GetString());
    }

    private static WorkflowNodeFieldPolicy Resolve(WorkflowFormSchema schema)
    {
        var compiled = WorkflowDefinitionCompiler.Compile(
            new WorkflowDefinitionDraft(1,
            [
                Node("start", "start", "{\"nextNodeKeys\":[\"approve\"]}"),
                Node("approve", "human.approval",
                    "{\"nextNodeKeys\":[\"end\"],\"fieldPolicies\":{\"reason\":\"readOnly\",\"secret\":\"hidden\",\"decision\":\"required\"}}"),
                Node("end", "end", "{}"),
            ]),
            schema);
        Assert.IsTrue(compiled.IsSuccess);
        Assert.IsTrue(WorkflowNodeFieldPolicy.TryResolve(
            compiled.Value!.CanonicalJson, "approve", schema, out var policy));
        return policy!;
    }

    private static WorkflowFormSchema Schema() =>
        new(1, 1,
        [
            new WorkflowFormSection("main",
            [
                Field("reason"),
                Field("secret"),
                Field("decision"),
            ]),
        ]);

    private static WorkflowFormField Field(string key) =>
        new(key, "text", false, new Dictionary<string, JsonElement>());

    private static WorkflowNodeDraft Node(string key, string type, string json) =>
        new(key, type, 1, ParseElement(json));

    private static JsonElement ParseElement(string json) =>
        JsonDocument.Parse(json).RootElement.Clone();

    private static Dictionary<string, JsonElement> ParseObject(string json) =>
        ParseElement(json).EnumerateObject()
            .ToDictionary(property => property.Name, property => property.Value.Clone(), StringComparer.Ordinal);
}
