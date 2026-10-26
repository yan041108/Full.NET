using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Organization.Contracts;
using Full.NET.Modules.Workflow.Domain;
using Full.NET.Modules.Workflow.Features;
using NSubstitute;

namespace Full.NET.UnitTests.Workflow;

/// <summary>无人和本人处置只消费正常空来源，身份错误、来源错误和替补耗尽不能变成隐含本人或同意票。</summary>
[TestClass]
public sealed class WorkflowAssigneeHandlingEvaluatorTests
{
    [TestMethod]
    [DataRow("suspend")]
    [DataRow("auto_pass")]
    [DataRow("auto_reject")]
    public async Task Empty_active_role_can_be_handled_but_a_missing_role_remains_an_error(string action)
    {
        var role = Guid.CreateVersion7(); var roles = EmptyRole(role); var resolver = Resolver(Hosts(), roles);
        var policy = Role(role) with { Handling = new(new(action, []), new("keep", [])) };
        var result = await Evaluate(resolver, policy, Guid.CreateVersion7());
        Assert.IsTrue(result.IsSuccess); Assert.AreEqual(action, result.Value!.Disposition!.ActionKey); Assert.AreEqual(0, result.Value.Members.Count);
        roles.FindActiveMemberUserIdsByRoleIdsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), 21, Arg.Any<CancellationToken>()).Returns(new Dictionary<Guid, IReadOnlyList<Guid>>());
        Assert.IsFalse((await Evaluate(resolver, policy, Guid.CreateVersion7())).IsSuccess);
    }

    [TestMethod]
    public async Task Skipping_the_initiator_preserves_other_order_but_cannot_hide_a_revoked_identity()
    {
        var initiator = Guid.CreateVersion7(); var other = Guid.CreateVersion7(); var hosts = Hosts(initiator, other);
        var policy = Users(initiator, other) with { Handling = new(new("error", []), new("skip", [])) };
        var result = await Evaluate(Resolver(hosts, Substitute.For<IWorkflowRoleMemberDirectory>()), policy, initiator);
        Assert.IsTrue(result.IsSuccess); CollectionAssert.AreEqual(new[] { other }, result.Value!.Members.ToArray());
        hosts.FindActiveHostUsersAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>()).Returns(new Dictionary<Guid, HostUserDirectoryEntry> { [other] = new(other, "other", "Other") });
        Assert.IsFalse((await Evaluate(Resolver(hosts, Substitute.For<IWorkflowRoleMemberDirectory>()), policy, initiator)).IsSuccess);
    }

    [TestMethod]
    public async Task Empty_and_initiator_replacement_share_three_attempts_and_capture_the_actual_origin()
    {
        var initiator = Guid.CreateVersion7(); var replacement = Guid.CreateVersion7(); var role = Guid.CreateVersion7();
        var policy = Role(role) with { Handling = new(new("assign", [Initiator()]), new("assign", [Initiator(), Users(replacement)])) };
        var capture = new WorkflowAssigneeResolutionCapture();
        var result = await WorkflowAssigneeHandlingEvaluator.EvaluateAsync(Resolver(Hosts(initiator, replacement), EmptyRole(role)), policy, [],
            Host(), initiator, capture, default);
        Assert.IsTrue(result.IsSuccess); CollectionAssert.AreEqual(new[] { replacement }, result.Value!.Members.ToArray());
        CollectionAssert.AreEqual(new[] { "assignee_policy", "handling.empty.0", "handling.initiator.0", "handling.initiator.1" },
            capture.Create(initiator, result.Value.Members).Sources.Select(source => source.OriginKey).ToArray());
        var exhausted = policy with { Handling = new(new("assign", [Initiator()]), new("assign", [Initiator(), Initiator(), Users(replacement)])) };
        var suspended = await Evaluate(Resolver(Hosts(initiator, replacement), EmptyRole(role)), exhausted, initiator);
        Assert.IsTrue(suspended.IsSuccess); Assert.AreEqual("suspend", suspended.Value!.Disposition!.ActionKey);
        Assert.AreEqual("fallback_exhausted", suspended.Value.Disposition.TriggerKey);
    }

    [TestMethod]
    public async Task An_invalid_explicit_fallback_identity_does_not_try_another_fallback_or_auto_pass()
    {
        var missing = Guid.CreateVersion7(); var valid = Guid.CreateVersion7(); var role = Guid.CreateVersion7(); var hosts = Hosts(valid);
        var policy = Role(role) with { Handling = new(new("assign", [Users(missing), Users(valid)]), new("keep", [])) };
        Assert.IsFalse((await Evaluate(Resolver(hosts, EmptyRole(role)), policy, Guid.CreateVersion7())).IsSuccess);
        await hosts.Received(1).FindActiveHostUsersAsync(Arg.Is<IReadOnlyCollection<Guid>>(ids => ids != null && ids.Contains(missing) && !ids.Contains(valid)), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task Missing_dynamic_context_is_not_a_normal_empty_source()
    {
        var source = new WorkflowAssigneeSource(WorkflowAssigneePolicy.FormUsers, [], [], null, null, FieldKey: "users");
        var policy = new WorkflowAssigneePolicy([source], new(new("auto_pass", []), new("keep", [])));
        Assert.IsFalse((await Evaluate(Resolver(Hosts(), Substitute.For<IWorkflowRoleMemberDirectory>()), policy, Guid.CreateVersion7())).IsSuccess);
    }

    private static Task<Full.NET.Abstractions.Results.Result<WorkflowHandledAssignees>> Evaluate(WorkflowAssigneeResolver resolver, WorkflowAssigneePolicy policy, Guid initiator) =>
        WorkflowAssigneeHandlingEvaluator.EvaluateAsync(resolver, policy, [], Host(), initiator, new(), default);
    private static WorkflowManagementScope Host() => new(null, "host", "host");
    private static WorkflowAssigneePolicy Users(params Guid[] ids) => new([new(WorkflowAssigneePolicy.SpecifiedUsers, ids, [], null, null)]);
    private static WorkflowAssigneePolicy Role(Guid id) => new([new(WorkflowAssigneePolicy.RoleMembers, [], [id], null, null)]);
    private static WorkflowAssigneePolicy Initiator() => new([new(WorkflowAssigneePolicy.Initiator, [], [], null, null)]);
    private static WorkflowAssigneeResolver Resolver(IHostUserBatchSelectionDirectory hosts, IWorkflowRoleMemberDirectory roles) =>
        new(hosts, Substitute.For<ITenantMemberSelectionDirectory>(), roles, Substitute.For<IWorkflowOrganizationAssigneeDirectory>());
    private static IWorkflowRoleMemberDirectory EmptyRole(Guid role)
    {
        var roles = Substitute.For<IWorkflowRoleMemberDirectory>();
        roles.FindActiveMemberUserIdsByRoleIdsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), 21, Arg.Any<CancellationToken>()).Returns(new Dictionary<Guid, IReadOnlyList<Guid>> { [role] = Array.Empty<Guid>() }); return roles;
    }
    private static IHostUserBatchSelectionDirectory Hosts(params Guid[] active)
    {
        var hosts = Substitute.For<IHostUserBatchSelectionDirectory>();
        hosts.FindActiveHostUsersAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(call => call.ArgAt<IReadOnlyCollection<Guid>>(0).Where(active.Contains).ToDictionary(id => id, id => new HostUserDirectoryEntry(id, "user", "User"))); return hosts;
    }
}
