using System.Security.Cryptography;
using System.Text;
using Full.NET.Agents.Workflows;

namespace Full.NET.UnitTests.Ai;

/// <summary>多节点工作流子步骤共用根 RunId，但 OperationId 必须彼此独立。</summary>
[TestClass]
public sealed class AiMultiAgentBudgetTests
{
    [TestMethod]
    public void Workflow_child_operation_ids_are_stable_and_distinct()
    {
        var runId = Guid.CreateVersion7();
        var read = ResolveWorkflowOperationId(runId, "read_sessions");
        var summarize = ResolveWorkflowOperationId(runId, "summarize");
        var rename = ResolveWorkflowOperationId(runId, "rename");
        Assert.AreNotEqual(runId, read);
        Assert.AreNotEqual(read, summarize);
        Assert.AreNotEqual(summarize, rename);
        Assert.AreEqual(read, ResolveWorkflowOperationId(runId, "read_sessions"));
    }

    [TestMethod]
    public void Workflow_registry_exposes_chat_rename_example()
    {
        var definition = AgentWorkflowRegistry.Resolve(AgentWorkflowRegistry.ChatRenameWorkflowKey, 1);
        Assert.IsNotNull(definition);
        Assert.AreEqual(4, definition.Nodes.Count);
        Assert.AreEqual("rename", definition.Nodes[^1].Key);
        Assert.AreEqual(AgentWorkflowNodeKind.ToolWrite, definition.Nodes[^1].Kind);
    }

    private static Guid ResolveWorkflowOperationId(Guid runId, string nodeKey)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(FormattableString.Invariant($"{runId:N}|workflow|{nodeKey}")));
        return new Guid(bytes.AsSpan(0, 16), bigEndian: true);
    }
}
