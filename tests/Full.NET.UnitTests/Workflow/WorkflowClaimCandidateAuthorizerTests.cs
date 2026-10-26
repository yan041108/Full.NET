using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Organization.Contracts;
using Full.NET.Modules.Workflow.Domain;
using NSubstitute;

namespace Full.NET.UnitTests.Workflow;

/// <summary>领取校验按实际用户证明来源，不能因角色增长重新截断候选或在业务事务内跨模块查询。</summary>
[TestClass]
public sealed class WorkflowClaimCandidateAuthorizerTests
{
    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task Direct_manager_claim_requires_the_current_explicit_manager(bool current)
    {
        var actor = Guid.CreateVersion7(); var initiator = Guid.CreateVersion7(); var users = Substitute.For<ITenantMemberSelectionDirectory>();
        users.FindActiveTenantMembersAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>()).Returns(new Dictionary<Guid, TenantUserDirectoryEntry> { [actor] = new(actor, "manager", "Manager") });
        var roles = Substitute.For<IWorkflowRoleMemberDirectory>(); var organizations = Substitute.For<IWorkflowOrganizationAssigneeDirectory>();
        organizations.ResolveDirectManagerAsync(initiator, Arg.Any<CancellationToken>()).Returns(new WorkflowDirectManagerResolution(true, Guid.CreateVersion7(), current ? actor : Guid.CreateVersion7(), 2));
        var authorizer = new WorkflowClaimCandidateAuthorizer(new(Substitute.For<IHostUserBatchSelectionDirectory>(), users, roles, organizations), roles, organizations, Substitute.For<IDataTransactionState>());
        var actual = await authorizer.IsCurrentCandidateAsync(new([new(WorkflowAssigneePolicy.InitiatorDirectManager, [], [], null, null)]), new(Guid.CreateVersion7(), "tenant", "tenant:test"), initiator, actor, default);
        Assert.AreEqual(current, actual);
    }

    [TestMethod]
    [DataRow(true, true, true, true)]
    [DataRow(false, true, true, false)]
    [DataRow(true, false, true, false)]
    [DataRow(true, true, false, false)]
    public async Task Replacement_requires_the_actual_captured_origin_final_member_and_current_role(bool actualOrigin, bool finalMember, bool currentMember, bool expected)
    {
        var actor = Guid.CreateVersion7(); var initiator = Guid.CreateVersion7();
        var emptyRole = Guid.CreateVersion7(); var usedRole = Guid.CreateVersion7(); var unusedRole = Guid.CreateVersion7();
        var hosts = Substitute.For<IHostUserBatchSelectionDirectory>();
        hosts.FindActiveHostUsersAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, HostUserDirectoryEntry> { [actor] = new(actor, "user", "User") });
        var roles = Substitute.For<IWorkflowRoleMemberDirectory>();
        roles.IsActiveMemberOfAnyRoleAsync(actor, Arg.Is<IReadOnlyCollection<Guid>>(ids => ids != null && ids.Contains(usedRole)), Arg.Any<CancellationToken>()).Returns(currentMember);
        roles.IsActiveMemberOfAnyRoleAsync(actor, Arg.Is<IReadOnlyCollection<Guid>>(ids => ids != null && ids.Contains(unusedRole)), Arg.Any<CancellationToken>()).Returns(true);
        var organizations = Substitute.For<IWorkflowOrganizationAssigneeDirectory>();
        var authorizer = new WorkflowClaimCandidateAuthorizer(new(hosts, Substitute.For<ITenantMemberSelectionDirectory>(), roles, organizations), roles, organizations, Substitute.For<IDataTransactionState>());
        WorkflowAssigneePolicy Role(Guid id) => new([new(WorkflowAssigneePolicy.RoleMembers, [], [id], null, null)]);
        var policy = Role(emptyRole) with { Handling = new(new("assign", [Role(usedRole), Role(unusedRole)]), new("keep", [])) };
        var frozen = new WorkflowAssigneeResolutionSnapshot(1, initiator, finalMember ? [actor] : [],
            [new(actualOrigin ? "handling.empty.0" : "handling.empty.1", 0, Role(usedRole).Sources[0], [actor], [new(usedRole, actor)])]);
        Assert.AreEqual(expected, await authorizer.IsCurrentCandidateAsync(policy, new(null, "host", "host"), initiator, actor, default, frozen));
        await roles.DidNotReceive().IsActiveMemberOfAnyRoleAsync(actor,
            Arg.Is<IReadOnlyCollection<Guid>>(ids => ids != null && ids.Contains(unusedRole)), Arg.Any<CancellationToken>());
        Assert.IsFalse(await authorizer.IsCurrentCandidateAsync(policy, new(null, "host", "host"), initiator, actor, default));
    }

    [TestMethod]
    [DataRow(true, true, true)]
    [DataRow(true, false, false)]
    [DataRow(false, true, false)]
    public async Task Role_candidate_requires_active_identity_and_current_direct_membership(bool active, bool member, bool expected)
    {
        var actor = Guid.CreateVersion7(); var role = Guid.CreateVersion7();
        var hosts = Substitute.For<IHostUserBatchSelectionDirectory>();
        hosts.FindActiveHostUsersAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(active ? new Dictionary<Guid, HostUserDirectoryEntry> { [actor] = new(actor, "user", "User") } : []);
        var roles = Substitute.For<IWorkflowRoleMemberDirectory>();
        roles.IsActiveMemberOfAnyRoleAsync(actor, Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>()).Returns(member);
        var organizations = Substitute.For<IWorkflowOrganizationAssigneeDirectory>();
        var authorizer = new WorkflowClaimCandidateAuthorizer(new(hosts, Substitute.For<ITenantMemberSelectionDirectory>(), roles, organizations),
            roles, organizations, Substitute.For<IDataTransactionState>());
        var result = await authorizer.IsCurrentCandidateAsync(new([new(WorkflowAssigneePolicy.RoleMembers, [], [role], null, null)]),
            new(null, "host", "host"), actor, actor, default);
        Assert.AreEqual(expected, result);
        Assert.IsFalse(roles.ReceivedCalls().Any(call => call.GetMethodInfo().Name == "FindActiveRoleMemberUserIdsAsync"));
        if (active) await roles.Received(1).IsActiveMemberOfAnyRoleAsync(actor,
            Arg.Is<IReadOnlyCollection<Guid>>(ids => ids != null && ids.Count == 1 && ids.Contains(role)), Arg.Any<CancellationToken>());
        else Assert.AreEqual(0, roles.ReceivedCalls().Count());
    }

    [TestMethod]
    public async Task Ambient_transaction_is_rejected_before_any_identity_read()
    {
        var hosts = Substitute.For<IHostUserBatchSelectionDirectory>(); var roles = Substitute.For<IWorkflowRoleMemberDirectory>();
        var organizations = Substitute.For<IWorkflowOrganizationAssigneeDirectory>(); var transaction = Substitute.For<IDataTransactionState>();
        transaction.HasTransaction.Returns(true);
        var authorizer = new WorkflowClaimCandidateAuthorizer(new(hosts, Substitute.For<ITenantMemberSelectionDirectory>(), roles, organizations), roles, organizations, transaction);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => authorizer.IsCurrentCandidateAsync(WorkflowAssigneePolicy.CreateDefault(),
            new(null, "host", "host"), Guid.CreateVersion7(), Guid.CreateVersion7(), default));
        Assert.AreEqual(0, hosts.ReceivedCalls().Count());
    }
}
