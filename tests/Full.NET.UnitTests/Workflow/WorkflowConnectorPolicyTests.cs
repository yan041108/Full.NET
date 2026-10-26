using System.Text.Json;
using Full.NET.Modules.Workflow.Domain;

namespace Full.NET.UnitTests.Workflow;

/// <summary>外部活动只映射授权字段，不接受连接配置、脚本路径或无类型的回填。</summary>
[TestClass]
public sealed class WorkflowConnectorPolicyTests
{
    private static readonly WorkflowFormSchema Schema = new(1, 1, [new("main", [
        new("title", "text", true, new Dictionary<string, JsonElement>()),
        new("count", "integer", false, new Dictionary<string, JsonElement>()),
        new("private", "text", false, new Dictionary<string, JsonElement>())])]);
    private const string Valid = """{"fieldPolicies":{"title":"readOnly","count":"editable"},"connectorPolicy":{"connectorKey":"credit.check","connectorVersion":2,"inputFields":{"subject":"title"},"outputFields":{"count":"score"}}}""";

    [TestMethod]
    public void Input_projection_is_bounded_and_does_not_send_unmapped_fields()
    {
        Assert.IsTrue(WorkflowConnectorPolicy.TryRead(Json(Valid), Schema, out var policy));
        var input = Values("""{"title":"Example","private":"do not send"}""");
        Assert.IsTrue(policy!.TryCreateInput(input, out var body)); Assert.AreEqual("""{"subject":"Example"}""", body);
        Assert.IsFalse(policy.TryCreateInput(Values("""{"private":"only"}"""), out _));
    }

    [TestMethod]
    public void Output_projection_uses_schema_write_access_and_preserves_the_fixed_input()
    {
        Assert.IsTrue(WorkflowConnectorPolicy.TryRead(Json(Valid), Schema, out var policy));
        var input = Values("""{"title":"Example","count":1,"private":"kept"}""");
        Assert.IsTrue(policy!.TryMapOutput(input, """{"score":7,"ignored":"server data"}""", out var output));
        Assert.AreEqual(7, output!.Values["count"].GetInt32()); Assert.AreEqual(1, input["count"].GetInt32());
        Assert.HasCount(1, output.Patch); Assert.AreEqual("kept", output.Values["private"].GetString());
    }

    [TestMethod]
    [DataRow("{\"score\":\"7\"}")]
    [DataRow("{\"score\":1.5}")]
    [DataRow("{\"other\":7}")]
    [DataRow("{\"score\":7,\"score\":8}")]
    [DataRow("[7]")]
    public void Invalid_or_ambiguous_results_never_partially_apply(string result)
    {
        Assert.IsTrue(WorkflowConnectorPolicy.TryRead(Json(Valid), Schema, out var policy));
        Assert.IsFalse(policy!.TryMapOutput(Values("""{"title":"Example"}"""), result, out var output)); Assert.IsNull(output);
    }

    [TestMethod]
    [DataRow("\"connectorVersion\":2", "\"connectorVersion\":\"2\"")]
    [DataRow("\"connectorVersion\":2", "\"connectorVersion\":0")]
    [DataRow("\"inputFields\":{\"subject\":\"title\"}", "\"inputFields\":{\"subject\":\"private\"}")]
    [DataRow("\"outputFields\":{\"count\":\"score\"}", "\"outputFields\":{\"title\":\"score\"}")]
    [DataRow("\"connectorKey\":\"credit.check\"", "\"connectorKey\":\"credit.check\",\"url\":\"https://example.test\"")]
    [DataRow("\"outputFields\":{\"count\":\"score\"}", "\"outputFields\":{\"count\":\"result.score\",\"count\":\"other\"}")]
    public void Unknown_config_ungranted_fields_and_non_numeric_versions_fail_closed(string old, string replacement) =>
        Assert.IsFalse(WorkflowConnectorPolicy.TryRead(Json(Valid.Replace(old, replacement, StringComparison.Ordinal)), Schema, out _));

    [TestMethod]
    public void Required_target_cannot_be_cleared_and_unbound_policy_cannot_execute()
    {
        var config = Json(Valid.Replace("\"title\":\"readOnly\"", "\"title\":\"required\"", StringComparison.Ordinal)
            .Replace("\"outputFields\":{\"count\":\"score\"}", "\"outputFields\":{\"title\":\"name\"}", StringComparison.Ordinal));
        Assert.IsTrue(WorkflowConnectorPolicy.TryRead(config, Schema, out var policy));
        Assert.IsFalse(policy!.TryMapOutput(Values("""{"title":"Example"}"""), """{"name":null}""", out _));
        Assert.IsTrue(WorkflowConnectorPolicy.TryRead(config, null, out var unbound));
        Assert.IsFalse(unbound!.TryCreateInput(Values("""{"title":"Example"}"""), out _));
        Assert.IsFalse(unbound.TryMapOutput(Values("""{"title":"Example"}"""), """{"name":"Result"}""", out _));
    }
    private static JsonElement Json(string value) { using var document = JsonDocument.Parse(value); return document.RootElement.Clone(); }
    private static IReadOnlyDictionary<string, JsonElement> Values(string value) => Json(value).EnumerateObject().ToDictionary(item => item.Name, item => item.Value.Clone(), StringComparer.Ordinal);
}
