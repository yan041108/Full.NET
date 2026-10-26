using System.Text.Json;
using Full.NET.Modules.Workflow.Domain;

namespace Full.NET.UnitTests.Workflow;

/// <summary>处置配置必须闭合且有界，空结果与本人不能通过未知字段或递归替补获得隐含放行。</summary>
[TestClass]
public sealed class WorkflowAssigneeHandlingPolicyTests
{
    [TestMethod]
    public void Assignee_policy_binds_closed_handling_and_rejects_duplicate_or_nested_handling()
    {
        var value = JsonSerializer.SerializeToElement(new { sources = new[] { new { resolverKindKey = "initiator" } },
            handling = new { whenInitiator = new { actionKey = "skip" } } });
        Assert.IsTrue(WorkflowAssigneePolicy.TryReadPolicy(value, out var policy)); Assert.AreEqual("skip", policy.Handling!.WhenInitiator.ActionKey);
        using var duplicate = JsonDocument.Parse("""{"sources":[{"resolverKindKey":"initiator"}],"handling":{"whenEmpty":{"actionKey":"error"}},"handling":{"whenEmpty":{"actionKey":"auto_pass"}}}""");
        Assert.IsFalse(WorkflowAssigneePolicy.TryReadPolicy(duplicate.RootElement, out _));
    }

    [TestMethod]
    [DataRow("empty")]
    [DataRow("initiator")]
    public void A_potentially_automatic_approval_cannot_satisfy_the_human_decision_barrier(string trigger)
    {
        var handling = trigger == "empty" ? new Dictionary<string, object> { ["whenEmpty"] = new { actionKey = "auto_pass" } }
            : new Dictionary<string, object> { ["whenInitiator"] = new { actionKey = "skip" } };
        var draft = new WorkflowDefinitionDraft(1,
            [new("start", "start", 1, JsonSerializer.SerializeToElement(new { nextNodeKeys = new[] { "review" } })),
             new("review", "human.approval", 1, JsonSerializer.SerializeToElement(new { nextNodeKeys = new[] { "end" },
                 assigneePolicy = new { sources = new[] { new { resolverKindKey = "initiator" } }, handling } })),
             new("end", "end", 1, JsonSerializer.SerializeToElement(new { }))]);
        Assert.IsFalse(WorkflowRuntimePlan.TryCreate(draft, out _));
        Assert.IsTrue(WorkflowRuntimePlan.TryCreate(draft with { WorkflowKindKey = "automation" }, out _));
        var withRealReview = draft with { Nodes = draft.Nodes.Select(node => node.NodeKey == "review" ? node with {
            Config = JsonSerializer.SerializeToElement(new { nextNodeKeys = new[] { "real" }, assigneePolicy = new { sources = new[] { new { resolverKindKey = "initiator" } }, handling } }) } : node)
            .Append(new("real", "human.approval", 1, JsonSerializer.SerializeToElement(new { nextNodeKeys = new[] { "end" } }))).ToArray() };
        Assert.IsTrue(WorkflowRuntimePlan.TryCreate(withRealReview, out _));
    }

    [TestMethod]
    public void Explicit_actions_preserve_safe_defaults_and_reject_cross_trigger_actions()
    {
        foreach (var key in new[] { "error", "suspend", "auto_pass", "auto_reject" })
        {
            Assert.IsTrue(Read(new { whenEmpty = new { actionKey = key } }, out var policy));
            Assert.AreEqual(key, policy!.WhenEmpty.ActionKey); Assert.AreEqual("keep", policy.WhenInitiator.ActionKey);
        }
        foreach (var key in new[] { "keep", "skip" }) Assert.IsTrue(Read(new { whenInitiator = new { actionKey = key } }, out _));
        Assert.IsFalse(Read(new { whenInitiator = new { actionKey = "auto_pass" } }, out _));
        Assert.IsFalse(Read(new { whenEmpty = new { actionKey = "keep" } }, out _));
        Assert.IsFalse(Read(new { whenEmpty = new { actionKey = "auto_pass", actorUserId = Guid.CreateVersion7() } }, out _));
        Assert.IsFalse(Read(new { whenEmpty = new { actionKey = "auto_pass" }, tenantId = Guid.CreateVersion7() }, out _));
        Assert.IsFalse(Read(new { }, out _));
    }

    [TestMethod]
    public void Fallback_order_is_explicit_and_cannot_exceed_three_or_embed_another_handling_policy()
    {
        var first = Guid.CreateVersion7(); var second = Guid.CreateVersion7();
        object Source(Guid user) => new { sources = new[] { new { resolverKindKey = "specified_users", userIds = new[] { user } } } };
        Assert.IsTrue(Read(new { whenEmpty = new { actionKey = "assign", fallbackPolicies = new[] { Source(first), Source(second) } } }, out var policy));
        Assert.AreEqual(first, policy!.WhenEmpty.FallbackPolicies[0].Sources[0].UserIds[0]);
        Assert.AreEqual(second, policy.WhenEmpty.FallbackPolicies[1].Sources[0].UserIds[0]);
        Assert.IsFalse(Read(new { whenEmpty = new { actionKey = "assign", fallbackPolicies = Array.Empty<object>() } }, out _));
        Assert.IsFalse(Read(new { whenEmpty = new { actionKey = "assign", fallbackPolicies = Enumerable.Repeat(Source(first), 4).ToArray() } }, out _));
        Assert.IsFalse(Read(new { whenEmpty = new { actionKey = "assign", fallbackPolicies = new[] { new {
            sources = new[] { new { resolverKindKey = "initiator" } }, handling = new { whenEmpty = new { actionKey = "auto_pass" } } } } } }, out _));
    }

    [TestMethod]
    public void Fallbacks_cannot_reopen_dynamic_input_or_ignore_duplicate_properties()
    {
        foreach (var kind in new[] { "form_users", "form_units" })
            Assert.IsFalse(Read(new { whenInitiator = new { actionKey = "assign", fallbackPolicies = new[] { new {
                sources = new[] { new { resolverKindKey = kind, fieldKey = "users" } } } } } }, out _));
        using var duplicate = JsonDocument.Parse("""{"whenEmpty":{"actionKey":"auto_pass"},"whenEmpty":{"actionKey":"error"}}""");
        Assert.IsFalse(WorkflowAssigneeHandlingPolicy.TryRead(duplicate.RootElement, out _));
        using var nested = JsonDocument.Parse("""{"whenEmpty":{"actionKey":"assign","fallbackPolicies":[{"sources":[{"resolverKindKey":"initiator"}],"sources":[{"resolverKindKey":"initiator"}]}]}}""");
        Assert.IsFalse(WorkflowAssigneeHandlingPolicy.TryRead(nested.RootElement, out _));
    }

    private static bool Read(object value, out WorkflowAssigneeHandlingPolicy? policy) =>
        WorkflowAssigneeHandlingPolicy.TryRead(JsonSerializer.SerializeToElement(value), out policy);
}
