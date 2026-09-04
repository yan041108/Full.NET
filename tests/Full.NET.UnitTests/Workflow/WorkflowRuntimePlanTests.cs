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
        Assert.AreEqual("first", plan.FirstApprovalNodeKey);
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
}
