using System.Text.Json;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Organization.Contracts;
using Full.NET.Modules.Workflow.Domain;
using Full.NET.Modules.Workflow.Features;
using NSubstitute;

namespace Full.NET.UnitTests.Workflow;

/// <summary>分组的门槛与成员范围分别验证，不能把组内成员数当成组间票数。</summary>
[TestClass]
public sealed class WorkflowGroupApprovalConfigurationTests
{
    [TestMethod]
    [DataRow("all", 0, 3)]
    [DataRow("any", 0, 1)]
    [DataRow("nOfM", 2, 2)]
    [DataRow("percent", 67, 3)]
    public void Group_threshold_is_frozen_from_group_count(string mode, int value, int expected)
    {
        var gate = mode == "nOfM" ? new Dictionary<string, object> { ["modeKey"] = mode, ["requiredApprovals"] = value }
            : mode == "percent" ? new Dictionary<string, object> { ["modeKey"] = mode, ["requiredPercentage"] = value }
            : new Dictionary<string, object> { ["modeKey"] = mode };
        var config = Config([Group("one", [Guid.CreateVersion7(), Guid.CreateVersion7()]),
            Group("two", [Guid.CreateVersion7()]), Group("three", [Guid.CreateVersion7()])], gate);
        Assert.IsTrue(WorkflowGroupApprovalConfiguration.TryRead(config, out var result));
        Assert.AreEqual(expected, result!.RequiredGroups);
    }

    [TestMethod]
    [DataRow("{\"modeKey\":\"nOfM\",\"requiredApprovals\":\"2\"}")]
    [DataRow("{\"modeKey\":\"percent\",\"requiredPercentage\":null}")]
    [DataRow("{\"modeKey\":\"percent\",\"requiredPercentage\":0}")]
    [DataRow("{\"modeKey\":\"all\",\"script\":\"true\"}")]
    [DataRow("{\"modeKey\":\"unknown\"}")]
    public void Malformed_group_gate_fails_closed_without_numeric_coercion(string gate)
    {
        var config = Config([Group("one", [Guid.CreateVersion7()]), Group("two", [Guid.CreateVersion7()])], JsonSerializer.Deserialize<JsonElement>(gate));
        Assert.IsFalse(WorkflowGroupApprovalConfiguration.TryRead(config, out _));
    }

