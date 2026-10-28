using System.Text.Json;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Organization.Contracts;
using Full.NET.Modules.Workflow.Contracts;
using Full.NET.Modules.Workflow.Domain;
using Full.NET.Modules.Workflow.Persistence;
using NSubstitute;

namespace Full.NET.UnitTests.Workflow;

/// <summary>动态来源绑定发布字段、先行活动和真实身份，不能由请求构造环境身份或预判当前办理完成。</summary>
[TestClass]
public sealed class WorkflowDynamicAssigneeTests
{
    [TestMethod]
    public void Dynamic_source_configuration_is_closed()
    {
        foreach (var source in new object[] { new { resolverKindKey = "form_users", fieldKey = "users" }, new { resolverKindKey = "form_units", fieldKey = "units" },
            new { resolverKindKey = "previous_node_actors", nodeKey = "first" } })
            Assert.IsTrue(WorkflowAssigneePolicy.TryReadPolicy(JsonSerializer.SerializeToElement(new { sources = new[] { source } }), out _));
        foreach (var source in new object[] { new { resolverKindKey = "form_users", fieldKey = "users", actorUserId = Guid.CreateVersion7() },
            new { resolverKindKey = "previous_node_actors", nodeKey = "first", userIds = new[] { Guid.CreateVersion7() } }, new { resolverKindKey = "form_units", fieldKey = "" } })
            Assert.IsFalse(WorkflowAssigneePolicy.TryReadPolicy(JsonSerializer.SerializeToElement(new { sources = new[] { source } }), out _));
    }
    [TestMethod]
    public void Previous_node_must_be_a_dominating_human_activity()
    {
        Assert.IsTrue(WorkflowDefinitionCompiler.Compile(Graph(new { resolverKindKey = "previous_node_actors", nodeKey = "first" })).IsSuccess);
        foreach (var key in new[] { "second", "end", "missing" })
            Assert.IsFalse(WorkflowDefinitionCompiler.Compile(Graph(new { resolverKindKey = "previous_node_actors", nodeKey = key })).IsSuccess);
        var graph = Graph(new { resolverKindKey = "previous_node_actors", nodeKey = "first" });
        var edges = new Dictionary<string, string[]> { ["start"] = ["first", "second"], ["first"] = ["second"], ["second"] = ["end"], ["end"] = [] };
        Assert.IsFalse(WorkflowDynamicAssigneeDefinitionRules.ValidateGraph(graph, edges, "start"));
    }
    [TestMethod]
    public void Form_sources_require_published_reference_field_types()
    {
        var graph = Graph(new { resolverKindKey = "form_users", fieldKey = "users" });
        var options = new Dictionary<string, JsonElement> { ["options"] = JsonSerializer.SerializeToElement(new[] { Guid.CreateVersion7().ToString("D") }) };
        Assert.IsTrue(WorkflowDefinitionCompiler.Compile(graph, new(1, 1, [new("main", [new("users", "select", false, options)])])).IsSuccess);
        Assert.IsFalse(WorkflowDefinitionCompiler.Compile(graph, new(1, 1, [new("main", [new("users", "text", false, new Dictionary<string, JsonElement>())])])).IsSuccess);
        Assert.IsFalse(WorkflowDefinitionCompiler.Compile(graph, new(1, 1, [new("main", [new("other", "select", false, options)])])).IsSuccess);
    }
    [TestMethod]
    public async Task Form_users_are_revalidated_in_the_current_identity_scope()
    {
        var actor = Guid.CreateVersion7(); var hosts = Substitute.For<IHostUserBatchSelectionDirectory>();
        hosts.FindActiveHostUsersAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, HostUserDirectoryEntry> { [actor] = new(actor, "actor", "Actor") });
        var resolver = new WorkflowAssigneeResolver(hosts, Substitute.For<ITenantMemberSelectionDirectory>(), Substitute.For<IWorkflowRoleMemberDirectory>(), Substitute.For<IWorkflowOrganizationAssigneeDirectory>());
        var policy = new WorkflowAssigneePolicy([new(WorkflowAssigneePolicy.FormUsers, [], [], null, null, FieldKey: "users")]);
        var context = new WorkflowAssigneeRuntimeContext(new Dictionary<string, JsonElement> { ["users"] = JsonSerializer.SerializeToElement(actor) }, new Dictionary<string, IReadOnlyList<WorkflowPreviousActorRecord>>());
        var result = await resolver.ResolveAsync(policy, [], new(null, "host", "host"), Guid.CreateVersion7(), context: context);
        Assert.IsTrue(result.IsSuccess); CollectionAssert.AreEqual(new[] { actor }, result.Value!.ToArray());
        hosts.FindActiveHostUsersAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>()).Returns(new Dictionary<Guid, HostUserDirectoryEntry>());
        Assert.IsFalse((await resolver.ResolveAsync(policy, [], new(null, "host", "host"), Guid.CreateVersion7(), context: context)).IsSuccess);
        Assert.IsFalse((await resolver.ResolveAsync(policy, [], new(null, "host", "host"), Guid.CreateVersion7())).IsSuccess);
    }
    [TestMethod]
    public async Task Current_activity_reference_defers_until_a_durable_continuation_observes_real_participants()
    {
        var actor = Guid.CreateVersion7(); var step = Guid.CreateVersion7(); var query = Substitute.For<IQueryExecutor>();
        query.QueryAsync<WorkflowPreviousActorRecord>(WorkflowDynamicAssigneeSql.ListPreviousActors, Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(new[] { new WorkflowPreviousActorRecord("first", step, actor) });
        var hosts = Substitute.For<IHostUserBatchSelectionDirectory>();
        hosts.FindActiveHostUsersAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, HostUserDirectoryEntry> { [actor] = new(actor, "actor", "Actor") });
        var resolver = new WorkflowAssigneeResolver(hosts, Substitute.For<ITenantMemberSelectionDirectory>(), Substitute.For<IWorkflowRoleMemberDirectory>(), Substitute.For<IWorkflowOrganizationAssigneeDirectory>());
        var preparation = new WorkflowHumanActivityPreparation(new(resolver), query, WorkflowTodoManagementTestDependencies.CreateStarterSelectionPreparation());
        Assert.IsTrue(WorkflowRuntimePlan.TryCreate(Graph(new { resolverKindKey = "previous_node_actors", nodeKey = "first" }), out var plan));
        Assert.IsTrue(plan!.TryResolveAfterHuman("first", out var next));
        var deferred = await preparation.PrepareAsync(next, new(null, "host", "host"), actor, default, instanceId: Guid.CreateVersion7(), completingNodeKey: "first");
        Assert.IsFalse(deferred.TryGet("second", out _)); Assert.AreEqual(0, hosts.ReceivedCalls().Count());
        var unbound = await preparation.PrepareAsync(next, new(null, "host", "host"), actor, default, instanceId: Guid.CreateVersion7());
        Assert.IsTrue(unbound.TryGet("second",out var missing)); Assert.IsFalse(missing.IsSuccess);
        Assert.AreEqual(0,query.ReceivedCalls().Count());
        var epoch=Guid.CreateVersion7();
        var resumed = await preparation.PrepareAsync(next, new(null, "host", "host"), actor, default, instanceId: Guid.CreateVersion7(), executionEpochId: epoch);
        Assert.IsTrue(resumed.TryGet("second", out var snapshot)); Assert.IsTrue(snapshot.IsSuccess);
        CollectionAssert.AreEqual(new[] { actor }, snapshot.Value!.Task!.MemberUserIds.ToArray());
        Assert.IsTrue(query.ReceivedCalls().Any(call=>call.GetArguments()[1] is IReadOnlyDictionary<string,object?> values && Equals(values["ExecutionEpochId"],epoch)));
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task Form_department_claims_require_current_membership_in_the_frozen_department(bool stillMember)
    {
        var actor = Guid.CreateVersion7(); var unit = Guid.CreateVersion7(); var tenantUsers = Substitute.For<ITenantMemberSelectionDirectory>();
        tenantUsers.FindActiveTenantMembersAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, TenantUserDirectoryEntry> { [actor] = new(actor, "actor", "Actor") });
        var organizations = Substitute.For<IWorkflowOrganizationAssigneeDirectory>();
        organizations.FindActiveUnitMemberUserIdsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), 21, Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, IReadOnlyList<Guid>> { [unit] = new[] { actor } });
        organizations.IsActiveMemberOfAnyUnitAsync(actor, Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>()).Returns(stillMember);
        var roles = Substitute.For<IWorkflowRoleMemberDirectory>();
        var resolver = new WorkflowAssigneeResolver(Substitute.For<IHostUserBatchSelectionDirectory>(), tenantUsers, roles, organizations);
        var policy = new WorkflowAssigneePolicy([new(WorkflowAssigneePolicy.FormUnits, [], [], null, null, FieldKey: "units")]);
        var context = new WorkflowAssigneeRuntimeContext(new Dictionary<string, JsonElement> { ["units"] = JsonSerializer.SerializeToElement(unit) },
            new Dictionary<string, IReadOnlyList<WorkflowPreviousActorRecord>>());
        var scope = new Full.NET.Modules.Workflow.Features.WorkflowManagementScope(Guid.CreateVersion7(), "tenant", "tenant:test");
        var capture = new WorkflowAssigneeResolutionCapture();
        var result = await resolver.ResolveAsync(policy, [], scope, actor, capture: capture, context: context);
        Assert.IsTrue(result.IsSuccess);
        var frozen = capture.Create(actor, result.Value!);
        Assert.AreEqual(unit, frozen.Sources[0].Memberships[0].SourceId);
        var authorizer = new WorkflowClaimCandidateAuthorizer(resolver, roles, organizations, Substitute.For<IDataTransactionState>());
        Assert.AreEqual(stillMember, await authorizer.IsCurrentCandidateAsync(policy, scope, actor, actor, default, frozen));
    }

    [TestMethod]
    [DataRow("form_users", true)]
    [DataRow("form_users", false)]
    [DataRow("previous_node_actors", true)]
    [DataRow("previous_node_actors", false)]
    public async Task Dynamic_claims_never_infer_original_members_without_a_frozen_source(string kind, bool hasSnapshot)
    {
        var actor = Guid.CreateVersion7(); var hosts = Substitute.For<IHostUserBatchSelectionDirectory>();
        hosts.FindActiveHostUsersAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, HostUserDirectoryEntry> { [actor] = new(actor, "actor", "Actor") });
        var roles = Substitute.For<IWorkflowRoleMemberDirectory>(); var organizations = Substitute.For<IWorkflowOrganizationAssigneeDirectory>();
        var resolver = new WorkflowAssigneeResolver(hosts, Substitute.For<ITenantMemberSelectionDirectory>(), roles, organizations);
        var source = new WorkflowAssigneeSource(kind, [], [], null, null, FieldKey: kind == "form_users" ? "users" : null,
            PreviousNodeKey: kind == "previous_node_actors" ? "first" : null);
        var authorizer = new WorkflowClaimCandidateAuthorizer(resolver, roles, organizations, Substitute.For<IDataTransactionState>());
        var snapshot = hasSnapshot ? new WorkflowAssigneeResolutionSnapshot(1, actor, [actor], [new("assignee_policy", 0, source, [actor], [])]) : null;
        Assert.AreEqual(hasSnapshot, await authorizer.IsCurrentCandidateAsync(new([source]), new(null, "host", "host"), actor, actor, default, snapshot));
    }
    private static WorkflowDefinitionDraft Graph(object source) => new(1,
    [new("start", "start", 1, JsonSerializer.SerializeToElement(new { nextNodeKeys = new[] { "first" } })),
     new("first", "human.task", 1, JsonSerializer.SerializeToElement(new { nextNodeKeys = new[] { "second" }, taskPolicy = new { completionModeKey = "all" } })),
     new("second", "human.task", 1, JsonSerializer.SerializeToElement(new { nextNodeKeys = new[] { "end" }, taskPolicy = new { completionModeKey = "all" }, assigneePolicy = new { sources = new[] { source } } })),
     new("end", "end", 1, JsonSerializer.SerializeToElement(new { }))], "automation");
}
