using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Full.NET.Modules.Workflow.Domain;
using Full.NET.Modules.Workflow.Serialization;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Workflow.Features;
using NSubstitute;

namespace Full.NET.UnitTests.Workflow;

/// <summary>重建只读取原激活成员和固定门槛，票、已完成状态和当前角色展开均不进入新席位。</summary>
[TestClass]
public sealed class WorkflowReturnAssigneeSnapshotTests
{
    [TestMethod]
    [DataRow(false, false)]
    [DataRow(false, true)]
    [DataRow(true, false)]
    [DataRow(true, true)]
    public async Task Activation_rechecks_only_original_members_against_the_current_scope(bool tenantScope, bool unavailable)
    {
        var members = Users(3); var initiator = Guid.CreateVersion7(); var tenant = Guid.CreateVersion7();
        var host = Substitute.For<IHostUserBatchSelectionDirectory>(); var tenants = Substitute.For<ITenantMemberSelectionDirectory>();
        host.FindActiveHostUsersAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(members.Take(unavailable ? 2 : 3).ToDictionary(id => id, id => new HostUserDirectoryEntry(id, "old", "Original")));
        tenants.FindActiveTenantMembersAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(members.Take(unavailable ? 2 : 3).ToDictionary(id => id, id => new TenantUserDirectoryEntry(id, "old", "Original")));
        var json = JsonSerializer.Serialize(new WorkflowAssigneeResolutionSnapshot(1, initiator, members, []),
            WorkflowJsonSerializerContext.Default.WorkflowAssigneeResolutionSnapshot);
        var result = await WorkflowTodoManagementTestDependencies.CreateAssigneeCoordinator(host, tenants).RestoreReturnAsync("human.approval", Approval("all"),
            json, Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(json))),
            tenantScope ? new WorkflowManagementScope(tenant, "tenant", $"tenant:{tenant:N}") : new(null, "host", "host"), initiator, default);
        Assert.AreEqual(!unavailable, result.IsSuccess);
        if (result.IsSuccess) CollectionAssert.AreEqual(members, result.Value!.ApprovalPolicy!.ApproverUserIds.ToArray());
        if (tenantScope)
            await tenants.Received(1).FindActiveTenantMembersAsync(Arg.Is<IReadOnlyCollection<Guid>>(ids => ids != null && ids.Count == members.Length && ids.ToHashSet().SetEquals(members)), Arg.Any<CancellationToken>());
        else await host.Received(1).FindActiveHostUsersAsync(Arg.Is<IReadOnlyCollection<Guid>>(ids => ids != null && ids.Count == members.Length && ids.ToHashSet().SetEquals(members)), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    [DataRow("all", 3)]
    [DataRow("any", 1)]
    [DataRow("sequential", 3)]
    [DataRow("percent", 2)]
    [DataRow("nOfM", 2)]
    public void Multi_member_return_uses_original_order_and_recomputes_the_same_frozen_denominator(string mode, int required)
    {
        var initiator = Guid.CreateVersion7(); var members = Users(3);
        var snapshot = new WorkflowAssigneeResolutionSnapshot(1, initiator, members, []);
        var config = Approval(mode);
        Assert.IsTrue(Restore("human.approval", config, initiator, snapshot, out var restored));
        CollectionAssert.AreEqual(members, restored!.ApprovalPolicy!.ApproverUserIds.ToArray());
        Assert.AreEqual(required, restored.ApprovalPolicy.RequiredApprovals);
        Assert.AreEqual(Guid.Empty, restored.FallbackAssigneeUserId);
    }

    [TestMethod]
    [DataRow("all")]
    [DataRow("any")]
    [DataRow("sequential")]
    public void Task_return_preserves_members_and_has_no_approval_policy(string mode)
    {
        var initiator = Guid.CreateVersion7(); var members = Users(3);
        Assert.IsTrue(Restore("human.task", JsonSerializer.SerializeToElement(new { taskPolicy = new { completionModeKey = mode } }), initiator,
            new(1, initiator, members, []), out var restored));
        Assert.IsNull(restored!.ApprovalPolicy);
        CollectionAssert.AreEqual(members, restored.Task!.MemberUserIds.ToArray());
        Assert.AreEqual(mode, restored.Task.Policy.CompletionModeKey);
    }

    [TestMethod]
    public void Single_member_return_preserves_the_original_assignee()
    {
        var initiator = Guid.CreateVersion7(); var member = Guid.CreateVersion7();
        Assert.IsTrue(Restore("human.approval", JsonSerializer.SerializeToElement(new { }), initiator, new(1, initiator, [member], []), out var restored));
        Assert.AreEqual(member, restored!.FallbackAssigneeUserId);
        Assert.IsNull(restored.ApprovalPolicy);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Group_return_preserves_each_original_cohort_threshold_and_system_disposition(bool systemFirst)
    {
        var initiator = Guid.CreateVersion7(); var first = systemFirst ? [] : Users(2); var second = Users(2);
        var config = GroupConfiguration();
        var snapshot = new WorkflowAssigneeResolutionSnapshot(2, initiator, [..first, ..second], [],
            [new("first", null, new(1, initiator, first, [], Handling: systemFirst ? new("skip", "empty") : null)),
             new("second", null, new(1, initiator, second, []))]);
        Assert.IsTrue(Restore("human.group_approval", config, initiator, snapshot, out var restored));
        var groups = restored!.ApprovalPolicy!.Groups!;
        Assert.AreEqual(2, restored.ApprovalPolicy.RequiredApprovals);
        CollectionAssert.AreEqual(first, groups[0].Policy.ApproverUserIds.ToArray());
        CollectionAssert.AreEqual(second, groups[1].Policy.ApproverUserIds.ToArray());
        Assert.AreEqual(systemFirst ? 0 : 2, groups[0].Policy.RequiredApprovals);
        Assert.AreEqual(systemFirst ? "skip" : null, groups[0].Handling?.ActionKey);
        Assert.AreEqual(1, groups[1].Policy.RequiredApprovals);
    }

    [TestMethod]
    [DataRow("key")]
    [DataRow("cohort")]
    [DataRow("nested")]
    [DataRow("initiator")]
    [DataRow("system")]
    public void Inconsistent_group_history_cannot_be_reinterpreted_as_new_seats(string defect)
    {
        var initiator = Guid.CreateVersion7(); var members = Users(4);
        var first = new WorkflowAssigneeResolutionSnapshot(1, initiator, members.Take(2).ToArray(), []);
        var second = new WorkflowAssigneeResolutionSnapshot(1, initiator, members.Skip(2).ToArray(), []);
        second = defect switch
        {
            "cohort" => second with { MemberUserIds = [members[0], members[3]] },
            "nested" => second with { Groups = [] },
            "initiator" => second with { InitiatorUserId = Guid.CreateVersion7() },
            "system" => second with { Handling = new("skip", "empty") },
            _ => second
        };
        var snapshot = new WorkflowAssigneeResolutionSnapshot(2, initiator, members, [],
            [new("first", null, first), new(defect == "key" ? "first" : "second", null, second)]);
        Assert.IsFalse(Restore("human.group_approval", GroupConfiguration(), initiator, snapshot, out _));
    }

    [TestMethod]
    [DataRow("hash")]
    [DataRow("json")]
    [DataRow("initiator")]
    [DataRow("duplicate")]
    [DataRow("empty")]
    [DataRow("oversized")]
    [DataRow("schema")]
    [DataRow("handled")]
    [DataRow("node")]
    public void Missing_or_corrupt_historical_evidence_fails_closed(string defect)
    {
        var initiator = Guid.CreateVersion7(); var users = Users(3);
        var snapshot = new WorkflowAssigneeResolutionSnapshot(1, initiator, users, []);
        snapshot = defect switch
        {
            "initiator" => snapshot with { InitiatorUserId = Guid.CreateVersion7() },
            "duplicate" => snapshot with { MemberUserIds = [users[0], users[0]] },
            "empty" => snapshot with { MemberUserIds = [] },
            "oversized" => snapshot with { MemberUserIds = Users(21) },
            "schema" => snapshot with { SchemaVersion = 99 },
            "handled" => snapshot with { Handling = new("auto_pass", "empty") },
            _ => snapshot
        };
        var json = JsonSerializer.Serialize(snapshot, WorkflowJsonSerializerContext.Default.WorkflowAssigneeResolutionSnapshot);
        if (defect == "json") json = "{";
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
        Assert.IsFalse(WorkflowReturnAssigneeSnapshot.TryRestore(defect == "node" ? "human.vote" : "human.approval", Approval("all"), initiator,
            json, defect == "hash" ? new string('a', 64) : hash, out _));
    }

    private static JsonElement Approval(string mode)
    {
        var policy = new Dictionary<string, object> { ["modeKey"] = mode, ["approverUserIds"] = Users(3) };
        if (mode == "percent") policy["requiredPercentage"] = 50;
        if (mode == "nOfM") policy["requiredApprovals"] = 2;
        return JsonSerializer.SerializeToElement(new { approvalPolicy = policy });
    }

    private static JsonElement GroupConfiguration() => JsonSerializer.SerializeToElement(new
    {
        groupApprovalPolicy = new { modeKey = "all" },
        groups = new[]
        {
            new { groupKey = "first", approvalPolicy = Approval("sequential").GetProperty("approvalPolicy") },
            new { groupKey = "second", approvalPolicy = Approval("percent").GetProperty("approvalPolicy") }
        }
    });

    private static bool Restore(string nodeType, JsonElement config, Guid initiator, WorkflowAssigneeResolutionSnapshot snapshot,
        out WorkflowHumanActivityAssignees? restored)
    {
        var json = JsonSerializer.Serialize(snapshot, WorkflowJsonSerializerContext.Default.WorkflowAssigneeResolutionSnapshot);
        return WorkflowReturnAssigneeSnapshot.TryRestore(nodeType, config, initiator, json,
            Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(json))), out restored);
    }

    private static Guid[] Users(int count) => Enumerable.Range(0, count).Select(_ => Guid.CreateVersion7()).ToArray();
}
