using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Organization.Contracts;
using Full.NET.Modules.Workflow.Domain;
using Full.NET.Modules.Workflow.Features;
using NSubstitute;

namespace Full.NET.UnitTests.Workflow;

/// <summary>发布允许声明了无人处理的活动空角色，但不能把无效角色或无策略空角色发布成可执行版本。</summary>
[TestClass]
public sealed class WorkflowAssigneeHandlingPublishTests
{
    [TestMethod]
    [DataRow("position_members", true)]
    [DataRow("position_members", false)]
    [DataRow("organization_unit_members", true)]
    [DataRow("organization_unit_members", false)]
    public async Task Empty_organization_source_requires_its_active_owner_entry(string kind, bool active)
    {
        var id = Guid.CreateVersion7(); var organization = Substitute.For<IWorkflowOrganizationAssigneeDirectory>();
        IReadOnlyDictionary<Guid, IReadOnlyList<Guid>> members = active ? new Dictionary<Guid, IReadOnlyList<Guid>> { [id] = [] } : new Dictionary<Guid, IReadOnlyList<Guid>>();
        organization.FindActivePositionMemberUserIdsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), 21, Arg.Any<CancellationToken>()).Returns(members);
        organization.FindActiveUnitMemberUserIdsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), 21, Arg.Any<CancellationToken>()).Returns(members);
        var validator = new WorkflowAssigneePublishValidator(Substitute.For<IHostUserBatchSelectionDirectory>(), Substitute.For<ITenantMemberSelectionDirectory>(), Substitute.For<IWorkflowRoleMemberDirectory>(), organization);
        var source = new WorkflowAssigneeSource(kind, [], [], kind == "organization_unit_members" ? id : null, null,
            kind == "position_members" ? [id] : null);
        var policy = new WorkflowAssigneePolicy([source], new(new("suspend", []), new("keep", [])));
        var tenantId = Guid.CreateVersion7(); var scope = new WorkflowManagementScope(tenantId, "tenant", $"tenant:{tenantId:N}");
        Assert.AreEqual(active, await validator.ValidatePolicyAsync(policy, scope, default));
        Assert.IsFalse(await validator.ValidatePolicyAsync(policy with { Handling = null }, scope, default));
    }

    [TestMethod]
    [DataRow(true, true, true)]
    [DataRow(false, true, false)]
    [DataRow(true, false, false)]
    public async Task Empty_role_requires_an_active_source_and_explicit_empty_handling(bool active, bool handled, bool expected)
    {
        var id = Guid.CreateVersion7(); var roles = Substitute.For<IWorkflowRoleMemberDirectory>();
        roles.FindActiveRolesAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(active ? new Dictionary<Guid, WorkflowRoleDirectoryEntry> { [id] = new(id, "empty", "Empty") } : []);
        roles.FindActiveMemberUserIdsByRoleIdsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), 1, Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, IReadOnlyList<Guid>> { [id] = [] });
        var validator = new WorkflowAssigneePublishValidator(Substitute.For<IHostUserBatchSelectionDirectory>(), Substitute.For<ITenantMemberSelectionDirectory>(),
            roles, Substitute.For<IWorkflowOrganizationAssigneeDirectory>());
        var policy = new WorkflowAssigneePolicy([new(WorkflowAssigneePolicy.RoleMembers, [], [id], null, null)], handled ? new(new("suspend", []), new("keep", [])) : null);
        Assert.AreEqual(expected, await validator.ValidatePolicyAsync(policy, new(null, "host", "host"), default));
    }

    [TestMethod]
    public async Task An_empty_fallback_can_be_tried_but_a_missing_later_fallback_is_not_hidden()
    {
        var main = Guid.CreateVersion7(); var empty = Guid.CreateVersion7(); var missing = Guid.CreateVersion7(); var roles = Substitute.For<IWorkflowRoleMemberDirectory>();
        roles.FindActiveRolesAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>()).Returns(call =>
            call.ArgAt<IReadOnlyCollection<Guid>>(0).Where(id => id != missing).ToDictionary(id => id, id => new WorkflowRoleDirectoryEntry(id, "role", "Role")));
        roles.FindActiveMemberUserIdsByRoleIdsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), 1, Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, IReadOnlyList<Guid>> { [main] = [Guid.CreateVersion7()] });
        var validator = new WorkflowAssigneePublishValidator(Substitute.For<IHostUserBatchSelectionDirectory>(), Substitute.For<ITenantMemberSelectionDirectory>(), roles, Substitute.For<IWorkflowOrganizationAssigneeDirectory>());
        WorkflowAssigneePolicy Role(Guid id) => new([new(WorkflowAssigneePolicy.RoleMembers, [], [id], null, null)]);
        var policy = Role(main) with { Handling = new(new("error", []), new("assign", [Role(empty)])) };
        Assert.IsTrue(await validator.ValidatePolicyAsync(policy, new(null, "host", "host"), default));
        Assert.IsFalse(await validator.ValidatePolicyAsync(policy with { Handling = new(new("error", []), new("assign", [Role(empty), Role(missing)])) }, new(null, "host", "host"), default));
    }
}
