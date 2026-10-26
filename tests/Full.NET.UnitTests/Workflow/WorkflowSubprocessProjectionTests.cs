using System.Text.Json;
using Full.NET.Modules.Workflow.Domain;
using Full.NET.Modules.Workflow.Persistence;

namespace Full.NET.UnitTests.Workflow;

/// <summary>文件身份只能来自已选成功项，明细按闭合列和字段上限复制或追加。</summary>
[TestClass]
public sealed class WorkflowSubprocessProjectionTests
{
    private static IReadOnlyDictionary<string, JsonElement> FileConstraints() => new Dictionary<string, JsonElement> {
        ["maxCount"] = JsonSerializer.SerializeToElement(2), ["maxSizeBytes"] = JsonSerializer.SerializeToElement(1024), ["allowedExtensions"] = JsonSerializer.SerializeToElement(new[] { "png" }) };
    private static IReadOnlyDictionary<string, JsonElement> TableConstraints(string column = "label", int maximum = 2) => new Dictionary<string, JsonElement> {
        ["maxRows"] = JsonSerializer.SerializeToElement(maximum), ["columns"] = JsonSerializer.SerializeToElement(new[] { new { columnKey = column, fieldTypeKey = "text", required = true, constraints = new { } } }) };
    private static WorkflowFormSchema Schema(WorkflowFormField field) => new(1, 1, [new("main", [field])]);

    [TestMethod]
    public void Table_mapping_preserves_closed_columns_and_validates_target_rows()
    {
        var parent = Schema(new("source", "subtable", false, TableConstraints())); var child = Schema(new("input", "subtable", false, TableConstraints()));
        var policy = new WorkflowSubprocessPolicy(Guid.CreateVersion7(), new Dictionary<string, string> { ["input"] = "source" }, new Dictionary<string, string>(), new Dictionary<string, string> { ["source"] = "readOnly" });
        Assert.IsTrue(policy.MatchesSchemas(parent, child)); Assert.IsFalse(policy.MatchesSchemas(parent, Schema(new("input", "subtable", false, TableConstraints("other")))));
        var values = new Dictionary<string, JsonElement> { ["source"] = JsonSerializer.SerializeToElement(new[] { new { label = "row" } }) };
        Assert.IsTrue(policy.TryCreateInput(values, child, out var input)); Assert.AreEqual("{\"input\":[{\"label\":\"row\"}]}", input);
        values["source"] = JsonSerializer.SerializeToElement(new[] { new { label = "row", hidden = "secret" } }); Assert.IsFalse(policy.TryCreateInput(values, child, out _));
    }
    [TestMethod]
    public void File_projection_needs_actual_selected_result_hash_and_mapped_child_field()
    {
        var file = Guid.CreateVersion7(); var child = Guid.CreateVersion7(); var call = Guid.CreateVersion7(); var now = DateTimeOffset.UtcNow;
        var schema = Schema(new("returned", "attachment", false, FileConstraints())); var values = new Dictionary<string, JsonElement> { ["returned"] = JsonSerializer.SerializeToElement(new[] { file }) };
        var json = JsonSerializer.Serialize(values); var item = new WorkflowSubprocessItemRecord(Guid.CreateVersion7(), call, Guid.CreateVersion7(), "host", "single", 1,
            Guid.CreateVersion7(), "{}", WorkflowSubprocessBindings.Hash("{}"), child, "succeeded", json, WorkflowSubprocessBindings.Hash(json), 2, 2, now, now);
        var policy = new WorkflowSubprocessPolicy(Guid.CreateVersion7(), new Dictionary<string, string>(), new Dictionary<string, string> { ["returned"] = "childEvidence" }, new Dictionary<string, string> { ["returned"] = "editable" });
        var sources = WorkflowSubprocessAttachmentProjection.PrepareOutput(policy, schema, values, [item]);
        Assert.AreEqual(child, sources![file].InstanceId); Assert.AreEqual("childEvidence", sources[file].FieldKey);
        Assert.IsNull(WorkflowSubprocessAttachmentProjection.PrepareOutput(policy, schema, values, []));
        Assert.IsNull(WorkflowSubprocessAttachmentProjection.PrepareOutput(policy, schema, values, [item with { ResultHash = new string('a', 64) }]));
        Assert.IsNull(WorkflowSubprocessAttachmentProjection.PrepareOutput(policy, schema, values, [item with { StatusKey = "cancelled" }]));
    }
    [TestMethod]
    public void Append_is_stable_bounded_and_rejects_duplicate_files()
    {
        var first = Guid.CreateVersion7(); var second = Guid.CreateVersion7(); var schema = Schema(new("result", "attachment", false, FileConstraints()));
        WorkflowMultiInstanceOutput Output(string key, Guid id) => new(Guid.CreateVersion7(), key, new Dictionary<string, JsonElement> { ["result"] = JsonSerializer.SerializeToElement(new[] { id }) });
        var rules = new Dictionary<string, string> { ["result"] = "append" };
        Assert.IsTrue(WorkflowMultiInstanceOutputReducer.TryMerge(schema, ["result"], rules, [Output("item-0002", second), Output("item-0001", first)], out var merged));
        CollectionAssert.AreEqual(new[] { first, second }, merged["result"].EnumerateArray().Select(item => item.GetGuid()).ToArray());
        Assert.IsFalse(WorkflowMultiInstanceOutputReducer.TryMerge(schema, ["result"], rules, [Output("item-0001", first), Output("item-0002", first)], out _));
        var table = Schema(new("result", "subtable", false, TableConstraints(maximum: 1)));
        var rows = new[] { new WorkflowMultiInstanceOutput(Guid.CreateVersion7(), "a", new Dictionary<string, JsonElement> { ["result"] = JsonSerializer.SerializeToElement(new[] { new { label = "a" } }) }),
            new WorkflowMultiInstanceOutput(Guid.CreateVersion7(), "b", new Dictionary<string, JsonElement> { ["result"] = JsonSerializer.SerializeToElement(new[] { new { label = "b" } }) }) };
        Assert.IsFalse(WorkflowMultiInstanceOutputReducer.TryMerge(table, ["result"], rules, rows, out _));
    }
}
