using System.Text.Json;
using Full.NET.Modules.Workflow.Domain;

namespace Full.NET.UnitTests.Workflow;

/// <summary>截止策略须区分系统处置和个人投票，并明确暂停时钟。</summary>
[TestClass]
public sealed class WorkflowSlaPolicyTests
{
    [TestMethod]
    [DataRow("human.approval", "auto_pass", true)]
    [DataRow("human.group_approval", "auto_pass", true)]
    [DataRow("human.task", "auto_pass", false)]
    [DataRow("human.vote", "auto_pass", false)]
    [DataRow("human.claim", "auto_pass", false)]
    [DataRow("human.task", "auto_reject", true)]
    [DataRow("human.vote", "suspend", true)]
    [DataRow("timer.delay", "suspend", false)]
    public void Disposition_is_explicit_and_does_not_fabricate_personal_actions(string kind, string action, bool expected)
    {
        var json = JsonSerializer.SerializeToElement(new { slaPolicy = new { dueAfterMinutes = 60, pauseModeKey = "freeze", actionKey = action } });
        Assert.AreEqual(expected, WorkflowSlaPolicy.TryRead(json, kind, out var policy));
        if (expected) Assert.AreEqual(new WorkflowSlaPolicy(60, "freeze", action), policy);
    }

    [TestMethod]
    [DataRow("{\"dueAfterMinutes\":0,\"pauseModeKey\":\"freeze\",\"actionKey\":\"suspend\"}")]
    [DataRow("{\"dueAfterMinutes\":1,\"pauseModeKey\":\"implicit\",\"actionKey\":\"suspend\"}")]
    [DataRow("{\"dueAfterMinutes\":1,\"pauseModeKey\":\"freeze\",\"actionKey\":\"script\"}")]
    [DataRow("{\"dueAfterMinutes\":1,\"pauseModeKey\":\"freeze\",\"actionKey\":\"suspend\",\"actorUserId\":\"fake\"}")]
    [DataRow("{\"dueAfterMinutes\":1,\"dueAfterMinutes\":2,\"pauseModeKey\":\"freeze\",\"actionKey\":\"suspend\"}")]
    public void Unknown_ambiguous_or_unbounded_policy_is_rejected(string json)
    {
        using var document = JsonDocument.Parse($"{{\"slaPolicy\":{json}}}");
        Assert.IsFalse(WorkflowSlaPolicy.TryRead(document.RootElement, "human.approval", out _));
    }

    [TestMethod]
    public void Pause_preserves_submillisecond_remaining_without_early_fire()
    {
        var now = DateTimeOffset.Parse("2026-10-26T00:00:00Z");
        Assert.AreEqual(1L, WorkflowSlaPolicy.RemainingMilliseconds(now.AddTicks(1), now));
        Assert.AreEqual(0L, WorkflowSlaPolicy.RemainingMilliseconds(now.AddSeconds(-1), now));
    }
}
