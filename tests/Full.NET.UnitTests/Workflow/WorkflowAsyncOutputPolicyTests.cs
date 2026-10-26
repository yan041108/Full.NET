using System.Text.Json;
using Full.NET.Modules.Workflow.Domain;

namespace Full.NET.UnitTests.Workflow;

/// <summary>异步输出合并只接纳原目标，其他字段的独立变更不能被旧快照覆盖。</summary>
[TestClass]
public sealed class WorkflowAsyncOutputPolicyTests
{
    private static readonly WorkflowFormSchema Schema = new(1, 1, [new("main", [
        new("title", "text", true, new Dictionary<string, JsonElement>()), new("count", "integer", false, new Dictionary<string, JsonElement>())])]);
    private static WorkflowConnectorPolicy Policy(bool mapped = true)
    {
        var config = JsonSerializer.SerializeToElement(new { fieldPolicies = new { title = "readOnly", count = "editable" }, connectorPolicy = new {
            connectorKey = "credit.check", connectorVersion = 1, inputFields = new { subject = "title" },
            outputFields = mapped ? new Dictionary<string, string> { ["count"] = "score" } : new Dictionary<string, string>() } });
        Assert.IsTrue(WorkflowConnectorPolicy.TryRead(config, Schema, out var result)); return result!;
    }
    [TestMethod]
    public void Unrelated_changes_are_preserved_and_the_request_snapshot_is_not_mutated()
    {
        var original = Values("""{"title":"original","count":1}"""); var current = Values("""{"title":"independent","count":1}""");
        Assert.IsTrue(WorkflowAsyncOutputPolicy.TryMap(Policy(), original, current, """{"score":7}""", out var result));
        Assert.AreEqual("independent", result!.Values["title"].GetString()); Assert.AreEqual(7, result.Values["count"].GetInt32()); Assert.HasCount(1, result.Patch);
        Assert.AreEqual("original", original["title"].GetString()); Assert.AreEqual(1, current["count"].GetInt32());
    }
    [TestMethod]
    [DataRow("{\"title\":\"independent\",\"count\":2}")]
    [DataRow("{\"title\":\"independent\"}")]
    public void A_changed_or_removed_target_cannot_be_overwritten(string changed)
    {
        Assert.IsFalse(WorkflowAsyncOutputPolicy.TryMap(Policy(), Values("""{"title":"original","count":1}"""), Values(changed), """{"score":7}""", out var result)); Assert.IsNull(result);
    }
    [TestMethod]
    public void No_mapping_still_requires_completion_but_does_not_modify_the_current_form()
    {
        Assert.IsTrue(WorkflowAsyncOutputPolicy.TryMap(Policy(mapped: false), Values("""{"title":"original","count":1}"""), Values("""{"title":"independent","count":9}"""), """{"score":7}""", out var result));
        Assert.IsFalse(result!.Matched); Assert.HasCount(0, result.Patch); Assert.AreEqual(9, result.Values["count"].GetInt32());
        Assert.IsFalse(WorkflowAsyncOutputPolicy.TryMap(Policy(), Values("""{"title":"original","count":1}"""), Values("""{"title":"independent","count":1}"""), """{"score":"seven"}""", out _));
    }
    private static Dictionary<string, JsonElement> Values(string json)
    { using var document = JsonDocument.Parse(json); return document.RootElement.EnumerateObject().ToDictionary(property => property.Name, property => property.Value.Clone(), StringComparer.Ordinal); }
}
