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
    [TestMethod]
    [DataRow(true, true, true)]
    [DataRow(true, false, false)]
    [DataRow(false, true, false)]
    public async Task Direct_manager_uses_actual_relation_revision_and_current_identity(bool valid, bool active, bool expected)
    {
        var directory = Substitute.For<IWorkflowOrganizationAssigneeDirectory>(); var manager = Guid.CreateVersion7(); var relation = Guid.CreateVersion7();
        directory.ResolveDirectManagerAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(new WorkflowDirectManagerResolution(valid, relation, manager, 7));
        var members = Substitute.For<ITenantMemberSelectionDirectory>();
        members.FindActiveTenantMembersAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>()).Returns(active ? new Dictionary<Guid, TenantUserDirectoryEntry> { [manager] = new(manager, "manager", "Manager") } : []);
        var resolver = new WorkflowAssigneeResolver(Substitute.For<IHostUserBatchSelectionDirectory>(), members, Substitute.For<IWorkflowRoleMemberDirectory>(), directory);
        var capture = new WorkflowAssigneeResolutionCapture(); var initiator = Guid.CreateVersion7();
        var result = await resolver.ResolveAsync(new([new(WorkflowAssigneePolicy.InitiatorDirectManager, [], [], null, null)]), [], new(Guid.CreateVersion7(), "tenant", "tenant:test"), initiator, capture: capture);
        Assert.AreEqual(expected, result.IsSuccess);
        if (expected) { var basis = capture.Create(initiator, result.Value!).Sources.Single().Memberships.Single(); Assert.AreEqual(relation, basis.SourceId); Assert.AreEqual(7L, basis.SourceRevision); Assert.IsNull(basis.Level); }
    }

    [TestMethod]
    [DataRow(true, true, true)]
    [DataRow(true, false, false)]
    [DataRow(false, true, false)]
    public async Task Explicit_empty_leadership_may_use_handling_but_invalid_source_never_does(bool valid, bool allowEmpty, bool expected)
    {
        var directory = Substitute.For<IWorkflowOrganizationAssigneeDirectory>(); var unit = Guid.CreateVersion7();
        directory.ResolveLeaderSourceAsync(Arg.Any<Guid>(), Arg.Any<WorkflowOrganizationLeaderSource>(), Arg.Any<CancellationToken>())
            .Returns(new WorkflowOrganizationLeaderResolution(valid, []));
        var members = Substitute.For<ITenantMemberSelectionDirectory>();
        var resolver = new WorkflowAssigneeResolver(Substitute.For<IHostUserBatchSelectionDirectory>(), members,
            Substitute.For<IWorkflowRoleMemberDirectory>(), directory);
        var result = await resolver.ResolveAsync(new([new(WorkflowAssigneePolicy.OrganizationUnitLeader, [], [], unit, null)]), [],
            new(Guid.CreateVersion7(), "tenant", "tenant:test"), Guid.CreateVersion7(), allowEmpty: allowEmpty);
        Assert.AreEqual(expected, result.IsSuccess); Assert.AreEqual(0, members.ReceivedCalls().Count());
    }

    [TestMethod]
    [DataRow("position_members", 3, true)]
    [DataRow("organization_unit_members", 3, true)]
    [DataRow("initiator_leader_chain", 3, true)]
    [DataRow("position_members", 0, false)]
    [DataRow("position_members", 21, false)]
    public async Task Organization_sources_are_bounded_and_revalidated_in_one_identity_batch(string kind, int count, bool allowed)
    {
        var sourceId = Guid.CreateVersion7(); var users = Enumerable.Range(0, count).Select(_ => Guid.CreateVersion7()).ToArray();
        var organizations = Substitute.For<IWorkflowOrganizationAssigneeDirectory>();
        var members = new Dictionary<Guid, IReadOnlyList<Guid>> { [sourceId] = users };
        organizations.FindActivePositionMemberUserIdsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), 21, Arg.Any<CancellationToken>()).Returns(members);
        organizations.FindActiveUnitMemberUserIdsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), 21, Arg.Any<CancellationToken>()).Returns(members);
        organizations.ResolveLeaderSourceAsync(Arg.Any<Guid>(), Arg.Is<WorkflowOrganizationLeaderSource>(source => source != null && source.Kind == WorkflowOrganizationLeaderSourceKind.InitiatorLeaderChain && source.Depth == 3), Arg.Any<CancellationToken>())
            .Returns(new WorkflowOrganizationLeaderResolution(true, users.Select((id, index) => new WorkflowOrganizationLeaderDirectoryEntry(sourceId, id, index+1)).ToArray()));
        var tenantUsers = Substitute.For<ITenantMemberSelectionDirectory>();
        tenantUsers.FindActiveTenantMembersAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(users.ToDictionary(id => id, id => new TenantUserDirectoryEntry(id, "member", "Member")));
        var resolver = new WorkflowAssigneeResolver(Substitute.For<IHostUserBatchSelectionDirectory>(), tenantUsers,
            Substitute.For<IWorkflowRoleMemberDirectory>(), organizations);
        var source = kind switch {
            "position_members" => new WorkflowAssigneeSource(kind, [], [], null, null, [sourceId]),
            "organization_unit_members" => new WorkflowAssigneeSource(kind, [], [], sourceId, null),
            _ => new WorkflowAssigneeSource(kind, [], [], null, 3),
        };
        var initiator = Guid.CreateVersion7();
        var capture = new WorkflowAssigneeResolutionCapture();
        var result = await resolver.ResolveAsync(new([source]), [], new(Guid.CreateVersion7(), "tenant", "tenant:test"), initiator, capture: capture);
        Assert.AreEqual(allowed, result.IsSuccess);
        await tenantUsers.Received(allowed ? 1 : 0).FindActiveTenantMembersAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>());
        if (allowed)
        {
            CollectionAssert.AreEquivalent(users, result.Value!.ToArray());
            var snapshot = capture.Create(initiator, result.Value!);
            Assert.AreEqual(1, snapshot.Sources.Count);
            CollectionAssert.AreEquivalent(users, snapshot.Sources[0].Memberships.Select(x => x.UserId).ToArray());
            Assert.IsTrue(snapshot.Sources[0].Memberships.All(x => x.SourceId == sourceId));
            if (kind == "initiator_leader_chain")
                CollectionAssert.AreEqual(new[] { 1, 2, 3 }, snapshot.Sources[0].Memberships.Select(x => x.Level!.Value).ToArray());
        }
    }

    [TestMethod]
    public async Task Host_cannot_expand_into_organization_sources_and_inactive_members_are_not_dropped()
    {
        var organizations = Substitute.For<IWorkflowOrganizationAssigneeDirectory>();
        var tenantUsers = Substitute.For<ITenantMemberSelectionDirectory>();
        var unit = Guid.CreateVersion7(); var inactive = Guid.CreateVersion7();
        organizations.FindActiveUnitMemberUserIdsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), 21, Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, IReadOnlyList<Guid>> { [unit] = new[] { inactive } });
        tenantUsers.FindActiveTenantMembersAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, TenantUserDirectoryEntry>());
        var resolver = new WorkflowAssigneeResolver(Substitute.For<IHostUserBatchSelectionDirectory>(), tenantUsers,
            Substitute.For<IWorkflowRoleMemberDirectory>(), organizations);
        var policy = new WorkflowAssigneePolicy([new(WorkflowAssigneePolicy.OrganizationUnitMembers, [], [], unit, null)]);
        Assert.IsFalse((await resolver.ResolveAsync(policy, [], new(null, "host", "host"), inactive)).IsSuccess);
        Assert.AreEqual(0, organizations.ReceivedCalls().Count());
        Assert.IsFalse((await resolver.ResolveAsync(policy, [], new(Guid.CreateVersion7(), "tenant", "tenant:test"), inactive)).IsSuccess);
    }

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

        var unitDirectory = Substitute.For<IWorkflowOrganizationAssigneeDirectory>();
        unitDirectory.ResolveLeaderSourceAsync(
                initiatorId,
                Arg.Is<WorkflowOrganizationLeaderSource>(source => source != null && source.Kind == WorkflowOrganizationLeaderSourceKind.InitiatorAncestorUnit && source.Depth == 2),
                Arg.Any<CancellationToken>())
            .Returns(new WorkflowOrganizationLeaderResolution(true, [new(Guid.CreateVersion7(), leaderId, 3)]));

        var tenantUsers = Substitute.For<ITenantMemberSelectionDirectory>();
        tenantUsers.FindActiveTenantMembersAsync(
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

    /// <summary>已保存 Host 发起人可在原租户自办，但续办仍必须复核其活动身份且不切换上下文。</summary>
    [TestMethod]
    public async Task ResolveAsync_revalidates_stored_host_initiator_without_tenant_directory_membership()
    {
        var initiatorId = Guid.CreateVersion7();
        var tenantId = Guid.CreateVersion7();
        var tenantUsers = Substitute.For<ITenantMemberSelectionDirectory>();
        tenantUsers.FindActiveTenantMembersAsync(
                Arg.Any<IReadOnlyCollection<Guid>>(),
                Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, TenantUserDirectoryEntry>());

        var hostUsers = Substitute.For<IHostUserBatchSelectionDirectory>();
        hostUsers.FindActiveHostUsersAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, HostUserDirectoryEntry> { [initiatorId] = new(initiatorId, "owner", "Owner") });
        var resolver = new WorkflowAssigneeResolver(
            hostUsers,
            tenantUsers,
            Substitute.For<IWorkflowRoleMemberDirectory>(),
            Substitute.For<IWorkflowOrganizationAssigneeDirectory>());

        var result = await resolver.ResolveAsync(
            WorkflowAssigneePolicy.CreateDefault(),
            [],
            new WorkflowManagementScope(tenantId, "tenant", $"tenant:{tenantId:N}"),
            initiatorId, allowStoredHostInitiator: true);

        Assert.IsTrue(result.IsSuccess);
        CollectionAssert.AreEqual(new[] { initiatorId }, result.Value!.ToArray());
        await tenantUsers.Received(1)
            .FindActiveTenantMembersAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>());
        await hostUsers.Received(1).FindActiveHostUsersAsync(Arg.Is<IReadOnlyCollection<Guid>>(ids => ids != null && ids.Count == 1 && ids.Contains(initiatorId)), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Inactive_initiator_is_not_reused_by_a_later_worker(bool tenantScope)
    {
        var tenantId = Guid.CreateVersion7();
        var resolver = new WorkflowAssigneeResolver(Substitute.For<IHostUserBatchSelectionDirectory>(),
            Substitute.For<ITenantMemberSelectionDirectory>(), Substitute.For<IWorkflowRoleMemberDirectory>(),
            Substitute.For<IWorkflowOrganizationAssigneeDirectory>());
        var scope = tenantScope ? new WorkflowManagementScope(tenantId, "tenant", $"tenant:{tenantId:N}") : new(null, "host", "host");
        var result = await resolver.ResolveAsync(WorkflowAssigneePolicy.CreateDefault(), [], scope, Guid.CreateVersion7(),
            allowStoredHostInitiator: true);
        Assert.AreEqual(WorkflowErrorCodes.DefinitionAssigneePolicyInvalid, result.Error?.Code);
    }

    [TestMethod]
    public async Task Untrusted_preview_does_not_promote_an_arbitrary_host_user_into_tenant_candidates()
    {
        var tenantId = Guid.CreateVersion7(); var hostId = Guid.CreateVersion7();
        var hosts = Substitute.For<IHostUserBatchSelectionDirectory>();
        hosts.FindActiveHostUsersAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, HostUserDirectoryEntry> { [hostId] = new(hostId, "host", "Host") });
        var resolver = new WorkflowAssigneeResolver(hosts, Substitute.For<ITenantMemberSelectionDirectory>(),
            Substitute.For<IWorkflowRoleMemberDirectory>(), Substitute.For<IWorkflowOrganizationAssigneeDirectory>());
        var result = await resolver.ResolveAsync(WorkflowAssigneePolicy.CreateDefault(), [],
            new(tenantId, "tenant", $"tenant:{tenantId:N}"), hostId);
        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(0, hosts.ReceivedCalls().Count());
    }

    /// <summary>上级部门负责人无法解析时必须失败关闭。</summary>
    [TestMethod]
    public async Task ResolveAsync_rejects_unresolved_initiator_ancestor_unit_leader()
    {
        var initiatorId = Guid.CreateVersion7();
        var tenantId = Guid.CreateVersion7();
        var unitDirectory = Substitute.For<IWorkflowOrganizationAssigneeDirectory>();
        unitDirectory.ResolveLeaderSourceAsync(
                initiatorId,
                Arg.Is<WorkflowOrganizationLeaderSource>(source => source != null && source.Kind == WorkflowOrganizationLeaderSourceKind.InitiatorAncestorUnit && source.Depth == 1),
                Arg.Any<CancellationToken>())
            .Returns(new WorkflowOrganizationLeaderResolution(true, []));

        var resolver = new WorkflowAssigneeResolver(
            Substitute.For<IHostUserBatchSelectionDirectory>(),
            Substitute.For<ITenantMemberSelectionDirectory>(),
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
