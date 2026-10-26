using System.Text.Json;
using Full.NET.Modules.Workflow.Domain;

namespace Full.NET.UnitTests.Workflow;

/// <summary>撤回必须显式授权，全部活动前沿满足边界，未知或已到达外部副作用不得绕过补偿。</summary>
[TestClass]
public sealed class WorkflowWithdrawalPolicyTests
{
    [TestMethod]
    public void Missing_policy_disables_withdrawal()
    {
        Assert.IsTrue(WorkflowWithdrawalPolicy.TryRead(JsonSerializer.SerializeToElement(new { }), out var policy));
        Assert.IsNull(policy);
    }
    [TestMethod]
    [DataRow("{\"modeKey\":\"all\"}")]
    [DataRow("{\"modeKey\":\"before_any_human_action\",\"actorId\":\"x\"}")]
    [DataRow("{\"modeKey\":\"at_active_nodes\",\"nodeKeys\":[\"review\",\"review\"]}")]
    [DataRow("{\"modeKey\":\"at_active_nodes\",\"nodeKeys\":[]}")]
    [DataRow("{\"modeKey\":\"disabled\",\"modeKey\":\"before_any_human_action\"}")]
    public void Invalid_or_overbroad_configuration_fails_closed(string json)
    {
        using var config = JsonDocument.Parse("{\"withdrawalPolicy\":" + json + "}");
        Assert.IsFalse(WorkflowWithdrawalPolicy.TryRead(config.RootElement, out _));
    }
    [TestMethod]
    public void Before_first_action_preserves_unknown_and_external_effect_barrier()
    {
        var policy = new WorkflowWithdrawalPolicy("before_any_human_action", new HashSet<string>());
        WorkflowWithdrawalActivityBoundary[] reached = [new("claim", "human.claim", "active")];
        Assert.IsTrue(policy.Allows(reached, false)); Assert.IsFalse(policy.Allows(reached, true));
        Assert.IsFalse(policy.Allows([.. reached, new("outbound", "service.http", "completed")], false));
        Assert.IsFalse(policy.Allows([.. reached, new("future", "unknown.new_kind", "active")], false));
        Assert.IsTrue(policy.Allows([.. reached, new("cc", "notify.cc", "completed")], false));
    }
    [TestMethod]
    public void Every_parallel_frontier_must_be_explicitly_withdrawable()
    {
        var policy = new WorkflowWithdrawalPolicy("at_active_nodes", new HashSet<string>(["left", "right"]));
        Assert.IsTrue(policy.Allows([new("left", "human.task", "active"), new("right", "human.approval", "active")], true));
        Assert.IsFalse(policy.Allows([new("left", "human.task", "active"), new("other", "human.approval", "active")], false));
        Assert.IsFalse(policy.Allows([], false));
    }

    [TestMethod]
    public void Published_boundaries_must_reference_real_human_nodes()
    {
        var graph = new WorkflowDefinitionDraft(1,
            [new("start", "start", 1, JsonSerializer.SerializeToElement(new { nextNodeKeys = new[] { "task" },
                withdrawalPolicy = new { modeKey = "at_active_nodes", nodeKeys = new[] { "missing" } } })),
             new("task", "human.task", 1, JsonSerializer.SerializeToElement(new { nextNodeKeys = new[] { "end" }, taskPolicy = new { completionModeKey = "all" } })),
             new("end", "end", 1, JsonSerializer.SerializeToElement(new { }))], "automation");
        Assert.AreEqual(Full.NET.Modules.Workflow.Contracts.WorkflowErrorCodes.DefinitionWithdrawalPolicyInvalid,
            WorkflowDefinitionCompiler.Compile(graph).ErrorCode);
    }
}
