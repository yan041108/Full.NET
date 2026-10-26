using System.Text.Json;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Organization.Contracts;
using Full.NET.Modules.Workflow.Domain;
using Full.NET.Modules.Workflow.Features;
using Full.NET.Modules.Workflow.Persistence;
using NSubstitute;

namespace Full.NET.UnitTests.Workflow;

/// <summary>动态抄送保持闭合配置、真实空来源和事务外来源快照，不预判正在完成的处理人。</summary>
[TestClass]
public sealed class WorkflowDynamicCcTests
{
    [TestMethod]
    public void Configuration_requires_one_recipient_mode_and_rejects_human_handling()
    {
        var user = Guid.CreateVersion7();
        Assert.IsTrue(WorkflowCcNodeConfiguration.TryReadPolicy(JsonSerializer.SerializeToElement(new { nextNodeKeys = new[] { "end" }, recipientUserIds = new[] { user } }), out var legacy));
        Assert.AreEqual(WorkflowAssigneePolicy.SpecifiedUsers, legacy.Sources.Single().ResolverKindKey);
        Assert.IsTrue(WorkflowCcNodeConfiguration.TryReadPolicy(Config(new { resolverKindKey = "flow_administrators" }), out _));
        Assert.IsFalse(WorkflowCcNodeConfiguration.TryReadPolicy(JsonSerializer.SerializeToElement(new { nextNodeKeys = new[] { "end" }, recipientUserIds = new[] { user }, recipientPolicy = new { sources = new[] { new { resolverKindKey = "initiator" } } } }), out _));
        Assert.IsFalse(WorkflowCcNodeConfiguration.TryReadPolicy(JsonSerializer.SerializeToElement(new { nextNodeKeys = new[] { "end" }, recipientPolicy = new { sources = new[] { new { resolverKindKey = "initiator" } }, handling = new { whenEmpty = new { actionKey = "auto_pass" } } } }), out _));
        Assert.IsFalse(WorkflowCcNodeConfiguration.TryReadPolicy(Config(new { resolverKindKey = "initiator", tenantId = Guid.CreateVersion7() }), out _));
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task Empty_active_role_records_zero_recipients_but_missing_role_is_invalid(bool exists)
    {
        var role = Guid.CreateVersion7(); var roles = Substitute.For<IWorkflowRoleMemberDirectory>();
        roles.FindActiveMemberUserIdsByRoleIdsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), 21, Arg.Any<CancellationToken>()).Returns(exists ? new Dictionary<Guid, IReadOnlyList<Guid>> { [role] = [] } : []);
        var coordinator = Coordinator(Substitute.For<IHostUserBatchSelectionDirectory>(), roles);
        Assert.IsTrue(WorkflowCcNodeConfiguration.TryReadPolicy(Config(new { resolverKindKey = "role_members", roleIds = new[] { role } }), out var policy));
        var result = await coordinator.ResolveCcAsync(policy, new(null, "host", "host"), Guid.CreateVersion7(), Context(), default);
        Assert.AreEqual(exists, result.IsSuccess);
        if (exists)
        {
            Assert.HasCount(0, result.Value!.UserIds); using var parsed = JsonDocument.Parse(result.Value.ResolutionJson);
            Assert.AreEqual(0, parsed.RootElement.GetProperty("memberUserIds").GetArrayLength()); Assert.AreEqual("recipient_policy", parsed.RootElement.GetProperty("sources")[0].GetProperty("originKey").GetString());
        }
    }

    [TestMethod]
    [DataRow("null", true)]
    [DataRow("[]", true)]
    [DataRow("\"\"", true)]
    [DataRow("\"not-an-identity\"", false)]
    [DataRow("[\"00000000-0000-0000-0000-000000000000\"]", false)]
    public async Task Empty_bound_field_differs_from_invalid_identity_input(string json, bool expected)
    {
        var resolver = new WorkflowAssigneeResolver(Substitute.For<IHostUserBatchSelectionDirectory>(), Substitute.For<ITenantMemberSelectionDirectory>(), Substitute.For<IWorkflowRoleMemberDirectory>(), Substitute.For<IWorkflowOrganizationAssigneeDirectory>());
        using var document = JsonDocument.Parse(json); var context = Context(new Dictionary<string, JsonElement> { ["people"] = document.RootElement });
        var policy = new WorkflowAssigneePolicy([new(WorkflowAssigneePolicy.FormUsers, [], [], null, null, FieldKey: "people")]);
        var result = await resolver.ResolveAsync(policy, [], new(null, "host", "host"), Guid.CreateVersion7(), context: context, allowEmpty: true);
        Assert.AreEqual(expected, result.IsSuccess);
    }

    [TestMethod]
    public async Task Current_completing_actor_is_deferred_before_creating_a_cc_snapshot()
    {
        var query = Substitute.For<IQueryExecutor>(); var roles = Substitute.For<IWorkflowRoleMemberDirectory>();
        var preparation = new WorkflowHumanActivityPreparation(Coordinator(Substitute.For<IHostUserBatchSelectionDirectory>(), roles), query, WorkflowTodoManagementTestDependencies.CreateStarterSelectionPreparation());
        Assert.IsTrue(WorkflowCcNodeConfiguration.TryReadPolicy(Config(new { resolverKindKey = "previous_node_actors", nodeKey = "review" }), out var policy));
        var transition = new WorkflowRuntimeTransition(null, true, [new("cc", "notify.cc", [], null, policy)], null);
        var prepared = await preparation.PrepareAsync(transition, new(null, "host", "host"), Guid.CreateVersion7(), default, completingNodeKey: "review");
        Assert.IsFalse(prepared.TryGetCc("cc", out _)); Assert.AreEqual(0, roles.ReceivedCalls().Count());
    }

    [TestMethod]
    public async Task Inactive_recipient_is_not_silently_removed_from_a_mixed_set()
    {
        var first = Guid.CreateVersion7(); var revoked = Guid.CreateVersion7(); var hosts = Substitute.For<IHostUserBatchSelectionDirectory>();
        hosts.FindActiveHostUsersAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>()).Returns(new Dictionary<Guid, HostUserDirectoryEntry> { [first] = new(first, "first", "First") });
        var result = await Coordinator(hosts, Substitute.For<IWorkflowRoleMemberDirectory>()).ResolveCcAsync(new([new(WorkflowAssigneePolicy.SpecifiedUsers, [first, revoked], [], null, null)]), new(null, "host", "host"), Guid.CreateVersion7(), Context(), default);
        Assert.IsFalse(result.IsSuccess);
    }

    private static WorkflowHumanActivityAssigneeCoordinator Coordinator(IHostUserBatchSelectionDirectory hosts, IWorkflowRoleMemberDirectory roles) =>
        new(new(hosts, Substitute.For<ITenantMemberSelectionDirectory>(), roles, Substitute.For<IWorkflowOrganizationAssigneeDirectory>()));
    private static JsonElement Config(object source) => JsonSerializer.SerializeToElement(new { nextNodeKeys = new[] { "end" }, recipientPolicy = new { sources = new[] { source } } });
    private static WorkflowAssigneeRuntimeContext Context(IReadOnlyDictionary<string, JsonElement>? values = null) => new(values ?? new Dictionary<string, JsonElement>(), new Dictionary<string, IReadOnlyList<WorkflowPreviousActorRecord>>());
}
