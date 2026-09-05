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

    /// <summary>运行计划必须把不可变多人审批策略传播到对应等待点。</summary>
    [TestMethod]
    public void Multi_approval_policy_is_carried_by_runtime_transition()
    {
        var users = new[] { Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7() };
        var draft = new WorkflowDefinitionDraft(1,
        [
            Node("start", "start", "review"),
            new WorkflowNodeDraft(
                "review",
                "human.approval",
                1,
                JsonSerializer.SerializeToElement(new
                {
                    nextNodeKeys = new[] { "end" },
                    approvalPolicy = new
                    {
                        modeKey = "nOfM",
                        approverUserIds = users,
                        requiredApprovals = 2,
                    },
                })),
            Node("end", "end"),
        ]);

        Assert.IsTrue(WorkflowRuntimePlan.TryCreate(draft, out var plan));
        Assert.IsTrue(plan!.TryResolveStart(out var transition));
        Assert.IsNotNull(transition.ApprovalPolicy);
        Assert.AreEqual("nOfM", transition.ApprovalPolicy.ModeKey);
        Assert.AreEqual(2, transition.ApprovalPolicy.RequiredApprovals);
        CollectionAssert.AreEqual(users, transition.ApprovalPolicy.ApproverUserIds.ToArray());
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

    [TestMethod]
    public void Parallel_gateway_fork_creates_branch_plans_and_join_waits_for_all_branches()
    {
        var draft = new WorkflowDefinitionDraft(1,
        [
            Node("start", "start", "fork1"),
            ParallelForkNode("fork1", "join1", "approve-a", "approve-b"),
            Node("approve-a", "human.approval", "join1"),
            Node("approve-b", "human.approval", "join1"),
            ParallelJoinNode("join1", "fork1", "after"),
            Node("after", "human.approval", "end"),
            Node("end", "end"),
        ]);

        Assert.IsTrue(WorkflowRuntimePlan.TryCreate(draft, out var plan));
        Assert.IsNotNull(plan);
        Assert.IsTrue(plan.TryResolveStart(out var start));
        Assert.IsNotNull(start.ParallelFork);
        Assert.AreEqual(2, start.ParallelFork!.Branches.Count);
        Assert.AreEqual("approve-a", start.ParallelFork.Branches[0].NextApprovalNodeKey);
        Assert.AreEqual("approve-b", start.ParallelFork.Branches[1].NextApprovalNodeKey);

        Assert.IsTrue(plan.TryResolveApproval("approve-a", Values("{}"), "join1", out var branchA));
        Assert.IsTrue(branchA.WaitsAtJoin);
        Assert.AreEqual("join1", branchA.JoinArrival!.JoinNodeKey);

        Assert.IsTrue(plan.TryResolveApproval("approve-b", Values("{}"), "join1", out var branchB));
        Assert.IsTrue(branchB.WaitsAtJoin);

        Assert.IsTrue(plan.TryResolveAfterJoin("join1", Values("{}"), out var afterJoin));
        Assert.AreEqual("after", afterJoin.NextApprovalNodeKey);
    }

    [TestMethod]
    public void Inclusive_gateway_activates_all_matching_branches_and_waits_at_join()
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
        var draft = new WorkflowDefinitionDraft(1,
        [
            Node("start", "start", "fork1"),
            InclusiveForkNode("fork1", "join1", "default-approve", "finance", "manager"),
            Node("finance", "human.approval", "join1"),
            Node("manager", "human.approval", "join1"),
            Node("default-approve", "human.approval", "join1"),
            InclusiveJoinNode("join1", "fork1", "after"),
            Node("after", "human.approval", "end"),
            Node("end", "end"),
        ]);

        Assert.IsTrue(WorkflowRuntimePlan.TryCreate(draft, schema, out var plan));
        Assert.IsNotNull(plan);
        Assert.IsTrue(plan.TryResolveStart(Values("{\"amount\":\"50.00\"}"), out var start));
        Assert.IsNotNull(start.ParallelFork);
        Assert.AreEqual("inclusive", start.ParallelFork!.GatewayTypeKey);
        Assert.AreEqual(1, start.ParallelFork.Branches.Count);
        Assert.AreEqual("manager", start.ParallelFork.Branches[0].NextApprovalNodeKey);
    }

    private static WorkflowNodeDraft InclusiveForkNode(
        string key,
        string joinNodeKey,
        string defaultTarget,
        params string[] conditionalTargets) =>
        new(
            key,
            "gateway.inclusive",
            1,
            JsonSerializer.SerializeToElement(new
            {
                gatewayRoleKey = "fork",
                joinNodeKey,
                defaultNextNodeKey = defaultTarget,
                branches = conditionalTargets.Select((target, index) => new
                {
                    branchKey = $"branch-{target}",
                    nextNodeKey = target,
                    condition = new
                    {
                        fieldKey = "amount",
                        @operator = index == 0 ? "greaterThanOrEqual" : "lessThan",
                        value = index == 0 ? "1000.00" : "100.00",
                    },
                }).ToArray(),
            }));

    private static WorkflowNodeDraft InclusiveJoinNode(string key, string forkNodeKey, string next) =>
        new(
            key,
            "gateway.inclusive",
            1,
            JsonSerializer.SerializeToElement(new
            {
                gatewayRoleKey = "join",
                forkNodeKey,
                nextNodeKeys = new[] { next },
            }));

    private static WorkflowNodeDraft ParallelForkNode(
        string key,
        string joinNodeKey,
        params string[] branchTargets) =>
        new(
            key,
            "gateway.parallel",
            1,
            JsonSerializer.SerializeToElement(new
            {
                gatewayRoleKey = "fork",
                joinNodeKey,
                branches = branchTargets.Select((target, index) => new
                {
                    branchKey = $"branch-{target}",
                    nextNodeKey = target,
                }).ToArray(),
            }));

    private static WorkflowNodeDraft ParallelJoinNode(string key, string forkNodeKey, string next) =>
        new(
            key,
            "gateway.parallel",
            1,
            JsonSerializer.SerializeToElement(new
            {
                gatewayRoleKey = "join",
                forkNodeKey,
                nextNodeKeys = new[] { next },
            }));

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
