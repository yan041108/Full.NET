using System.Text.Json;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Workflow.Domain;
using Full.NET.Modules.Workflow.Features;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Workflow.Persistence;
using Full.NET.Modules.Workflow.Serialization;
using Full.NET.Modules.Organization.Contracts;
using System.Security.Cryptography;
using System.Text;
using NSubstitute;

namespace Full.NET.UnitTests.Workflow;

/// <summary>发起人选择必须满足固定范围、当前作用域和有界输入，重试摘要不依赖字典插入顺序。</summary>
[TestClass]
public sealed class WorkflowStarterSelectionTests
{
    [TestMethod]
    public void Selection_policy_rejects_unknown_fields_ranges_and_unbounded_input()
    {
        foreach (var json in new[] {
            """{"selectionKey":"review","modeKey":"single","maximumUsers":1,"range":{"kindKey":"all"}}""",
            """{"selectionKey":"review","modeKey":"multiple","maximumUsers":20,"range":{"kindKey":"all"}}""" })
        { using var document = JsonDocument.Parse(json); Assert.IsTrue(WorkflowStarterSelectionPolicy.TryRead(document.RootElement, out _)); }
        foreach (var json in new[] {
            """{"selectionKey":"review","modeKey":"single","maximumUsers":2,"range":{"kindKey":"all"}}""",
            """{"selectionKey":"review","modeKey":"single","maximumUsers":"1","range":{"kindKey":"all"}}""",
            """{"selectionKey":"review","modeKey":"multiple","maximumUsers":21,"range":{"kindKey":"all"}}""",
            """{"selectionKey":"review","modeKey":"single","maximumUsers":1,"range":{"kindKey":"all","tenantId":"external"}}""",
            """{"selectionKey":"review","modeKey":"single","maximumUsers":1,"range":{"kindKey":"all"},"actorUserId":"external"}""" })
        { using var document = JsonDocument.Parse(json); Assert.IsFalse(WorkflowStarterSelectionPolicy.TryRead(document.RootElement, out _)); }
    }
    [TestMethod]
    public void Normalization_preserves_member_order_and_bounds_the_complete_start_intent()
    {
        var first = Guid.CreateVersion7(); var second = Guid.CreateVersion7();
        Assert.IsTrue(WorkflowStarterSelectionPreparation.TryNormalize(new Dictionary<string, IReadOnlyList<Guid>> { ["z"] = [second, first], ["a"] = [first] }, out var values));
        CollectionAssert.AreEqual(new[] { "a", "z" }, values.Keys.ToArray()); CollectionAssert.AreEqual(new[] { second, first }, values["z"].ToArray());
        Assert.IsFalse(WorkflowStarterSelectionPreparation.TryNormalize(new Dictionary<string, IReadOnlyList<Guid>> { ["a"] = [first, first] }, out _));
        Assert.IsFalse(WorkflowStarterSelectionPreparation.TryNormalize(new Dictionary<string, IReadOnlyList<Guid>> { ["a"] = [Guid.Empty] }, out _));
        Assert.IsFalse(WorkflowStarterSelectionPreparation.TryNormalize(Enumerable.Range(0, 65).ToDictionary(index => "choice" + index, _ => (IReadOnlyList<Guid>)new[] { first }), out _));
    }
    [TestMethod]
    public async Task Specified_and_all_ranges_never_replace_invalid_or_missing_choices_with_the_initiator()
    {
        var actor = Guid.CreateVersion7(); var foreign = Guid.CreateVersion7(); var hosts = Hosts(actor, foreign);
        var service = new WorkflowStarterSelectionPreparation(hosts, Substitute.For<ITenantMemberSelectionDirectory>(), Substitute.For<IWorkflowRoleMemberDirectory>());
        var policy = Policy("review", "specified_users", [actor]);
        Assert.IsFalse((await service.ResolveAsync([policy], Choices("review", foreign), Host(), default, true)).IsSuccess);
        Assert.IsFalse((await service.ResolveAsync([policy], null, Host(), default, true)).IsSuccess);
        Assert.IsTrue((await service.ResolveAsync([policy], Choices("review", actor), Host(), default, true)).IsSuccess);
        Assert.IsFalse((await service.ResolveAsync([policy], Choices("other", actor), Host(), default, true)).IsSuccess);
        hosts.FindActiveHostUsersAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>()).Returns(new Dictionary<Guid, HostUserDirectoryEntry>());
        Assert.IsFalse((await service.ResolveAsync([Policy("review", "all")], Choices("review", actor), Host(), default, true)).IsSuccess);
    }
    [TestMethod]
    public async Task Role_ranges_use_one_selected_intersection_and_do_not_truncate_the_full_roster()
    {
        var actor = Guid.CreateVersion7(); var role = Guid.CreateVersion7(); var roles = Substitute.For<IWorkflowRoleMemberDirectory>();
        roles.FindSelectedActiveMembersByRoleIdsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, IReadOnlyList<Guid>> { [role] = new[] { actor } });
        var service = new WorkflowStarterSelectionPreparation(Hosts(actor), Substitute.For<ITenantMemberSelectionDirectory>(), roles);
        var policy = Policy("review", "role_members", roles: [role]);
        var result = await service.ResolveAsync([policy], Choices("review", actor), Host(), default, true);
        Assert.IsTrue(result.IsSuccess); Assert.AreEqual(role, result.Value!.Memberships["review"][0].SourceId);
        Assert.AreEqual(1, roles.ReceivedCalls().Count());
        roles.FindSelectedActiveMembersByRoleIdsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, IReadOnlyList<Guid>>());
        Assert.IsFalse((await service.ResolveAsync([policy], Choices("review", actor), Host(), default, true)).IsSuccess);
    }
    [TestMethod]
    public async Task Equal_choice_maps_have_equal_hashes_while_member_order_is_meaningful()
    {
        var first = Guid.CreateVersion7(); var second = Guid.CreateVersion7();
        var service = new WorkflowStarterSelectionPreparation(Hosts(first, second), Substitute.For<ITenantMemberSelectionDirectory>(), Substitute.For<IWorkflowRoleMemberDirectory>());
        var policies = new[] { Policy("a", "all") with { ModeKey = "multiple", MaximumUsers = 2 }, Policy("z", "all") };
        var one = await service.ResolveAsync(policies, new Dictionary<string, IReadOnlyList<Guid>> { ["z"] = [first], ["a"] = [first, second] }, Host(), default, true);
        var two = await service.ResolveAsync(policies, new Dictionary<string, IReadOnlyList<Guid>> { ["a"] = [first, second], ["z"] = [first] }, Host(), default, true);
        var reordered = await service.ResolveAsync(policies, new Dictionary<string, IReadOnlyList<Guid>> { ["a"] = [second, first], ["z"] = [first] }, Host(), default, true);
        Assert.IsTrue(one.IsSuccess && two.IsSuccess && reordered.IsSuccess);
        Assert.AreEqual(one.Value!.Hash, two.Value!.Hash); Assert.AreNotEqual(one.Value.Hash, reordered.Value!.Hash);
    }
    [TestMethod]
    [DataRow("valid")]
    [DataRow("missing")]
    [DataRow("hash")]
    [DataRow("schema")]
    [DataRow("unknown")]
    [DataRow("inactive")]
    public async Task Resumed_activation_requires_its_own_intact_snapshot_and_current_identity(string scenario)
    {
        var actor = Guid.CreateVersion7(); var hosts = Hosts(actor); var query = Substitute.For<IQueryExecutor>();
        var roles = Substitute.For<IWorkflowRoleMemberDirectory>(); var tenants = Substitute.For<ITenantMemberSelectionDirectory>();
        var snapshot = new WorkflowStarterSelectionSnapshot(scenario == "schema" ? 2 : 1, Choices("review", actor));
        var json = JsonSerializer.Serialize(snapshot, WorkflowJsonSerializerContext.Default.WorkflowStarterSelectionSnapshot);
        if (scenario == "unknown") json = json[..^1] + ",\"actorUserId\":\"external\"}";
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
        if (scenario != "missing") query.QuerySingleOrDefaultAsync<WorkflowStarterSelectionRecord>(WorkflowStarterSelectionSql.Find,
            Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(new WorkflowStarterSelectionRecord(json, scenario == "hash" ? new string('0', 64) : hash));
        if (scenario == "inactive") hosts.FindActiveHostUsersAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>()).Returns(new Dictionary<Guid, HostUserDirectoryEntry>());
        var source = new { resolverKindKey = "starter_selected_users", selectionPolicy = new {
            selectionKey = "review", modeKey = "single", maximumUsers = 1, range = new { kindKey = "all" } } };
        var draft = new WorkflowDefinitionDraft(1, [
            new("start", "start", 1, JsonSerializer.SerializeToElement(new { nextNodeKeys = new[] { "task" } })),
            new("task", "human.task", 1, JsonSerializer.SerializeToElement(new { nextNodeKeys = new[] { "end" }, taskPolicy = new { completionModeKey = "all" }, assigneePolicy = new { sources = new[] { source } } })),
            new("end", "end", 1, JsonSerializer.SerializeToElement(new { }))], "automation");
        Assert.IsTrue(WorkflowRuntimePlan.TryCreate(draft, out var plan)); Assert.IsTrue(plan!.TryResolveStart(out var transition));
        var resolver = new WorkflowAssigneeResolver(hosts, tenants, roles, Substitute.For<IWorkflowOrganizationAssigneeDirectory>());
        var preparation = new WorkflowHumanActivityPreparation(new(resolver), query, new(hosts, tenants, roles));
        var prepared = await preparation.PrepareAsync(transition, Host(), Guid.CreateVersion7(), default, instanceId: Guid.CreateVersion7());
        Assert.IsTrue(prepared.TryGet("task", out var result)); Assert.AreEqual(scenario == "valid", result.IsSuccess);
        if (scenario == "valid") CollectionAssert.AreEqual(new[] { actor }, result.Value!.Task!.MemberUserIds.ToArray());
        else if (scenario != "inactive") Assert.AreEqual(0, hosts.ReceivedCalls().Count());
    }

    private static WorkflowStarterSelectionPolicy Policy(string key, string kind, IReadOnlyList<Guid>? users = null, IReadOnlyList<Guid>? roles = null) => new(key, "single", 1, new(kind, users ?? [], roles ?? []));
    private static IReadOnlyDictionary<string, IReadOnlyList<Guid>> Choices(string key, Guid user) => new Dictionary<string, IReadOnlyList<Guid>> { [key] = new[] { user } };
    private static WorkflowManagementScope Host() => new(null, "host", "host");
    private static IHostUserBatchSelectionDirectory Hosts(params Guid[] ids)
    {
        var hosts = Substitute.For<IHostUserBatchSelectionDirectory>();
        hosts.FindActiveHostUsersAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(call => call.ArgAt<IReadOnlyCollection<Guid>>(0).Where(ids.Contains).ToDictionary(id => id, id => new HostUserDirectoryEntry(id, "user", "User")));
        return hosts;
    }
}
