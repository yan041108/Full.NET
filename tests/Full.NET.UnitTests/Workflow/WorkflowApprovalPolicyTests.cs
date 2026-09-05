using System.Text.Json;
using Full.NET.Modules.Workflow.Domain;

namespace Full.NET.UnitTests.Workflow;

/// <summary>验证多人审批策略只接受闭合的会签、或签和法定票数配置。</summary>
[TestClass]
public sealed class WorkflowApprovalPolicyTests
{
    /// <summary>三种多人审批模式必须推导确定的法定票数。</summary>
    [TestMethod]
    public void Supported_modes_resolve_required_approval_count()
    {
        var users = Enumerable.Range(0, 3).Select(_ => Guid.CreateVersion7()).ToArray();

        AssertPolicy("all", users, null, 3);
        AssertPolicy("any", users, null, 1);
        AssertPolicy("nOfM", users, 2, 2);
    }

    /// <summary>未配置审批策略时必须保留现有单人审批兼容语义。</summary>
    [TestMethod]
    public void Missing_policy_is_valid_legacy_single_approval()
    {
        var config = JsonSerializer.SerializeToElement(new { nextNodeKeys = new[] { "end" } });

        var valid = WorkflowApprovalPolicy.TryRead(config, out var policy);

        Assert.IsTrue(valid);
        Assert.IsNull(policy);
    }

    /// <summary>重复办理人、未知模式和非法法定票数必须失败关闭。</summary>
    [TestMethod]
    public void Invalid_multi_approval_policy_is_rejected()
    {
        var user = Guid.CreateVersion7();

        AssertInvalid(new { modeKey = "all", approverUserIds = new[] { user, user } });
        AssertInvalid(new { modeKey = "majority", approverUserIds = new[] { user, Guid.CreateVersion7() } });
        AssertInvalid(new
        {
            modeKey = "nOfM",
            approverUserIds = new[] { user, Guid.CreateVersion7(), Guid.CreateVersion7() },
            requiredApprovals = 3,
        });
    }

    /// <summary>断言指定模式被解析为预期票数。</summary>
    /// <param name="modeKey">多人审批模式。</param>
    /// <param name="users">办理人快照。</param>
    /// <param name="requiredApprovals">可选显式法定票数。</param>
    /// <param name="expectedRequired">期望法定票数。</param>
    private static void AssertPolicy(
        string modeKey,
        IReadOnlyList<Guid> users,
        int? requiredApprovals,
        int expectedRequired)
    {
        var policyElement = requiredApprovals.HasValue
            ? JsonSerializer.SerializeToElement(new
            {
                modeKey,
                approverUserIds = users,
                requiredApprovals = requiredApprovals.Value,
            })
            : JsonSerializer.SerializeToElement(new { modeKey, approverUserIds = users });
        var config = JsonSerializer.SerializeToElement(new
        {
            nextNodeKeys = new[] { "end" },
            approvalPolicy = policyElement,
        });

        var valid = WorkflowApprovalPolicy.TryRead(config, out var policy);

        Assert.IsTrue(valid);
        Assert.IsNotNull(policy);
        Assert.AreEqual(modeKey, policy.ModeKey);
        Assert.AreEqual(expectedRequired, policy.RequiredApprovals);
        CollectionAssert.AreEqual(users.ToArray(), policy.ApproverUserIds.ToArray());
    }

    /// <summary>断言指定策略被拒绝。</summary>
    /// <param name="policy">待验证的策略对象。</param>
    private static void AssertInvalid(object policy)
    {
        var config = JsonSerializer.SerializeToElement(new
        {
            nextNodeKeys = new[] { "end" },
            approvalPolicy = policy,
        });

        Assert.IsFalse(WorkflowApprovalPolicy.TryRead(config, out _));
    }
}
