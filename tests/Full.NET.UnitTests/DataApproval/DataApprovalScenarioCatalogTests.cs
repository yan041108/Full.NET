using Full.NET.Modules.DataApproval.Contracts;
using Full.NET.Modules.DataApproval.Domain;
using Full.NET.Modules.Workflow.Contracts;

namespace Full.NET.UnitTests.DataApproval;

[TestClass]
public sealed class DataApprovalScenarioCatalogTests
{
    [TestMethod]
    public void Find_returns_serial_rule_host_update_entry()
    {
        var entry = DataApprovalScenarioCatalog.Find(DataApprovalScenarioKeys.SerialRuleHostUpdate);
        Assert.IsNotNull(entry);
        Assert.AreEqual("host", entry.ScopeKey);
        Assert.AreEqual(DataApprovalWorkflowBusinessTypes.SerialRuleUpdate, entry.WorkflowBusinessType);
    }

    [TestMethod]
    public void Find_returns_serial_rule_host_disable_entry()
    {
        var entry = DataApprovalScenarioCatalog.Find(DataApprovalScenarioKeys.SerialRuleHostDisable);
        Assert.IsNotNull(entry);
        Assert.AreEqual("host", entry.ScopeKey);
        Assert.AreEqual(DataApprovalWorkflowBusinessTypes.SerialRuleDisable, entry.WorkflowBusinessType);
    }

    [TestMethod]
    public void IsRegisteredForScope_rejects_unknown_scenario()
    {
        Assert.IsFalse(DataApprovalScenarioCatalog.IsRegisteredForScope("other.scenario", "host"));
    }

    [TestMethod]
    public void IsRegisteredForScope_rejects_host_scenario_in_tenant_scope()
    {
        Assert.IsFalse(DataApprovalScenarioCatalog.IsRegisteredForScope(
            DataApprovalScenarioKeys.SerialRuleHostUpdate,
            "tenant"));
    }
}
