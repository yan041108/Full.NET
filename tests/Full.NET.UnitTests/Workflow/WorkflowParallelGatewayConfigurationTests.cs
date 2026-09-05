using System.Text.Json;
using Full.NET.Modules.Workflow.Domain;

namespace Full.NET.UnitTests.Workflow;

[TestClass]
public sealed class WorkflowParallelGatewayConfigurationTests
{
    [TestMethod]
    public void Fork_and_join_configuration_is_accepted()
    {
        var fork = JsonSerializer.SerializeToElement(new
        {
            gatewayRoleKey = "fork",
            joinNodeKey = "join1",
            branches = new object[]
            {
                new { branchKey = "branch-a", nextNodeKey = "approve-a" },
                new { branchKey = "branch-b", nextNodeKey = "approve-b" },
            },
        });
        var join = JsonSerializer.SerializeToElement(new
        {
            gatewayRoleKey = "join",
            forkNodeKey = "fork1",
            nextNodeKeys = new[] { "after-join" },
        });

        Assert.IsTrue(WorkflowParallelGatewayConfiguration.TryRead(fork, out var forkDefinition));
        Assert.IsNotNull(forkDefinition);
        Assert.AreEqual(WorkflowParallelGatewayRole.Fork, forkDefinition!.Role);
        Assert.AreEqual("join1", forkDefinition.JoinNodeKey);
        Assert.AreEqual(2, forkDefinition.Branches.Count);

        Assert.IsTrue(WorkflowParallelGatewayConfiguration.TryRead(join, out var joinDefinition));
        Assert.IsNotNull(joinDefinition);
        Assert.AreEqual(WorkflowParallelGatewayRole.Join, joinDefinition!.Role);
        Assert.AreEqual("fork1", joinDefinition.ForkNodeKey);
        Assert.AreEqual("after-join", joinDefinition.NextNodeKey);
    }

    [TestMethod]
    public void Invalid_parallel_configuration_is_rejected()
    {
        var singleBranch = JsonSerializer.SerializeToElement(new
        {
            gatewayRoleKey = "fork",
            joinNodeKey = "join1",
            branches = new object[] { new { branchKey = "branch-a", nextNodeKey = "approve-a" } },
        });
        var missingJoin = JsonSerializer.SerializeToElement(new
        {
            gatewayRoleKey = "join",
            forkNodeKey = "fork1",
        });

        Assert.IsFalse(WorkflowParallelGatewayConfiguration.TryRead(singleBranch, out _));
        Assert.IsFalse(WorkflowParallelGatewayConfiguration.TryRead(missingJoin, out _));
    }
}
