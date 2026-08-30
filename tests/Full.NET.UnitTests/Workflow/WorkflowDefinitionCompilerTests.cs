using System.Text.Json;
using Full.NET.Modules.Workflow.Contracts;
using Full.NET.Modules.Workflow.Domain;

namespace Full.NET.UnitTests.Workflow;

[TestClass]
public sealed class WorkflowDefinitionCompilerTests
{
    [TestMethod]
    public void Compile_produces_stable_canonical_json_and_hash_for_reordered_object_keys()
    {
        var first = CompileValid("{\"label\":\"审批\",\"nextNodeKeys\":[\"end\"]}");
        var second = CompileValid("{\"nextNodeKeys\":[\"end\"],\"label\":\"审批\"}");

        Assert.IsTrue(first.IsSuccess);
        Assert.IsTrue(second.IsSuccess);
        Assert.AreEqual(first.Value!.CanonicalJson, second.Value!.CanonicalJson);
        Assert.AreEqual(first.Value.ContentHash, second.Value.ContentHash);
    }

    [TestMethod]
    [DataRow("unknown", WorkflowErrorCodes.DefinitionNodeTypeUnknown)]
    [DataRow("duplicate", WorkflowErrorCodes.DefinitionNodeKeyDuplicate)]
    [DataRow("dangling", WorkflowErrorCodes.DefinitionReferenceDangling)]
    [DataRow("unreachable", WorkflowErrorCodes.DefinitionNodeUnreachable)]
    [DataRow("no-end", WorkflowErrorCodes.DefinitionEndMissing)]
    [DataRow("back-edge", WorkflowErrorCodes.DefinitionBackEdgeIllegal)]
    [DataRow("definition-schema", WorkflowErrorCodes.DefinitionSchemaUnsupported)]
    [DataRow("node-schema", WorkflowErrorCodes.DefinitionSchemaUnsupported)]
    public void Compile_rejects_invalid_graphs_with_stable_error_codes(
        string scenario,
        string expectedCode)
    {
        var result = WorkflowDefinitionCompiler.Compile(CreateInvalid(scenario));

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(expectedCode, result.ErrorCode);
    }

    [TestMethod]
    public void Compile_with_form_schema_materializes_complete_node_field_policies()
    {
        var draft = new WorkflowDefinitionDraft(1,
        [
            Node("start", "start", "{\"nextNodeKeys\":[\"approve\"]}"),
            Node("approve", "human.approval",
                "{\"nextNodeKeys\":[\"end\"],\"fieldPolicies\":{\"secret\":\"hidden\",\"reason\":\"readOnly\"}}"),
            Node("end", "end", "{}"),
        ]);
        var schema = FormSchema(
            FormField("reason", required: false),
            FormField("secret", required: false),
            FormField("decision", required: true));

        var result = WorkflowDefinitionCompiler.Compile(draft, schema);

        Assert.IsTrue(result.IsSuccess);
        using var document = JsonDocument.Parse(result.Value!.CanonicalJson);
        var approval = document.RootElement.GetProperty("nodes").EnumerateArray()
            .Single(node => node.GetProperty("nodeKey").GetString() == "approve");
        var policies = approval.GetProperty("config").GetProperty("fieldPolicies");
        Assert.AreEqual("required", policies.GetProperty("decision").GetString());
        Assert.AreEqual("readOnly", policies.GetProperty("reason").GetString());
        Assert.AreEqual("hidden", policies.GetProperty("secret").GetString());
    }

    [TestMethod]
    [DataRow("{\"nextNodeKeys\":[\"end\"],\"fieldPolicies\":{\"missing\":\"editable\"}}")]
    [DataRow("{\"nextNodeKeys\":[\"end\"],\"fieldPolicies\":{\"reason\":\"ownerOnly\"}}")]
    [DataRow("{\"nextNodeKeys\":[\"end\"],\"fieldPolicies\":[]}")]
    public void Compile_rejects_unknown_or_malformed_node_field_policies(string approvalConfig)
    {
        var result = WorkflowDefinitionCompiler.Compile(
            new WorkflowDefinitionDraft(1,
            [
                Node("start", "start", "{\"nextNodeKeys\":[\"approve\"]}"),
                Node("approve", "human.approval", approvalConfig),
                Node("end", "end", "{}"),
            ]),
            FormSchema(FormField("reason", required: false)));

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(WorkflowErrorCodes.DefinitionFieldPolicyInvalid, result.ErrorCode);
    }

    private static WorkflowCompilationResult CompileValid(string approvalConfig) =>
        WorkflowDefinitionCompiler.Compile(new WorkflowDefinitionDraft(1,
        [
            Node("start", "start", "{\"nextNodeKeys\":[\"approve\"]}"),
            Node("approve", "human.approval", approvalConfig),
            Node("end", "end", "{}"),
        ]));

    private static WorkflowDefinitionDraft CreateInvalid(string scenario) => scenario switch
    {
        "unknown" => new(1,
        [
            Node("start", "start", "{\"nextNodeKeys\":[\"mystery\"]}"),
            Node("mystery", "script", "{\"nextNodeKeys\":[\"end\"]}"),
            Node("end", "end", "{}"),
        ]),
        "duplicate" => new(1,
        [
            Node("start", "start", "{\"nextNodeKeys\":[\"end\"]}"),
            Node("start", "end", "{}"),
        ]),
        "dangling" => new(1,
        [
            Node("start", "start", "{\"nextNodeKeys\":[\"missing\"]}"),
            Node("end", "end", "{}"),
        ]),
        "unreachable" => new(1,
        [
            Node("start", "start", "{\"nextNodeKeys\":[\"end\"]}"),
            Node("orphan", "notify.cc", "{\"nextNodeKeys\":[\"end\"]}"),
            Node("end", "end", "{}"),
        ]),
        "no-end" => new(1,
        [
            Node("start", "start", "{\"nextNodeKeys\":[\"approve\"]}"),
            Node("approve", "human.approval", "{}"),
        ]),
        "back-edge" => new(1,
        [
            Node("start", "start", "{\"nextNodeKeys\":[\"approve\"]}"),
            Node("approve", "human.approval", "{\"nextNodeKeys\":[\"start\",\"end\"]}"),
            Node("end", "end", "{}"),
        ]),
        "definition-schema" => new(2,
        [
            Node("start", "start", "{\"nextNodeKeys\":[\"end\"]}"),
            Node("end", "end", "{}"),
        ]),
        "node-schema" => new(1,
        [
            Node("start", "start", "{\"nextNodeKeys\":[\"end\"]}") with { NodeSchemaVersion = 2 },
            Node("end", "end", "{}"),
        ]),
        _ => throw new ArgumentOutOfRangeException(nameof(scenario)),
    };

    private static WorkflowNodeDraft Node(string key, string type, string json) =>
        new(key, type, 1, JsonDocument.Parse(json).RootElement.Clone());

    private static WorkflowFormSchema FormSchema(params WorkflowFormField[] fields) =>
        new(1, 1, [new WorkflowFormSection("main", fields)]);

    private static WorkflowFormField FormField(string key, bool required) =>
        new(key, "text", required, new Dictionary<string, JsonElement>());
}
