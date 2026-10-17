using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Organization.Contracts;
using Full.NET.Modules.Workflow.Contracts;
using Full.NET.Modules.Workflow.Domain;
using Full.NET.Modules.Workflow.Features;
using Full.NET.Modules.Workflow.Features.ManageDefinitions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Full.NET.UnitTests.Workflow;

/// <summary>验证现代成员资格与旧角色目录冲突时，办理人与候选必须遵守成员权威来源。</summary>
[TestClass]
public sealed class WorkflowMembershipSelectionTests
{
    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task Explicit_assignee_uses_current_active_membership_async(bool activeMember)
    {
        using var provider = CreateProvider(activeMember, out var userId, out var tenantId);
        var resolver = provider.GetRequiredService<WorkflowAssigneeResolver>();
        var policy = new WorkflowAssigneePolicy([
            new WorkflowAssigneeSource(WorkflowAssigneePolicy.SpecifiedUsers, [userId], [], null, null),
        ]);
        var result = await resolver.ResolveAsync(policy, [],
            new WorkflowManagementScope(tenantId, "tenant", $"tenant:{tenantId:N}"), Guid.CreateVersion7());
        Assert.AreEqual(activeMember, result.IsSuccess);
        if (activeMember)
        {
            CollectionAssert.AreEqual(new[] { userId }, result.Value!.ToArray());
        }
        else
        {
            Assert.AreEqual(WorkflowErrorCodes.DefinitionAssigneePolicyInvalid, result.Error!.Code);
        }
        await provider.GetRequiredService<ITenantUserSelectionDirectory>()
            .DidNotReceiveWithAnyArgs().FindActiveTenantUsersAsync(default!, default);
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task Candidate_page_uses_current_active_membership_async(bool activeMember)
    {
        using var provider = CreateProvider(activeMember, out var userId, out _);
        var result = await provider.GetRequiredService<WorkflowRecipientCandidateQueryService>().ListAsync(1, 50);
        Assert.AreEqual(activeMember ? 1L : 0L, result.Total);
        CollectionAssert.AreEqual(activeMember ? new[] { userId } : Array.Empty<Guid>(),
            result.Items.Select(item => item.Id).ToArray());
        await provider.GetRequiredService<IHostUserSelectionDirectory>()
            .DidNotReceiveWithAnyArgs().ListActiveHostUsersAsync(default, default, default);
        await provider.GetRequiredService<ITenantUserSelectionDirectory>()
            .DidNotReceiveWithAnyArgs().ListActiveTenantUsersAsync(default, default, default);
    }

    private static ServiceProvider CreateProvider(bool activeMember, out Guid userId, out Guid tenantId)
    {
        userId = Guid.CreateVersion7();
        tenantId = Guid.CreateVersion7();
        var entry = new TenantUserDirectoryEntry(userId, "member", "租户成员");
        var legacy = Substitute.For<ITenantUserSelectionDirectory>();
        var members = Substitute.For<ITenantMemberBatchSelectionDirectory>();
        var memberPage = Substitute.For<ITenantMemberSelectionDirectory>();
        // 两份目录故意互相冲突：新成员没有旧角色，撤销成员仍有旧角色。
        var valid = activeMember ? new Dictionary<Guid, TenantUserDirectoryEntry> { [userId] = entry } : [];
        var stale = activeMember ? new Dictionary<Guid, TenantUserDirectoryEntry>() : new() { [userId] = entry };
        legacy.FindActiveTenantUsersAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>()).Returns(stale);
        members.FindActiveTenantMembersAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>()).Returns(valid);
        legacy.ListActiveTenantUsersAsync(1, 50, Arg.Any<CancellationToken>())
            .Returns(new PagedResult<TenantUserDirectoryEntry>(stale.Values.ToArray(), 1, 50, stale.Count));
        memberPage.ListActiveTenantMembersAsync(1, 50, Arg.Any<CancellationToken>())
            .Returns(new PagedResult<TenantUserDirectoryEntry>(valid.Values.ToArray(), 1, 50, valid.Count));
        var tenant = Substitute.For<ICurrentTenant>();
        tenant.IsHost.Returns(false); tenant.IsAvailable.Returns(true); tenant.Id.Returns(tenantId);
        var services = new ServiceCollection();
        services.AddSingleton(tenant); services.AddSingleton(legacy); services.AddSingleton(members); services.AddSingleton(memberPage);
        services.AddSingleton(Substitute.For<IHostUserBatchSelectionDirectory>());
        services.AddSingleton(Substitute.For<IHostUserSelectionDirectory>());
        services.AddSingleton(Substitute.For<IWorkflowRoleMemberDirectory>());
        services.AddSingleton(Substitute.For<IWorkflowUnitLeaderDirectory>());
        services.AddTransient<WorkflowAssigneeResolver>(); services.AddTransient<WorkflowRecipientCandidateQueryService>();
        services.AddTransient<WorkflowAssigneePublishValidator>();
        return services.BuildServiceProvider();
    }

    [TestMethod]
    [DataRow("role_members", true)]
    [DataRow("role_members", false)]
    [DataRow("organization_unit_leader", true)]
    [DataRow("organization_unit_leader", false)]
    public async Task Indirect_assignee_publication_rechecks_active_membership_async(string kind, bool activeMember)
    {
        using var provider = CreateProvider(activeMember, out var userId, out var tenantId);
        var entityId = Guid.CreateVersion7();
        var roles = provider.GetRequiredService<IWorkflowRoleMemberDirectory>();
        roles.FindActiveRolesAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, WorkflowRoleDirectoryEntry> { [entityId] = new(entityId, "role", "角色") });
        roles.FindActiveMemberUserIdsByRoleIdsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, IReadOnlyList<Guid>> { [entityId] = new[] { userId } });
        var units = provider.GetRequiredService<IWorkflowUnitLeaderDirectory>();
        units.FindActiveUnitsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, WorkflowOrganizationUnitDirectoryEntry> { [entityId] = new(entityId, "unit", "机构") });
        units.FindActiveUnitLeaderUserIdsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, Guid> { [entityId] = userId });
        // 成员撤销后旧角色和机构关系可能仍存在，发布不能只相信这些投影。
        var source = kind == WorkflowAssigneePolicy.RoleMembers
            ? new WorkflowAssigneeSource(kind, [], [entityId], null, null)
            : new WorkflowAssigneeSource(kind, [], [], entityId, null);
        var valid = await provider.GetRequiredService<WorkflowAssigneePublishValidator>().ValidateSourceAsync(source,
            new WorkflowManagementScope(tenantId, "tenant", $"tenant:{tenantId:N}"), CancellationToken.None);
        Assert.AreEqual(activeMember, valid);
    }
}
