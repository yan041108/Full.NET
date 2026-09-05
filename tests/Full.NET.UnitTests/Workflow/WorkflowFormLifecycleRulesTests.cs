using Full.NET.Modules.Workflow.Domain;

namespace Full.NET.UnitTests.Workflow;

/// <summary>验证工作流表单生命周期规则边界。</summary>
[TestClass]
public sealed class WorkflowFormLifecycleRulesTests
{
    [TestMethod]
    public void Allows_publish_only_when_active()
    {
        Assert.IsTrue(WorkflowFormLifecycleRules.AllowsPublish(WorkflowDefinitionStatusKeys.Active));
        Assert.IsFalse(WorkflowFormLifecycleRules.AllowsPublish(WorkflowDefinitionStatusKeys.Disabled));
        Assert.IsFalse(WorkflowFormLifecycleRules.AllowsPublish(WorkflowDefinitionStatusKeys.Archived));
    }

    [TestMethod]
    public void Can_transition_between_active_disabled_and_archive()
    {
        Assert.IsTrue(WorkflowFormLifecycleRules.CanTransition(
            WorkflowDefinitionStatusKeys.Active, WorkflowDefinitionStatusKeys.Disabled));
        Assert.IsTrue(WorkflowFormLifecycleRules.CanTransition(
            WorkflowDefinitionStatusKeys.Disabled, WorkflowDefinitionStatusKeys.Active));
        Assert.IsFalse(WorkflowFormLifecycleRules.CanTransition(
            WorkflowDefinitionStatusKeys.Archived, WorkflowDefinitionStatusKeys.Active));
    }

    [TestMethod]
    public void Allows_draft_mutation_for_active_and_disabled_only()
    {
        Assert.IsTrue(WorkflowFormLifecycleRules.AllowsDraftMutation(WorkflowDefinitionStatusKeys.Active));
        Assert.IsTrue(WorkflowFormLifecycleRules.AllowsDraftMutation(WorkflowDefinitionStatusKeys.Disabled));
        Assert.IsFalse(WorkflowFormLifecycleRules.AllowsDraftMutation(WorkflowDefinitionStatusKeys.Archived));
    }
}
