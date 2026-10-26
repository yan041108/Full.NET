using System.Text.Json;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Organization.Contracts;
using Full.NET.Modules.Workflow.Domain;
using Full.NET.Modules.Workflow.Features;
using NSubstitute;

namespace Full.NET.UnitTests.Workflow;

/// <summary>固定版本的管理员名单不从超级管理员推断，也不能被节点内参数或后来的名单替代。</summary>
[TestClass]
public sealed class WorkflowFlowAdministratorsTests
{
    [TestMethod]
    public void Administrator_list_is_closed_bounded_and_unique_without_implicit_default()
    {
        Assert.IsTrue(WorkflowFlowAdministrators.TryRead(JsonSerializer.SerializeToElement(new { }), out var absent)); Assert.HasCount(0, absent);
        var id = Guid.CreateVersion7();
        Assert.IsTrue(WorkflowFlowAdministrators.TryRead(JsonSerializer.SerializeToElement(new { administratorUserIds = new[] { id } }), out var users)); CollectionAssert.AreEqual(new[] { id }, users.ToArray());
        foreach (var invalid in new object[] { new { administratorUserIds = new[] { id, id } }, new { administratorUserIds = new[] { Guid.Empty } },
            new { administratorUserIds = Enumerable.Range(0, 21).Select(_ => Guid.CreateVersion7()).ToArray() }, new { administratorUserIds = "administrator" } })
            Assert.IsFalse(WorkflowFlowAdministrators.TryRead(JsonSerializer.SerializeToElement(invalid), out _));
        Assert.IsFalse(WorkflowAssigneePolicy.TryReadPolicy(JsonSerializer.SerializeToElement(new { sources = new[] { new { resolverKindKey = "flow_administrators", userIds = new[] { id } } } }), out _));
    }

    [TestMethod]
    public void Runtime_plan_keeps_each_version_administrator_list_separate()
    {
        var first = Guid.CreateVersion7(); var later = Guid.CreateVersion7();
        WorkflowDefinitionDraft Draft(Guid id) => new(1, [new("start", "start", 1, JsonSerializer.SerializeToElement(new { nextNodeKeys = new[] { "review" }, administratorUserIds = new[] { id } })),
            new("review", "human.task", 1, JsonSerializer.SerializeToElement(new { nextNodeKeys = new[] { "end" }, taskPolicy = new { completionModeKey = "all" }, assigneePolicy = new { sources = new[] { new { resolverKindKey = "flow_administrators" } } } })),
            new("end", "end", 1, JsonSerializer.SerializeToElement(new { }))], "automation");
        Assert.IsTrue(WorkflowRuntimePlan.TryCreate(Draft(first), out var original)); Assert.IsTrue(WorkflowRuntimePlan.TryCreate(Draft(later), out var next));
        CollectionAssert.AreEqual(new[] { first }, original!.FlowAdministratorUserIds.ToArray()); CollectionAssert.AreEqual(new[] { later }, next!.FlowAdministratorUserIds.ToArray());
    }

