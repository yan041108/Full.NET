using System.Text.Json;
using Full.NET.Modules.Workflow.Domain;

namespace Full.NET.UnitTests.Workflow;

/// <summary>验证办理人解析策略只接受闭合的来源键与参数集合。</summary>
[TestClass]
public sealed class WorkflowAssigneePolicyTests
{
    /// <summary>缺失办理人策略时必须回落到默认发起人单人语义。</summary>
    [TestMethod]
    public void Missing_policy_defaults_to_initiator()
    {
        var config = JsonSerializer.SerializeToElement(new { nextNodeKeys = new[] { "end" } });

        var valid = WorkflowAssigneePolicy.TryRead(config, out var policy);

        Assert.IsTrue(valid);
        Assert.AreEqual(1, policy.Sources.Count);
        Assert.AreEqual(WorkflowAssigneePolicy.Initiator, policy.Sources[0].ResolverKindKey);
    }

    /// <summary>支持的来源键必须按闭合结构解析。</summary>
    [TestMethod]
    public void Supported_sources_are_parsed()
    {
        var userId = Guid.CreateVersion7();
        var roleId = Guid.CreateVersion7();
        var unitId = Guid.CreateVersion7();
        var config = JsonSerializer.SerializeToElement(new
        {
            nextNodeKeys = new[] { "end" },
            assigneePolicy = new
            {
                sources = new object[]
                {
                    new { resolverKindKey = "specified_users", userIds = new[] { userId } },
                    new { resolverKindKey = "role_members", roleIds = new[] { roleId } },
                    new { resolverKindKey = "organization_unit_leader", unitId },
                    new { resolverKindKey = "initiator" },
                    new { resolverKindKey = "initiator_primary_unit_leader" },
                },
            },
        });

        var valid = WorkflowAssigneePolicy.TryRead(config, out var policy);

        Assert.IsTrue(valid);
        Assert.AreEqual(5, policy.Sources.Count);
    }

    /// <summary>未知来源键、重复用户和越界数组必须失败关闭。</summary>
    [TestMethod]
    public void Invalid_assignee_policy_is_rejected()
    {
        var userId = Guid.CreateVersion7();
        AssertInvalid(new
        {
            sources = new[]
            {
                new { resolverKindKey = "manager_chain" },
            },
        });
        AssertInvalid(new
        {
            sources = new[]
            {
                new { resolverKindKey = "specified_users", userIds = new[] { userId, userId } },
            },
        });
        AssertInvalid(new
        {
            sources = Array.Empty<object>(),
        });
    }

    /// <summary>断言非法办理人策略被拒绝。</summary>
    /// <param name="assigneePolicy">待验证策略对象。</param>
    private static void AssertInvalid(object assigneePolicy)
    {
        var config = JsonSerializer.SerializeToElement(new
        {
            nextNodeKeys = new[] { "end" },
            assigneePolicy,
        });

        var valid = WorkflowAssigneePolicy.TryRead(config, out _);

        Assert.IsFalse(valid);
    }
}
