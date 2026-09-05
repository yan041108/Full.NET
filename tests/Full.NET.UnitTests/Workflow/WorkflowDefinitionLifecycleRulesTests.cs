using Full.NET.Modules.Workflow.Domain;

namespace Full.NET.UnitTests.Workflow;

/// <summary>验证工作流定义生命周期规则边界。</summary>
[TestClass]
public sealed class WorkflowDefinitionLifecycleRulesTests
{
    [TestMethod]
    public void Allows_new_instance_only_when_active()
    {
        Assert.IsTrue(WorkflowDefinitionLifecycleRules.AllowsNewInstance(WorkflowDefinitionStatusKeys.Active));
        Assert.IsFalse(WorkflowDefinitionLifecycleRules.AllowsNewInstance(WorkflowDefinitionStatusKeys.Disabled));
        Assert.IsFalse(WorkflowDefinitionLifecycleRules.AllowsNewInstance(WorkflowDefinitionStatusKeys.Archived));
    }

    [TestMethod]
    public void Can_transition_between_active_disabled_and_archive()
    {
        Assert.IsTrue(WorkflowDefinitionLifecycleRules.CanTransition(
            WorkflowDefinitionStatusKeys.Active, WorkflowDefinitionStatusKeys.Disabled));
        Assert.IsTrue(WorkflowDefinitionLifecycleRules.CanTransition(
            WorkflowDefinitionStatusKeys.Disabled, WorkflowDefinitionStatusKeys.Active));
        Assert.IsTrue(WorkflowDefinitionLifecycleRules.CanTransition(
            WorkflowDefinitionStatusKeys.Active, WorkflowDefinitionStatusKeys.Archived));
        Assert.IsFalse(WorkflowDefinitionLifecycleRules.CanTransition(
            WorkflowDefinitionStatusKeys.Archived, WorkflowDefinitionStatusKeys.Active));
    }

    [TestMethod]
    public void Allows_draft_mutation_for_active_and_disabled_only()
    {
        Assert.IsTrue(WorkflowDefinitionLifecycleRules.AllowsDraftMutation(WorkflowDefinitionStatusKeys.Active));
        Assert.IsTrue(WorkflowDefinitionLifecycleRules.AllowsDraftMutation(WorkflowDefinitionStatusKeys.Disabled));
        Assert.IsFalse(WorkflowDefinitionLifecycleRules.AllowsDraftMutation(WorkflowDefinitionStatusKeys.Archived));
    }
}
