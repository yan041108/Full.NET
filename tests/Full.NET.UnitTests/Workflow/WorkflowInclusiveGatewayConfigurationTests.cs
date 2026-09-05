using System.Text.Json;
using Full.NET.Modules.Workflow.Domain;

namespace Full.NET.UnitTests.Workflow;

[TestClass]
public sealed class WorkflowInclusiveGatewayConfigurationTests
{
    [TestMethod]
    public void Fork_and_join_configuration_is_accepted()
    {
        var fork = JsonSerializer.SerializeToElement(new
        {
            gatewayRoleKey = "fork",
            joinNodeKey = "join1",
            defaultNextNodeKey = "default-approve",
            branches = new object[]
            {
                new
                {
                    branchKey = "large",
                    nextNodeKey = "finance",
                    condition = new { fieldKey = "amount", @operator = "greaterThanOrEqual", value = "1000.00" }
                },
            },
        });
        var join = JsonSerializer.SerializeToElement(new
        {
            gatewayRoleKey = "join",
            forkNodeKey = "fork1",
            nextNodeKeys = new[] { "after-join" },
        });

        Assert.IsTrue(WorkflowInclusiveGatewayConfiguration.TryRead(fork, null, out var forkDefinition));
        Assert.IsNotNull(forkDefinition);
        Assert.AreEqual(WorkflowInclusiveGatewayRole.Fork, forkDefinition!.Role);
        Assert.AreEqual("join1", forkDefinition.JoinNodeKey);
        Assert.AreEqual("default-approve", forkDefinition.DefaultNextNodeKey);
        Assert.AreEqual(1, forkDefinition.Branches.Count);

        Assert.IsTrue(WorkflowInclusiveGatewayConfiguration.TryRead(join, null, out var joinDefinition));
        Assert.IsNotNull(joinDefinition);
        Assert.AreEqual(WorkflowInclusiveGatewayRole.Join, joinDefinition!.Role);
        Assert.AreEqual("fork1", joinDefinition.ForkNodeKey);
        Assert.AreEqual("after-join", joinDefinition.DefaultNextNodeKey);
    }

    [TestMethod]
    public void Selects_all_matching_branches_or_default()
    {
        var schema = new WorkflowFormSchema(1, 1,
        [
            new WorkflowFormSection("main",
            [
                new WorkflowFormField(
                    "amount",
                    "money",
                    true,
                    new Dictionary<string, JsonElement> { ["scale"] = JsonSerializer.SerializeToElement(2) }),
            ]),
        ]);
        var fork = JsonSerializer.SerializeToElement(new
        {
            gatewayRoleKey = "fork",
            joinNodeKey = "join1",
            defaultNextNodeKey = "default-approve",
            branches = new object[]
            {
                new
                {
                    branchKey = "large",
                    nextNodeKey = "finance",
                    condition = new { fieldKey = "amount", @operator = "greaterThanOrEqual", value = "1000.00" }
                },
                new
                {
                    branchKey = "small",
                    nextNodeKey = "manager",
                    condition = new { fieldKey = "amount", @operator = "lessThan", value = "100.00" }
                },
            },
        });
        Assert.IsTrue(WorkflowInclusiveGatewayConfiguration.TryRead(fork, schema, out var definition));
        Assert.IsNotNull(definition);

        var values = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["amount"] = JsonSerializer.SerializeToElement("1500.00"),
        };
        Assert.IsTrue(definition!.TrySelectBranches(values, out var selections));
        Assert.AreEqual(1, selections.Count);
        Assert.AreEqual("large", selections[0].BranchKey);

        values["amount"] = JsonSerializer.SerializeToElement("50.00");
        Assert.IsTrue(definition.TrySelectBranches(values, out selections));
        Assert.AreEqual(1, selections.Count);
        Assert.AreEqual("small", selections[0].BranchKey);

        values["amount"] = JsonSerializer.SerializeToElement("500.00");
        Assert.IsTrue(definition.TrySelectBranches(values, out selections));
        Assert.AreEqual(1, selections.Count);
        Assert.AreEqual("default", selections[0].BranchKey);
        Assert.AreEqual("default-approve", selections[0].NextNodeKey);
    }
}
