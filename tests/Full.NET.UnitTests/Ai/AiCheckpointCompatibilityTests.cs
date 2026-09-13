using Full.NET.Agents.Definitions;
using Full.NET.Agents.Runtime;
using Full.NET.Agents.Workflows;

namespace Full.NET.UnitTests.Ai;

[TestClass]
public sealed class AiCheckpointCompatibilityTests
{
    [TestMethod]
    public void Compatible_checkpoint_versions_are_accepted()
    {
        Assert.IsTrue(AgentCheckpointCompatibility.TryValidate(
            AgentDefinitionRegistry.SingleTextKey,
            1,
            AgentCheckpointCompatibility.CurrentCheckpointFormatVersion,
            AgentFrameworkRuntime.FrameworkVersion,
            out var error));
        Assert.AreEqual(string.Empty, error);
    }

    [TestMethod]
    public void Unknown_definition_is_rejected()
    {
        Assert.IsFalse(AgentCheckpointCompatibility.TryValidate(
            "unknown-definition",
            1,
            AgentCheckpointCompatibility.CurrentCheckpointFormatVersion,
            AgentFrameworkRuntime.FrameworkVersion,
            out var error));
        Assert.AreEqual("ai.agent_run.definition_incompatible", error);
    }

    [TestMethod]
    public void Mismatched_framework_version_is_rejected()
    {
        Assert.IsFalse(AgentCheckpointCompatibility.TryValidate(
            AgentDefinitionRegistry.ReadOnlyToolLoopKey,
            1,
            AgentCheckpointCompatibility.CurrentCheckpointFormatVersion,
            "0.0.0",
            out var error));
        Assert.AreEqual("ai.agent_run.framework_incompatible", error);
    }

    [TestMethod]
    public void Unsupported_checkpoint_format_is_rejected()
    {
        Assert.IsFalse(AgentCheckpointCompatibility.TryValidate(
            AgentDefinitionRegistry.ReadOnlyToolLoopKey,
            1,
            99,
            AgentFrameworkRuntime.FrameworkVersion,
            out var error));
        Assert.AreEqual("ai.agent_run.checkpoint_format_incompatible", error);
    }

    [TestMethod]
    public void Workflow_checkpoint_versions_are_accepted()
    {
        Assert.IsTrue(AgentCheckpointCompatibility.TryValidateWorkflow(
            AgentWorkflowRegistry.ChatRenameWorkflowKey,
            1,
            AgentCheckpointCompatibility.CurrentCheckpointFormatVersion,
            AgentFrameworkRuntime.FrameworkVersion,
            out var error));
        Assert.AreEqual(string.Empty, error);
    }
}
