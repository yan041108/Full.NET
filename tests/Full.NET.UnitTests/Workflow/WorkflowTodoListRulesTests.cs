using Full.NET.Modules.Workflow.Domain;

namespace Full.NET.UnitTests.Workflow;

/// <summary>验证工作流待办/已办列表筛选规则。</summary>
[TestClass]
public sealed class WorkflowTodoListRulesTests
{
    [TestMethod]
    public void IsValidResultActionKey_accepts_supported_actions_and_null()
    {
        Assert.IsTrue(WorkflowTodoListRules.IsValidResultActionKey(null));
        Assert.IsTrue(WorkflowTodoListRules.IsValidResultActionKey("approve"));
        Assert.IsTrue(WorkflowTodoListRules.IsValidResultActionKey("reject"));
        Assert.IsFalse(WorkflowTodoListRules.IsValidResultActionKey("unknown"));
    }

    [TestMethod]
    public void IsValidTimeRange_rejects_inverted_ranges()
    {
        var from = DateTimeOffset.Parse("2026-01-02T00:00:00Z");
        var to = DateTimeOffset.Parse("2026-01-01T00:00:00Z");
        Assert.IsTrue(WorkflowTodoListRules.IsValidTimeRange(null, to));
        Assert.IsFalse(WorkflowTodoListRules.IsValidTimeRange(from, to));
    }

    [TestMethod]
    public void Normalize_filters_trim_blank_values()
    {
        Assert.IsNull(WorkflowTodoListRules.NormalizeDefinitionKey("  "));
        Assert.AreEqual("leave", WorkflowTodoListRules.NormalizeDefinitionKey(" leave "));
        Assert.AreEqual("purchase", WorkflowTodoListRules.NormalizeBusinessType(" purchase "));
    }
}
