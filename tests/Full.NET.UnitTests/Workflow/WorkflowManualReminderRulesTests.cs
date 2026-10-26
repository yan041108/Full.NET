using Full.NET.Modules.Workflow.Domain;

namespace Full.NET.UnitTests.Workflow;

/// <summary>同一步骤的人工催办预算不能因并发、时钟回退或换人而失效。</summary>
[TestClass]
public sealed class WorkflowManualReminderRulesTests
{
    [TestMethod]
    public void First_request_and_exact_interval_are_allowed()
    {
        var now = DateTimeOffset.UtcNow;
        Assert.IsTrue(WorkflowManualReminderRules.CanRequest(0, null, now));
        Assert.IsTrue(WorkflowManualReminderRules.CanRequest(1, now.AddSeconds(-300), now));
        Assert.IsFalse(WorkflowManualReminderRules.CanRequest(1, now.AddSeconds(-299), now));
    }
    [TestMethod]
    public void Exhaustion_clock_reversal_and_inconsistent_facts_fail_closed()
    {
        var now = DateTimeOffset.UtcNow;
        Assert.IsFalse(WorkflowManualReminderRules.CanRequest(20, now.AddDays(-1), now));
        Assert.IsFalse(WorkflowManualReminderRules.CanRequest(1, now.AddSeconds(1), now));
        Assert.IsFalse(WorkflowManualReminderRules.CanRequest(0, now, now));
        Assert.IsFalse(WorkflowManualReminderRules.CanRequest(1, null, now));
        Assert.IsFalse(WorkflowManualReminderRules.CanRequest(-1, null, now));
    }
}
