using Full.NET.Modules.Workflow.Domain;

namespace Full.NET.UnitTests.Workflow;

/// <summary>验证工作流实例列表筛选规则。</summary>
[TestClass]
public sealed class WorkflowInstanceListRulesTests
{
    [TestMethod]
    public void IsValidStatusKey_accepts_supported_statuses_and_null()
    {
        Assert.IsTrue(WorkflowInstanceListRules.IsValidStatusKey(null));
        Assert.IsTrue(WorkflowInstanceListRules.IsValidStatusKey("active"));
        Assert.IsTrue(WorkflowInstanceListRules.IsValidStatusKey("completed"));
        Assert.IsTrue(WorkflowInstanceListRules.IsValidStatusKey("rejected"));
        Assert.IsTrue(WorkflowInstanceListRules.IsValidStatusKey("cancelled"));
        Assert.IsTrue(WorkflowInstanceListRules.IsValidStatusKey("suspended"));
        Assert.IsFalse(WorkflowInstanceListRules.IsValidStatusKey("running"));
    }

    [TestMethod]
    public void IsValidTimeRange_rejects_inverted_ranges()
    {
        var from = DateTimeOffset.Parse("2026-01-02T00:00:00Z");
        var to = DateTimeOffset.Parse("2026-01-01T00:00:00Z");
        Assert.IsTrue(WorkflowInstanceListRules.IsValidTimeRange(null, to));
        Assert.IsTrue(WorkflowInstanceListRules.IsValidTimeRange(from, null));
        Assert.IsTrue(WorkflowInstanceListRules.IsValidTimeRange(from, from));
        Assert.IsFalse(WorkflowInstanceListRules.IsValidTimeRange(from, to));
    }

    [TestMethod]
    public void NormalizeDefinitionKey_trims_and_nulls_blank_values()
    {
        Assert.IsNull(WorkflowInstanceListRules.NormalizeDefinitionKey(null));
        Assert.IsNull(WorkflowInstanceListRules.NormalizeDefinitionKey("   "));
        Assert.AreEqual("purchase", WorkflowInstanceListRules.NormalizeDefinitionKey(" purchase "));
    }
}
