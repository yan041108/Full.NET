using System.Text.Json;
using Full.NET.Modules.Workflow.Contracts;
using Full.NET.Modules.Workflow.Domain;
using Full.NET.Modules.Workflow.Features;

namespace Full.NET.UnitTests.Workflow;

/// <summary>业务节点只能使用真实静态动作及根审批最终提交拓扑，结果不能伪装效果版本。</summary>
[TestClass]
public sealed class WorkflowBusinessCommandTests
{
    private static WorkflowNodeDraft Node(string key, string kind, string config) => new(key, kind, 1, JsonDocument.Parse(config).RootElement.Clone());
    private static WorkflowDefinitionDraft Draft() => new(1, [Node("start", "start", """{"nextNodeKeys":["review"]}"""),
        Node("review", "human.approval", """{"nextNodeKeys":["apply"]}"""),
        Node("apply", "business.command", """{"nextNodeKeys":["end"],"businessCommandPolicy":{"actionKey":"owner.apply","actionVersion":1}}"""), Node("end", "end", "{}")]);
    private static WorkflowBusinessActionCatalog Catalog() => new([new("owner", "owner.apply", 1, "host", "owner.rule", "root_finalizer")]);

    [TestMethod]
    public void Bound_action_uses_registered_hash_and_real_wait_without_todo()
    {
        var bound = WorkflowBusinessCommandBindings.Bind(Draft(), new(null, "host", "host"), Catalog());
        Assert.IsTrue(bound.IsSuccess);
        Assert.AreEqual(Catalog().ForScope("host")[0].Hash, bound.Value!.Nodes[2].Config.GetProperty("boundBusinessActionHash").GetString());
        Assert.IsTrue(WorkflowDefinitionCompiler.Compile(bound.Value).IsSuccess);
        Assert.IsTrue(WorkflowRuntimePlan.TryCreate(bound.Value, out var plan));
        Assert.IsTrue(plan!.HasBusinessCommands);
    }

    [TestMethod]
    public void Host_action_is_unavailable_to_tenant_and_unknown_version()
    {
        Assert.IsFalse(WorkflowBusinessCommandBindings.Bind(Draft(), new(Guid.CreateVersion7(), "tenant", "tenant:test"), Catalog()).IsSuccess);
        Assert.IsNull(Catalog().Find("host", "owner.apply", 2));
        Assert.ThrowsExactly<InvalidOperationException>(() => new WorkflowBusinessActionCatalog([
            new("owner", "owner.apply", 1, "host", "owner.rule", "root_finalizer"), new("other", "owner.apply", 1, "host", "owner.rule", "root_finalizer")]));
    }

    [TestMethod]
    [DataRow("{\"actionKey\":\"owner.apply\",\"actionVersion\":1,\"sql\":\"delete\"}")]
    [DataRow("{\"actionKey\":\"owner.apply\",\"actionVersion\":\"1\"}")]
    [DataRow("{\"actionKey\":\"owner.apply\",\"actionVersion\":1,\"actionVersion\":1}")]
    public void Arbitrary_payload_and_ambiguous_policy_are_rejected(string policy)
    {
        Assert.IsFalse(WorkflowBusinessCommandPolicy.TryRead("business.command", JsonDocument.Parse("{\"businessCommandPolicy\":" + policy + "}").RootElement, out _));
    }

    [TestMethod]
    public void Bypass_end_and_nonapproval_predecessor_are_rejected()
    {
        var draft = Draft();
        Assert.IsFalse(WorkflowBusinessCommandBindings.TryReadFinalizer(draft with { Nodes = draft.Nodes.Select(n => n.NodeKey == "review" ? n with { Config = JsonDocument.Parse("""{"nextNodeKeys":["apply","end"]}""").RootElement.Clone() } : n).ToArray() }, "apply", out _));
        Assert.IsFalse(WorkflowBusinessCommandBindings.TryReadFinalizer(draft with { Nodes = draft.Nodes.Select(n => n.NodeKey == "review" ? n with { NodeTypeKey = "human.task" } : n).ToArray() }, "apply", out _));
    }

    [TestMethod]
    public void Input_digest_binds_root_requester_action_and_definition()
    {
        var b = new WorkflowBusinessCommandBinding(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), "owner", "owner.apply", 1, new('a', 64), "owner.rule", "request", Guid.CreateVersion7());
        Assert.AreNotEqual(WorkflowBusinessCommandHash.Compute(b), WorkflowBusinessCommandHash.Compute(b with { InstanceId = Guid.CreateVersion7() }));
        Assert.AreNotEqual(WorkflowBusinessCommandHash.Compute(b), WorkflowBusinessCommandHash.Compute(b with { RequestedByUserId = Guid.CreateVersion7() }));
    }

    [TestMethod]
    public void Applied_requires_owner_proof_and_compensation_requires_later_version()
    {
        var value = new WorkflowBusinessCommandResultIntegrationEvent(Guid.CreateVersion7(), Guid.CreateVersion7(), new('a',64), "execute", 1, "applied", new('b',64), 2, null, null, DateTimeOffset.UtcNow);
        Assert.IsTrue(WorkflowBusinessCommandValidation.ValidResult(value));
        Assert.IsFalse(WorkflowBusinessCommandValidation.ValidResult(value with { OwnerInputHash = null }));
        Assert.IsFalse(WorkflowBusinessCommandValidation.ValidResult(value with { OutcomeKey = "compensated", CompensatedVersion = 2 }));
        Assert.IsTrue(WorkflowBusinessCommandValidation.ValidResult(value with { PhaseKey = "compensate", Generation = 2, OutcomeKey = "compensated", CompensatedVersion = 3 }));
        Assert.IsFalse(WorkflowBusinessCommandValidation.ValidResult(value with { ErrorCode = "private snapshot" }));
    }
}
