using System.Text.Json;
using Full.NET.Modules.Workflow.Domain;

namespace Full.NET.UnitTests.Workflow;

/// <summary>结果字段绑定真实Schema及显式写权限，不允许文本、文件或隐藏字段承载审批结果。</summary>
[TestClass]
public sealed class WorkflowResultCallbackPolicyTests
{
    [TestMethod]
    [DataRow("human.approval")]
    [DataRow("human.group_approval")]
    [DataRow("human.task")]
    [DataRow("human.vote")]
    [DataRow("human.claim")]
    public void Final_boolean_result_keeps_personal_input_and_unrelated_fields(string kind)
    {
        var config = JsonSerializer.SerializeToElement(new { resultCallbackFieldKey = "accepted", fieldPolicies = new { accepted = "editable" } });
        Assert.IsTrue(WorkflowResultCallbackPolicy.TryRead(kind, config, Schema("switch"), out var policy));
        var original = new Dictionary<string, JsonElement> { ["title"] = JsonSerializer.SerializeToElement("original"), ["accepted"] = JsonSerializer.SerializeToElement(false) };
        var passed = policy!.Apply(original, true); Assert.IsTrue(passed["accepted"].GetBoolean());
        Assert.IsFalse(original["accepted"].GetBoolean()); Assert.AreEqual("original", passed["title"].GetString());
        Assert.IsFalse(policy.Apply(passed, false)["accepted"].GetBoolean()); Assert.IsTrue(passed["accepted"].GetBoolean());
    }
    [TestMethod]
    [DataRow("readOnly", "switch")]
    [DataRow("hidden", "switch")]
    [DataRow("editable", "text")]
    [DataRow("required", "attachment")]
    public void Read_only_or_wrong_field_type_cannot_receive_system_result(string permission, string fieldType)
    {
        var config = JsonSerializer.SerializeToElement(new { resultCallbackFieldKey = "accepted", fieldPolicies = new Dictionary<string, string> { ["accepted"] = permission } });
        Assert.IsFalse(WorkflowResultCallbackPolicy.TryRead("human.approval", config, Schema(fieldType), out _));
    }
    [TestMethod]
    [DataRow("{\"resultCallbackFieldKey\":\"accepted\"}")]
    [DataRow("{\"resultCallbackFieldKey\":\"missing\",\"fieldPolicies\":{\"accepted\":\"editable\"}}")]
    [DataRow("{\"resultCallbackFieldKey\":\"accepted\",\"resultCallbackFieldKey\":\"accepted\",\"fieldPolicies\":{\"accepted\":\"editable\"}}")]
    [DataRow("{\"resultCallbackFieldKey\":\"accepted\",\"fieldPolicies\":{\"accepted\":\"hidden\",\"accepted\":\"editable\"}}")]
    public void Missing_or_ambiguous_grants_are_rejected(string json)
    {
        using var document = JsonDocument.Parse(json);
        Assert.IsFalse(WorkflowResultCallbackPolicy.TryRead("human.approval", document.RootElement, Schema("switch"), out _));
    }
    private static WorkflowFormSchema Schema(string fieldType) => new(1, 1, [new("main", [new("accepted", fieldType, false, new Dictionary<string, JsonElement>())])]);
}
