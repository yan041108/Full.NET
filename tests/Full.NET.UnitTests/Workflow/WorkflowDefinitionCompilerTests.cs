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
    public void Compile_rejects_invalid_graphs_with_stable_error_codes(
        string scenario,
        string expectedCode)
    {
        var result = WorkflowDefinitionCompiler.Compile(CreateInvalid(scenario));

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(expectedCode, result.ErrorCode);
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
        _ => throw new ArgumentOutOfRangeException(nameof(scenario)),
    };

    private static WorkflowNodeDraft Node(string key, string type, string json) =>
        new(key, type, 1, JsonDocument.Parse(json).RootElement.Clone());
}
