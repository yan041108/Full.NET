using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Workflow.Domain;
using Full.NET.Modules.Workflow.Features;
using NSubstitute;

namespace Full.NET.UnitTests.Workflow;

/// <summary>激活成员是实际解析快照，配置列表人数不能提前关闭会签。</summary>
[TestClass]
public sealed class WorkflowApprovalAssigneeCoordinatorTests
{
    [TestMethod]
    [DataRow("approval", "auto_pass")]
    [DataRow("task", "auto_pass")]
    [DataRow("claim", "auto_pass")]
    [DataRow("vote", "suspend")]
    public async Task Empty_handling_preserves_system_outcome_without_constructing_a_human_activation(string kind, string expected)
    {
        var role = Guid.CreateVersion7(); var roles = Substitute.For<IWorkflowRoleMemberDirectory>();
        roles.FindActiveMemberUserIdsByRoleIdsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), 21, Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, IReadOnlyList<Guid>> { [role] = [] });
        var coordinator = new WorkflowHumanActivityAssigneeCoordinator(new(Substitute.For<IHostUserBatchSelectionDirectory>(),
            Substitute.For<ITenantMemberSelectionDirectory>(), roles, Substitute.For<Full.NET.Modules.Organization.Contracts.IWorkflowOrganizationAssigneeDirectory>()));
        var policy = new WorkflowAssigneePolicy([new(WorkflowAssigneePolicy.RoleMembers, [], [role], null, null)], new(new("auto_pass", []), new("keep", [])));
        var scope = new WorkflowManagementScope(null, "host", "host"); var initiator = Guid.CreateVersion7();
        var result = kind switch {
            "task" => await coordinator.ResolveTaskAsync(policy, new("all"), scope, initiator, default),
            "claim" => await coordinator.ResolveClaimAsync(policy, new(true), scope, initiator, default),
            "vote" => await coordinator.ResolveVoteAsync(policy, new(Guid.CreateVersion7(), 100, 100, true, null, null, null), scope, initiator, default),
            _ => await coordinator.ResolveAsync(policy, null, scope, initiator)
        };
        Assert.IsTrue(result.IsSuccess); var prepared = result.Value!;
        Assert.AreEqual(expected, prepared.Handling!.ActionKey); Assert.AreEqual(Guid.Empty, prepared.FallbackAssigneeUserId);
        Assert.IsNull(prepared.ApprovalPolicy); Assert.IsNull(prepared.Task); Assert.IsNull(prepared.Vote); Assert.IsNull(prepared.Claim);
        using var json = System.Text.Json.JsonDocument.Parse(prepared.ResolutionJson!);
        Assert.AreEqual(expected, json.RootElement.GetProperty("handling").GetProperty("actionKey").GetString());
        Assert.AreEqual(0, json.RootElement.GetProperty("memberUserIds").GetArrayLength());
    }

    [TestMethod]
    public async Task Activation_snapshot_binds_actual_members_sources_and_exact_json_hash()
    {
        var users = new[] { Guid.CreateVersion7(), Guid.CreateVersion7() };
        var initiator = Guid.CreateVersion7();
        var result = await WorkflowTodoManagementTestDependencies.CreateAssigneeCoordinator().ResolveAsync(
            new([new(WorkflowAssigneePolicy.SpecifiedUsers, [users[0]], [], null, null)]),
            new("all", users, 2), new(null, "host", "host"), initiator);
        Assert.IsTrue(result.IsSuccess);
        var value = result.Value!;
        Assert.IsNotNull(value.ResolutionJson);
        Assert.AreEqual(Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(value.ResolutionJson))), value.ResolutionHash);
        using var json = System.Text.Json.JsonDocument.Parse(value.ResolutionJson);
        Assert.AreEqual(initiator, json.RootElement.GetProperty("initiatorUserId").GetGuid());
        CollectionAssert.AreEquivalent(users, json.RootElement.GetProperty("memberUserIds").EnumerateArray().Select(x => x.GetGuid()).ToArray());
        var sources = json.RootElement.GetProperty("sources");
        Assert.AreEqual(2, sources.GetArrayLength());
        Assert.AreEqual("assignee_policy", sources[0].GetProperty("originKey").GetString());
        Assert.AreEqual("approval_policy", sources[1].GetProperty("originKey").GetString());
    }

    [TestMethod]
    [DataRow(0, false)]
    [DataRow(1, true)]
    [DataRow(3, true)]
    public async Task Dynamic_all_freezes_the_actual_cohort_without_an_implicit_initiator(int count, bool allowed)
    {
        var ids = Enumerable.Range(0, count).Select(_ => Guid.CreateVersion7()).ToArray();
        var directory = Substitute.For<IHostUserBatchSelectionDirectory>();
        directory.FindActiveHostUsersAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(ids.ToDictionary(id => id, id => new HostUserDirectoryEntry(id, "member", "Member")));
        var coordinator = WorkflowTodoManagementTestDependencies.CreateAssigneeCoordinator(directory);
        var result = await coordinator.ResolveAsync(new([new(WorkflowAssigneePolicy.SpecifiedUsers, ids, [], null, null)]),
            new("all", [], 1), new(null, "host", "host"), Guid.CreateVersion7());
        Assert.AreEqual(allowed, result.IsSuccess);
        if (allowed) Assert.AreEqual(count, result.Value!.ApprovalPolicy!.RequiredApprovals);
    }

    [TestMethod]
    [DataRow(false, 2)]
    [DataRow(true, 3)]
    public async Task Only_an_explicit_initiator_source_expands_the_approval_cohort(bool includeInitiator, int expected)
    {
        var users = Enumerable.Range(0, 3).Select(_ => Guid.CreateVersion7()).ToArray();
        var directory = Substitute.For<IHostUserBatchSelectionDirectory>();
        directory.FindActiveHostUsersAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(call => (call.Arg<IReadOnlyCollection<Guid>>() ?? Array.Empty<Guid>()).ToDictionary(
                id => id, id => new HostUserDirectoryEntry(id, "member", "Member")));
        var json = includeInitiator
            ? "{\"approvalPolicy\":{},\"assigneePolicy\":{\"sources\":[{\"resolverKindKey\":\"initiator\"}]}}"
            : "{\"approvalPolicy\":{}}";
        using var document = System.Text.Json.JsonDocument.Parse(json);
        Assert.IsTrue(WorkflowAssigneePolicy.TryRead(document.RootElement, out var policy));
        var result = await WorkflowTodoManagementTestDependencies.CreateAssigneeCoordinator(directory).ResolveAsync(
            policy, new("all", users.Take(2).ToArray(), 2), new(null, "host", "host"), users[2]);
        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(expected, result.Value!.ApprovalPolicy!.ApproverUserIds.Count);
        Assert.AreEqual(expected, result.Value.ApprovalPolicy.RequiredApprovals);
    }

    [TestMethod]
    public async Task Percentage_denominator_uses_resolved_members_instead_of_explicit_list_count()
    {
        var users = Enumerable.Range(0, 3).Select(_ => Guid.CreateVersion7()).ToArray();
        var directory = Substitute.For<IHostUserBatchSelectionDirectory>();
        directory.FindActiveHostUsersAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(users.ToDictionary(id => id, id => new HostUserDirectoryEntry(id, "member", "Member")));
        var coordinator = WorkflowTodoManagementTestDependencies.CreateAssigneeCoordinator(directory);
        var result = await coordinator.ResolveAsync(new([new(WorkflowAssigneePolicy.SpecifiedUsers, users, [], null, null)]),
            new("percent", users.Take(2).ToArray(), 1, 50), new WorkflowManagementScope(null, "host", "host"), users[0]);
        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(3, result.Value!.ApprovalPolicy!.ApproverUserIds.Count);
        Assert.AreEqual(2, result.Value.ApprovalPolicy.RequiredApprovals);
        Assert.AreEqual(50, result.Value.ApprovalPolicy.RequiredPercentage);
    }

    [TestMethod]
    [DataRow("all", 2, 3)]
    [DataRow("sequential", 2, 3)]
    [DataRow("any", 1, 1)]
    [DataRow("nOfM", 2, 2)]
    public async Task Activation_freezes_threshold_against_resolved_distinct_cohort(string mode, int configured, int expected)
    {
        var users = Enumerable.Range(0, 3).Select(_ => Guid.CreateVersion7()).ToArray();
        var directory = Substitute.For<IHostUserBatchSelectionDirectory>();
        directory.FindActiveHostUsersAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(users.ToDictionary(id => id, id => new HostUserDirectoryEntry(id, "member", "Member")));
        var coordinator = WorkflowTodoManagementTestDependencies.CreateAssigneeCoordinator(directory);
        var result = await coordinator.ResolveAsync(new([new(WorkflowAssigneePolicy.SpecifiedUsers, users, [], null, null)]),
            new(mode, users.Take(2).ToArray(), configured), new WorkflowManagementScope(null, "host", "host"), users[0]);
        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(3, result.Value!.ApprovalPolicy!.ApproverUserIds.Count);
        Assert.AreEqual(expected, result.Value.ApprovalPolicy.RequiredApprovals);
    }
}
