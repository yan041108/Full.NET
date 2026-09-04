using System.Text.Json;
using Full.NET.Modules.Workflow.Domain;

namespace Full.NET.UnitTests.Workflow;

[TestClass]
public sealed class WorkflowRuntimePlanTests
{
    [TestMethod]
    public void Linear_plan_resolves_first_and_subsequent_approval_nodes()
    {
        var draft = new WorkflowDefinitionDraft(1,
        [
            Node("start", "start", "first"),
            Node("first", "human.approval", "second"),
            Node("second", "human.approval", "end"),
            Node("end", "end"),
        ]);

        var created = WorkflowRuntimePlan.TryCreate(draft, out var plan);

        Assert.IsTrue(created);
        Assert.IsNotNull(plan);
        Assert.IsTrue(plan.TryResolveStart(out var initial));
        Assert.AreEqual("first", initial.NextApprovalNodeKey);
        Assert.IsTrue(plan.TryResolveApproval("first", out var first));
        Assert.AreEqual("second", first.NextApprovalNodeKey);
        Assert.IsFalse(first.CompletesInstance);
        Assert.IsTrue(plan.TryResolveApproval("second", out var second));
        Assert.IsNull(second.NextApprovalNodeKey);
        Assert.IsTrue(second.CompletesInstance);
    }

    [TestMethod]
    public void Branched_or_non_approval_plan_is_rejected()
    {
        var branched = new WorkflowDefinitionDraft(1,
        [
            Node("start", "start", "first", "second"),
            Node("first", "human.approval", "end"),
            Node("second", "human.approval", "end"),
            Node("end", "end"),
        ]);
        var withoutApproval = new WorkflowDefinitionDraft(1,
        [
            Node("start", "start", "end"),
            Node("end", "end"),
        ]);

        Assert.IsFalse(WorkflowRuntimePlan.TryCreate(branched, out _));
        Assert.IsFalse(WorkflowRuntimePlan.TryCreate(withoutApproval, out _));
    }

    [TestMethod]
    public void Linear_plan_accepts_cc_before_between_and_after_approvals()
    {
        var recipient = Guid.NewGuid();
        var draft = new WorkflowDefinitionDraft(1,
        [
            Node("start", "start", "before"),
            CcNode("before", recipient, "first"),
            Node("first", "human.approval", "middle"),
            CcNode("middle", recipient, "second"),
            Node("second", "human.approval", "after"),
            CcNode("after", recipient, "end"),
            Node("end", "end"),
        ]);

        Assert.IsTrue(WorkflowRuntimePlan.TryCreate(draft, out var plan));
        Assert.IsNotNull(plan);
        Assert.IsTrue(plan.TryResolveStart(out var initial));
        Assert.AreEqual("first", initial.NextApprovalNodeKey);
        CollectionAssert.AreEqual(new[] { "before" },
            initial.CcNodes.Select(node => node.NodeKey).ToArray());

        Assert.IsTrue(plan.TryResolveApproval("first", out var middle));
        Assert.AreEqual("second", middle.NextApprovalNodeKey);
        Assert.IsFalse(middle.CompletesInstance);
        CollectionAssert.AreEqual(new[] { "middle" },
            middle.CcNodes.Select(node => node.NodeKey).ToArray());

        Assert.IsTrue(plan.TryResolveApproval("second", out var trailing));
        Assert.IsNull(trailing.NextApprovalNodeKey);
        Assert.IsTrue(trailing.CompletesInstance);
        CollectionAssert.AreEqual(new[] { "after" },
            trailing.CcNodes.Select(node => node.NodeKey).ToArray());
        CollectionAssert.AreEqual(new[] { recipient },
            trailing.CcNodes.Single().RecipientUserIds.ToArray());
    }

    [TestMethod]
    public void Exclusive_gateway_selects_one_path_and_records_the_taken_branch()
    {
        var schema = new WorkflowFormSchema(1, 1,
        [
            new WorkflowFormSection("main",
            [
                new WorkflowFormField(
                    "amount",
                    "money",
                    true,
                    new Dictionary<string, JsonElement>
                    {
                        ["scale"] = JsonSerializer.SerializeToElement(2),
                    }),
            ]),
        ]);
        var draft = new WorkflowDefinitionDraft(1,
        [
            Node("start", "start", "route"),
            GatewayNode("route", "amount", "1000.00", "finance", "manager"),
            Node("finance", "human.approval", "end"),
            Node("manager", "human.approval", "end"),
            Node("end", "end"),
        ]);

        Assert.IsTrue(WorkflowRuntimePlan.TryCreate(draft, schema, out var plan));
        Assert.IsNotNull(plan);
        Assert.IsTrue(plan.TryResolveStart(Values("{\"amount\":\"1200.00\"}"), out var finance));
        Assert.AreEqual("finance", finance.NextApprovalNodeKey);
        Assert.AreEqual("route", finance.AutomaticNodes.Single().NodeKey);
        Assert.AreEqual("large", finance.AutomaticNodes.Single().OutcomeKey);

        Assert.IsTrue(plan.TryResolveStart(Values("{\"amount\":\"100.00\"}"), out var manager));
        Assert.AreEqual("manager", manager.NextApprovalNodeKey);
        Assert.AreEqual("default", manager.AutomaticNodes.Single().OutcomeKey);
    }

    private static WorkflowNodeDraft Node(string key, string type, params string[] next) =>
        new(
            key,
            type,
            1,
            JsonSerializer.SerializeToElement(new { nextNodeKeys = next }));

    private static WorkflowNodeDraft CcNode(string key, Guid recipient, params string[] next) =>
        new(
            key,
            "notify.cc",
            1,
            JsonSerializer.SerializeToElement(new
            {
                nextNodeKeys = next,
                recipientUserIds = new[] { recipient },
            }));

    private static WorkflowNodeDraft GatewayNode(
        string key,
        string fieldKey,
        string expectedValue,
        string branchTarget,
        string defaultTarget) =>
        new(
            key,
            "gateway.exclusive",
            1,
            JsonSerializer.SerializeToElement(new
            {
                nextNodeKeys = new[] { branchTarget, defaultTarget },
                branches = new[]
                {
                    new
                    {
                        branchKey = "large",
                        nextNodeKey = branchTarget,
                        condition = new
                        {
                            fieldKey,
                            @operator = "greaterThanOrEqual",
                            value = expectedValue,
                        },
                    },
                },
                defaultNextNodeKey = defaultTarget,
            }));

    private static IReadOnlyDictionary<string, JsonElement> Values(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.EnumerateObject()
            .ToDictionary(property => property.Name, property => property.Value.Clone(), StringComparer.Ordinal);
    }
}
