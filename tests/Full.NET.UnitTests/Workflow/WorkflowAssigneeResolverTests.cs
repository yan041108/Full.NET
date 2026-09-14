using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Organization.Contracts;
using Full.NET.Modules.Workflow.Contracts;
using Full.NET.Modules.Workflow.Domain;
using Full.NET.Modules.Workflow.Features;
using NSubstitute;

namespace Full.NET.UnitTests.Workflow;

/// <summary>验证办理人解析器对上级部门负责人来源的运行时语义。</summary>
[TestClass]
public sealed class WorkflowAssigneeResolverTests
{
    /// <summary>上级部门负责人来源应委托组织目录并返回唯一活动用户。</summary>
    [TestMethod]
    public async Task ResolveAsync_resolves_initiator_ancestor_unit_leader()
    {
        var initiatorId = Guid.CreateVersion7();
        var leaderId = Guid.CreateVersion7();
        var tenantId = Guid.CreateVersion7();
        var tenant = Substitute.For<ICurrentTenant>();
        tenant.IsHost.Returns(false);
        tenant.IsAvailable.Returns(true);
        tenant.Id.Returns(tenantId);

        var unitDirectory = Substitute.For<IWorkflowUnitLeaderDirectory>();
        unitDirectory.FindInitiatorAncestorUnitLeaderUserIdAsync(
                initiatorId,
                2,
                Arg.Any<CancellationToken>())
            .Returns(leaderId);

        var tenantUsers = Substitute.For<ITenantUserSelectionDirectory>();
        tenantUsers.FindActiveTenantUsersAsync(
                Arg.Any<IReadOnlyCollection<Guid>>(),
                Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, TenantUserDirectoryEntry>
            {
                [leaderId] = new(leaderId, "leader", "Leader"),
            });

        var resolver = new WorkflowAssigneeResolver(
            Substitute.For<IHostUserBatchSelectionDirectory>(),
            tenantUsers,
            Substitute.For<IWorkflowRoleMemberDirectory>(),
            unitDirectory);

        var policy = new WorkflowAssigneePolicy([
            new WorkflowAssigneeSource(
                WorkflowAssigneePolicy.InitiatorAncestorUnitLeader,
                [],
                [],
                null,
                2),
        ]);

        var result = await resolver.ResolveAsync(
            policy,
            [],
            new WorkflowManagementScope(tenantId, "tenant", $"tenant:{tenantId:N}"),
            initiatorId);

        Assert.IsTrue(result.IsSuccess);
        CollectionAssert.AreEqual(new[] { leaderId }, result.Value!.ToArray());
    }

    /// <summary>租户默认发起人策略应信任当前已认证发起人，不要求其出现在租户用户目录。</summary>
    [TestMethod]
    public async Task ResolveAsync_accepts_authenticated_initiator_without_tenant_directory_membership()
    {
        var initiatorId = Guid.CreateVersion7();
        var tenantId = Guid.CreateVersion7();
        var tenantUsers = Substitute.For<ITenantUserSelectionDirectory>();
        tenantUsers.FindActiveTenantUsersAsync(
                Arg.Any<IReadOnlyCollection<Guid>>(),
                Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, TenantUserDirectoryEntry>());

        var resolver = new WorkflowAssigneeResolver(
            Substitute.For<IHostUserBatchSelectionDirectory>(),
            tenantUsers,
            Substitute.For<IWorkflowRoleMemberDirectory>(),
            Substitute.For<IWorkflowUnitLeaderDirectory>());

        var result = await resolver.ResolveAsync(
            WorkflowAssigneePolicy.CreateDefault(),
            [],
            new WorkflowManagementScope(tenantId, "tenant", $"tenant:{tenantId:N}"),
            initiatorId);

        Assert.IsTrue(result.IsSuccess);
        CollectionAssert.AreEqual(new[] { initiatorId }, result.Value!.ToArray());
        await tenantUsers.DidNotReceive()
            .FindActiveTenantUsersAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>());
    }

    /// <summary>上级部门负责人无法解析时必须失败关闭。</summary>
    [TestMethod]
    public async Task ResolveAsync_rejects_unresolved_initiator_ancestor_unit_leader()
    {
        var initiatorId = Guid.CreateVersion7();
        var tenantId = Guid.CreateVersion7();
        var unitDirectory = Substitute.For<IWorkflowUnitLeaderDirectory>();
        unitDirectory.FindInitiatorAncestorUnitLeaderUserIdAsync(
                initiatorId,
                1,
                Arg.Any<CancellationToken>())
            .Returns((Guid?)null);

        var resolver = new WorkflowAssigneeResolver(
            Substitute.For<IHostUserBatchSelectionDirectory>(),
            Substitute.For<ITenantUserSelectionDirectory>(),
            Substitute.For<IWorkflowRoleMemberDirectory>(),
            unitDirectory);

        var policy = new WorkflowAssigneePolicy([
            new WorkflowAssigneeSource(
                WorkflowAssigneePolicy.InitiatorAncestorUnitLeader,
                [],
                [],
                null,
                1),
        ]);

        var result = await resolver.ResolveAsync(
            policy,
            [],
            new WorkflowManagementScope(tenantId, "tenant", $"tenant:{tenantId:N}"),
            initiatorId);

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(WorkflowErrorCodes.DefinitionAssigneePolicyInvalid, result.Error!.Code);
    }
}
