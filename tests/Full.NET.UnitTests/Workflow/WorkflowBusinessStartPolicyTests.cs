using Full.NET.Modules.Workflow.Contracts;
using Full.NET.Modules.Workflow.Domain;

namespace Full.NET.UnitTests.Workflow;

/// <summary>普通启动权限不能冒充业务所有者；保留类型只来自模块的静态声明。</summary>
[TestClass]
public sealed class WorkflowBusinessStartPolicyTests
{
    [TestMethod]
    [DataRow("data_approval.serial_rule.update", true)]
    [DataRow(" data_approval.serial_rule.update ", true)]
    [DataRow("demo.enterprise_request", true)]
    [DataRow("general.request", false)]
    [DataRow(null, false)]
    public void Public_start_matches_actual_owner_types_with_the_service_normalization(string? businessType, bool expected)
    {
        Assert.AreEqual(expected, WorkflowBusinessStartPolicy.RequiresOwner(businessType, [
            new(DataApprovalWorkflowBusinessTypes.SerialRuleUpdate, "data_approval"), new("demo.enterprise_request", "enterprise_request")]));
    }
}