    [TestMethod]
    [DataRow("same", "same")]
    [DataRow("Valid", "two")]
    [DataRow("one", "two-three")]
    public void Group_keys_are_unique_bounded_machine_keys(string first, string second)
    {
        Assert.IsFalse(WorkflowGroupApprovalConfiguration.TryRead(Config([Group(first, [Guid.CreateVersion7()]),
            Group(second, [Guid.CreateVersion7()])], new { modeKey = "all" }), out _));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Resolves_group_sources_then_validates_final_members_once_in_the_trusted_scope(bool isTenant)
    {
        var users = Enumerable.Range(0, 3).Select(_ => Guid.CreateVersion7()).ToArray();
        var config = Config([Group("one", users[..2], "sequential"), Group("two", users[2..])], new { modeKey = "all" });
        var f = Fixture(users);
        Assert.IsTrue(WorkflowApprovalPolicy.TryRead(config, out var raw));
        var scope = Scope(isTenant);
        var resolved = await f.Coordinator.ResolveAsync(WorkflowAssigneePolicy.CreateDefault(), raw, scope, Guid.CreateVersion7());
        Assert.IsTrue(resolved.IsSuccess, resolved.Error?.Code);
        Assert.AreEqual("group", resolved.Value!.ApprovalPolicy!.ModeKey);
        Assert.AreEqual(2, resolved.Value.ApprovalPolicy.RequiredApprovals);
        CollectionAssert.AreEqual(users, resolved.Value.ApprovalPolicy.ApproverUserIds.ToArray());
        Assert.AreEqual(2, resolved.Value.ApprovalPolicy.Groups![0].Policy.RequiredApprovals);
        Assert.AreEqual("sequential", resolved.Value.ApprovalPolicy.Groups[0].Policy.ModeKey);
        using var snapshot = JsonDocument.Parse(resolved.Value.ResolutionJson!);
        Assert.AreEqual(2, snapshot.RootElement.GetProperty("schemaVersion").GetInt32());
        Assert.AreEqual("one", snapshot.RootElement.GetProperty("groups")[0].GetProperty("groupKey").GetString());
        await f.Host.Received(isTenant ? 0 : 1).FindActiveHostUsersAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>());
        await f.Members.Received(isTenant ? 1 : 0).FindActiveTenantMembersAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task Cross_group_overlap_never_becomes_two_votes_or_a_partial_trusted_cohort()
    {
        var user = Guid.CreateVersion7(); var f = Fixture([user]);
        Assert.IsTrue(WorkflowApprovalPolicy.TryRead(Config([Group("one", [user]), Group("two", [user])], new { modeKey = "all" }), out var raw));
        var result = await f.Coordinator.ResolveAsync(WorkflowAssigneePolicy.CreateDefault(), raw, Scope(false), Guid.CreateVersion7());
        Assert.IsFalse(result.IsSuccess); Assert.AreEqual(0, f.Host.ReceivedCalls().Count());
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Inactive_member_does_not_activate_the_other_group_or_fall_back_to_a_host_user(bool isTenant)
    {
        var users = new[] { Guid.CreateVersion7(), Guid.CreateVersion7() }; var f = Fixture(users[..1]);
        Assert.IsTrue(WorkflowApprovalPolicy.TryRead(Config([Group("one", users[..1]), Group("two", users[1..])], new { modeKey = "all" }), out var raw));
        var result = await f.Coordinator.ResolveAsync(WorkflowAssigneePolicy.CreateDefault(), raw, Scope(isTenant), Guid.CreateVersion7());
        Assert.IsFalse(result.IsSuccess);
        await f.Host.Received(isTenant ? 0 : 1).FindActiveHostUsersAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task Member_limit_applies_to_all_groups_and_never_truncates_a_group()
    {
        var users = Enumerable.Range(0, 21).Select(_ => Guid.CreateVersion7()).ToArray(); var f = Fixture(users);
        Assert.IsTrue(WorkflowApprovalPolicy.TryRead(Config([Group("one", users[..20]), Group("two", users[20..])], new { modeKey = "any" }), out var raw));
        var result = await f.Coordinator.ResolveAsync(WorkflowAssigneePolicy.CreateDefault(), raw, Scope(false), Guid.CreateVersion7());
        Assert.IsFalse(result.IsSuccess); Assert.AreEqual(0, f.Host.ReceivedCalls().Count());
    }

    private static JsonElement Config(object[] groups, object gate) => JsonSerializer.SerializeToElement(new { groups, groupApprovalPolicy = gate });
    private static object Group(string key, Guid[] users, string mode = "all") => new { groupKey = key,
        assigneePolicy = new { sources = new[] { new { resolverKindKey = "specified_users", userIds = users } } },
        approvalPolicy = new { modeKey = mode, approverUserIds = Array.Empty<Guid>() } };
    private static WorkflowManagementScope Scope(bool tenant) => tenant
        ? new(Guid.CreateVersion7(), "tenant", "tenant:test") : new(null, "host", "host");
    private static (WorkflowHumanActivityAssigneeCoordinator Coordinator, IHostUserBatchSelectionDirectory Host, ITenantMemberSelectionDirectory Members) Fixture(Guid[] active)
    {
        var host = Substitute.For<IHostUserBatchSelectionDirectory>(); var members = Substitute.For<ITenantMemberSelectionDirectory>();
        host.FindActiveHostUsersAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>()).Returns(active.ToDictionary(x => x, x => new HostUserDirectoryEntry(x, "user", "User")));
        members.FindActiveTenantMembersAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>()).Returns(active.ToDictionary(x => x, x => new TenantUserDirectoryEntry(x, "user", "User")));
        return (new(new(host, members, Substitute.For<IWorkflowRoleMemberDirectory>(), Substitute.For<IWorkflowOrganizationAssigneeDirectory>())), host, members);
    }
}
