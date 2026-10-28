using System.Text.Json;
using Full.NET.Modules.Workflow.Domain;

namespace Full.NET.UnitTests.Workflow;

[TestClass]
public sealed class WorkflowSubprocessTimeoutPolicyTests
{
    [TestMethod]
    public void Static_timeout_edge_is_validated_and_cannot_bypass_required_human_decision()
    {
        var version = Guid.CreateVersion7();
        WorkflowNodeDraft Node(string key, string kind, object config) => new(key, kind, 1, JsonSerializer.SerializeToElement(config));
        WorkflowDefinitionDraft Draft(string target) => new(1, [Node("start", "start", new { nextNodeKeys = new[] { "child" } }),
            Node("child", "process.subprocess", new { nextNodeKeys = new[] { "normal" }, fieldPolicies = new { },
                subprocessPolicy = new { definitionVersionId = version, inputFields = new { }, outputFields = new { } },
                boundSubprocess = new { definitionVersionId = version, formVersionId = Guid.CreateVersion7(), definitionHash = new string('a',64), formHash = new string('b',64), activityBudget = 3, maximumDepth = 1, hasHumanApproval = true },
                childTimeoutPolicy = new { dueAfterMinutes = 1, pauseModeKey = "freeze", actionKey = "route", nextNodeKey = target } }),
            Node("normal", "control.empty", new { nextNodeKeys = new[] { "fallback" } }), Node("fallback", "control.empty", new { nextNodeKeys = new[] { "end" } }), Node("end", "end", new { })], "automation");
        var schema = new WorkflowFormSchema(1, 1, []);
        Assert.IsTrue(WorkflowRuntimePlan.TryCreate(Draft("fallback"), schema, out var plan));
        Assert.IsTrue(plan!.TryResolveStart(new Dictionary<string, JsonElement>(), out var transition));
        Assert.AreEqual("normal", ((WorkflowSubprocessWaitPlan)transition.AutomaticWait!).ContinuationNodeKey);
        Assert.IsFalse(WorkflowRuntimePlan.TryCreate(Draft("missing"), schema, out _));
        Assert.IsFalse(WorkflowRuntimePlan.TryCreate(Draft("start"), schema, out _));
        Assert.IsFalse(WorkflowRuntimePlan.TryCreate(Draft("fallback") with { WorkflowKindKey = "approval" }, schema, out _));
    }

    [TestMethod]
    public void Timeout_requires_closed_configuration_and_explicit_pause_semantics()
    {
        bool Read(string json) { using var doc = JsonDocument.Parse(json); return WorkflowSubprocessTimeoutPolicy.TryRead(doc.RootElement, out _); }
        Assert.IsTrue(Read("{}"));
        Assert.IsTrue(Read("""{"childTimeoutPolicy":{"dueAfterMinutes":1,"pauseModeKey":"freeze","actionKey":"continue"}}"""));
        Assert.IsTrue(Read("""{"childTimeoutPolicy":{"dueAfterMinutes":525600,"pauseModeKey":"elapsed","actionKey":"route","nextNodeKey":"fallback"}}"""));
        foreach (var value in new[] {
            """{"dueAfterMinutes":0,"pauseModeKey":"freeze","actionKey":"continue"}""",
            """{"dueAfterMinutes":1,"actionKey":"continue"}""",
            """{"dueAfterMinutes":1,"pauseModeKey":"freeze","actionKey":"continue","nextNodeKey":"fallback"}""",
            """{"dueAfterMinutes":1,"pauseModeKey":"freeze","actionKey":"route"}""",
            """{"dueAfterMinutes":1,"pauseModeKey":"freeze","actionKey":"continue","tenantId":"host"}""" })
            Assert.IsFalse(Read("{\"childTimeoutPolicy\":" + value + "}"), value);
    }
}
