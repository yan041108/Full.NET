using System.Text.Json;
using Full.NET.Modules.Workflow.Domain;
using Full.NET.Modules.Workflow.Persistence;
using Full.NET.Modules.Workflow.Serialization;

namespace Full.NET.UnitTests.Workflow;

/// <summary>目标事实严格绑定唯一调用和真实目标；字段合并不把旧同意扩大到新输入。</summary>
[TestClass]
public sealed class WorkflowCollaborationControlPolicyTests
{
    internal static (WorkflowCollaborationAdmissionRecord Admission, WorkflowCollaborationControlRecord Row) Control(string kind = "completed")
    {
        var data = WorkflowCollaborationTargetTests.Fixture();
        var id = Guid.CreateVersion7();
        var body = new WorkflowCollaborationControlBody(id, data.Admission.Id, kind, data.Admission.BindingHash, data.Admission.TargetInstanceId,
            data.Accepted.TargetDefinitionVersionId, 2, kind == "created" ? "created" : "completed", "{}", WorkflowCollaborationCallPolicy.Hash("{}"), kind == "created" ? 0 : 1);
        var json = JsonSerializer.Serialize(body, WorkflowJsonSerializerContext.Default.WorkflowCollaborationControlBody);
        return (data.Admission, new(id, data.Admission.Id, data.Admission.SourceTenantScopeKey, data.Admission.TargetTenantScopeKey, kind,
            json, WorkflowCollaborationCallPolicy.Hash(json), null, "published", 1, data.Now, null));
    }

    [TestMethod]
    [DataRow("created")]
    [DataRow("completed")]
    [DataRow("cancelled")]
    public void Each_control_kind_accepts_only_its_fixed_correlated_target_fact(string kind)
    {
        var data = Control(kind); Assert.IsTrue(WorkflowCollaborationControlPolicy.TryRead(data.Row, data.Admission, out _));
    }

    [TestMethod]
    [DataRow("extra")]
    [DataRow("case")]
    [DataRow("duplicate")]
    [DataRow("binding")]
    [DataRow("target")]
    [DataRow("projection")]
    [DataRow("budget")]
    [DataRow("scope")]
    [DataRow("revision")]
    public void Rehashed_tampering_cannot_create_an_authorized_result(string mutation)
    {
        var data = Control(); var row = data.Row;
        var body = JsonSerializer.Deserialize(row.BodyJson, WorkflowJsonSerializerContext.Default.WorkflowCollaborationControlBody)!;
        if (mutation == "binding") body = body with { BindingHash = new('a', 64) };
        if (mutation == "target") body = body with { TargetInstanceId = Guid.CreateVersion7() };
        if (mutation == "projection") body = body with { ProjectionJson = "{\"secret\":true,\"secret\":false}" };
        if (mutation == "budget") body = body with { UnusedActivities = 4097 };
        if (mutation == "revision") body = body with { TargetRevision = 0 };
        var json = JsonSerializer.Serialize(body, WorkflowJsonSerializerContext.Default.WorkflowCollaborationControlBody);
        if (mutation == "extra") json = json[..^1] + ",\"actorUserId\":null}";
        if (mutation == "duplicate") json = json[..^1] + ",\"callId\":\"" + body.CallId.ToString("D") + "\"}";
        if (mutation == "case") json = json.Replace("targetRevision", "TargetRevision", StringComparison.Ordinal);
        row = row with { BodyJson = json, BodyHash = WorkflowCollaborationCallPolicy.Hash(json) };
        if (mutation == "scope") row = row with { TargetTenantScopeKey = "host" };
        Assert.IsFalse(WorkflowCollaborationControlPolicy.TryRead(row, data.Admission, out _));
    }

    [TestMethod]
    public void Uncreated_closure_returns_only_the_whole_original_slice_without_inventing_a_target()
    {
        var data = Control("cancelled"); var admission = data.Admission with { TargetInstanceId = null, StatusKey = "cancel_requested" };
        var body = JsonSerializer.Deserialize(data.Row.BodyJson, WorkflowJsonSerializerContext.Default.WorkflowCollaborationControlBody)!;
        var binding = JsonSerializer.Deserialize(admission.BindingJson, WorkflowJsonSerializerContext.Default.WorkflowCollaborationCallBinding)!;
        body = body with { TargetInstanceId = null, TargetRevision = null, OutcomeKey = "not_created", UnusedActivities = binding.ReservedActivities };
        var json = JsonSerializer.Serialize(body, WorkflowJsonSerializerContext.Default.WorkflowCollaborationControlBody);
        Assert.IsTrue(WorkflowCollaborationControlPolicy.TryRead(data.Row with { BodyJson = json, BodyHash = WorkflowCollaborationCallPolicy.Hash(json) }, admission, out _));
    }

    [TestMethod]
    [DataRow("safe", true)]
    [DataRow("input_changed", false)]
    [DataRow("outside", false)]
    [DataRow("duplicate", false)]
    [DataRow("wrong_type", false)]
    public void Result_merge_keeps_private_updates_and_rejects_stale_or_unagreed_fields(string scenario, bool expected)
    {
        var data = WorkflowCollaborationTargetTests.Fixture();
        var accepted = data.Accepted with { InputFields = new Dictionary<string, string> { ["title"] = "title" }, OutputFields = new Dictionary<string, string> { ["title"] = "title" } };
        var schema = new WorkflowFormSchema(1, 1, [new("main", [new("title", "text", true, new Dictionary<string, JsonElement>()), new("secret", "text", false, new Dictionary<string, JsonElement>())])]);
        var original = JsonSerializer.Deserialize("{\"title\":\"before\",\"secret\":\"private\"}", WorkflowJsonSerializerContext.Default.DictionaryStringJsonElement)!;
        var current = JsonSerializer.Deserialize(scenario == "input_changed" ? "{\"title\":\"changed\",\"secret\":\"updated privately\"}" : "{\"title\":\"before\",\"secret\":\"updated privately\"}", WorkflowJsonSerializerContext.Default.DictionaryStringJsonElement)!;
        var projection = scenario switch { "outside" => "{\"secret\":\"leak\"}", "duplicate" => "{\"title\":\"a\",\"title\":\"b\"}", "wrong_type" => "{\"title\":42}", _ => "{\"title\":\"approved\"}" };
        Assert.AreEqual(expected, WorkflowCollaborationResultProjection.TryMerge(schema, accepted, original, current, projection, out var output, out var error));
        if (expected) { Assert.AreEqual("updated privately", output!.Values["secret"].GetString()); Assert.AreEqual("approved", output.Values["title"].GetString()); }
        if (scenario == "input_changed") Assert.AreEqual("workflow.collaboration.input_conflict", error);
    }
}