    [TestMethod]
    [DataRow(true, true)]
    [DataRow(false, false)]
    public async Task Source_uses_explicit_context_and_revalidates_current_identity(bool active, bool expected)
    {
        var user = Guid.CreateVersion7(); var hosts = Substitute.For<IHostUserBatchSelectionDirectory>();
        hosts.FindActiveHostUsersAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>()).Returns(active ? new Dictionary<Guid, HostUserDirectoryEntry> { [user] = new(user, "admin", "Admin") } : []);
        var resolver = new WorkflowAssigneeResolver(hosts, Substitute.For<ITenantMemberSelectionDirectory>(), Substitute.For<IWorkflowRoleMemberDirectory>(), Substitute.For<IWorkflowOrganizationAssigneeDirectory>());
        var policy = Policy(); var scope = new WorkflowManagementScope(null, "host", "host"); var initiator = Guid.CreateVersion7();
        Assert.IsFalse((await resolver.ResolveAsync(policy, [], scope, initiator, allowEmpty: true)).IsSuccess);
        var result = await resolver.ResolveAsync(policy, [], scope, initiator, context: new(new Dictionary<string, JsonElement>(), new Dictionary<string, IReadOnlyList<Full.NET.Modules.Workflow.Persistence.WorkflowPreviousActorRecord>>(), FlowAdministratorUserIds: [user]));
        Assert.AreEqual(expected, result.IsSuccess); if (expected) CollectionAssert.AreEqual(new[] { user }, result.Value!.ToArray());
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Empty_published_list_only_allows_explicit_empty_handling(bool allowEmpty)
    {
        var resolver = new WorkflowAssigneeResolver(Substitute.For<IHostUserBatchSelectionDirectory>(), Substitute.For<ITenantMemberSelectionDirectory>(), Substitute.For<IWorkflowRoleMemberDirectory>(), Substitute.For<IWorkflowOrganizationAssigneeDirectory>());
        var result = await resolver.ResolveAsync(Policy(), [], new(null, "host", "host"), Guid.CreateVersion7(), context: new(new Dictionary<string, JsonElement>(), new Dictionary<string, IReadOnlyList<Full.NET.Modules.Workflow.Persistence.WorkflowPreviousActorRecord>>(), FlowAdministratorUserIds: []), allowEmpty: allowEmpty);
        Assert.AreEqual(allowEmpty, result.IsSuccess);
    }

    [TestMethod]
    public async Task Publishing_rejects_unprepared_or_inactive_administrators_even_with_empty_fallback()
    {
        var hosts = Substitute.For<IHostUserBatchSelectionDirectory>(); hosts.FindActiveHostUsersAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>()).Returns(new Dictionary<Guid, HostUserDirectoryEntry>());
        var validator = new WorkflowAssigneePublishValidator(hosts, Substitute.For<ITenantMemberSelectionDirectory>(), Substitute.For<IWorkflowRoleMemberDirectory>(), Substitute.For<IWorkflowOrganizationAssigneeDirectory>());
        var policy = Policy() with { Handling = new(new("suspend", []), new("keep", [])) }; var scope = new WorkflowManagementScope(null, "host", "host");
        Assert.IsFalse(await validator.ValidatePolicyAsync(policy, scope, default));
        Assert.IsTrue(await validator.ValidatePolicyAsync(policy, scope, default, []));
        Assert.IsFalse(await validator.ValidatePolicyAsync(policy, scope, default, [Guid.CreateVersion7()]));
    }

    [TestMethod]
    [DataRow(true, true)]
    [DataRow(false, false)]
    public async Task Claim_requires_actual_frozen_administrator_origin(bool captured, bool expected)
    {
        var actor = Guid.CreateVersion7(); var initiator = Guid.CreateVersion7(); var hosts = Substitute.For<IHostUserBatchSelectionDirectory>();
        hosts.FindActiveHostUsersAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>()).Returns(new Dictionary<Guid, HostUserDirectoryEntry> { [actor] = new(actor, "admin", "Admin") });
        var roles = Substitute.For<IWorkflowRoleMemberDirectory>(); var organizations = Substitute.For<IWorkflowOrganizationAssigneeDirectory>();
        var authorizer = new WorkflowClaimCandidateAuthorizer(new(hosts, Substitute.For<ITenantMemberSelectionDirectory>(), roles, organizations), roles, organizations, Substitute.For<Full.NET.Data.Abstractions.IDataTransactionState>());
        var policy = Policy(); var snapshot = new WorkflowAssigneeResolutionSnapshot(1, initiator, [actor], captured ? [new("assignee_policy", 0, policy.Sources[0], [actor], [])] : []);
        Assert.AreEqual(expected, await authorizer.IsCurrentCandidateAsync(policy, new(null, "host", "host"), initiator, actor, default, snapshot));
        Assert.AreEqual(0, roles.ReceivedCalls().Count()); Assert.AreEqual(0, organizations.ReceivedCalls().Count());
    }

    private static WorkflowAssigneePolicy Policy() => new([new(WorkflowAssigneePolicy.FlowAdministrators, [], [], null, null)]);
}
