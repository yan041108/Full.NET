using System.Text.Json;
using Full.NET.Abstractions.Ids;
using Full.NET.Data.Abstractions;
using Full.NET.Messaging.Abstractions;
using Full.NET.Modules.Workflow.Contracts;
using Full.NET.Modules.Workflow.Domain;
using Full.NET.Modules.Workflow.Features;
using Full.NET.Modules.Workflow.Features.ManageCollaborationGrants;
using Full.NET.Modules.Workflow.Persistence;
using Full.NET.Modules.Workflow.Serialization;
using NSubstitute;

namespace Full.NET.UnitTests.Workflow;

/// <summary>公开协作的发布只绑定源定义和固定表单，不能由目标参与方、旧授权或伪造摘要借用。</summary>
[TestClass]
public sealed class WorkflowCollaborationBindingTests
{
    [TestMethod]
    [DataRow(WorkflowChildStartContext.BusinessType)][DataRow(WorkflowCollaborationStartContext.BusinessType)]
    public async Task Child_or_target_roots_cannot_issue_another_collaboration_slice(string businessType)
    {
        var (grant, now) = Fixture(); var query = Substitute.For<IQueryExecutor>(); var commands = Substitute.For<ICommandExecutor>();
        var outbox = Substitute.For<IOutboxWriter>(); var instanceId = Guid.CreateVersion7();
        var instance = new WorkflowInstanceRecord(instanceId, grant.SourceTenantId, "tenant", grant.SourceTenantScopeKey, Guid.CreateVersion7(),
            grant.SourceFormVersionId, businessType, "nested", null, "active", 1, grant.SourceActorId, now, null, null, null, null, null, null);
        var writer = new WorkflowCollaborationActivationWriter(query, commands, Substitute.For<IIdGenerator>(), outbox);
        var result = await writer.ActivateAsync(instance, Scope(grant), instanceId, new("call", "end", Policy(grant), Schema()),
            new Dictionary<string, JsonElement> { ["amount"] = JsonSerializer.SerializeToElement(3) }, 1, 1, null, null, now, default, new(Guid.CreateVersion7()));
        Assert.IsFalse(result.IsSuccess); Assert.AreEqual(0, query.ReceivedCalls().Count());
        Assert.AreEqual(0, commands.ReceivedCalls().Count()); Assert.AreEqual(0, outbox.ReceivedCalls().Count());
    }
    [TestMethod]
    public void Accepted_source_binding_matches_its_fixed_form_and_summary()
    {
        var (grant, now) = Fixture(); Assert.IsTrue(Matches(grant, now));
    }

    [TestMethod]
    [DataRow("definition")][DataRow("form")][DataRow("scope")][DataRow("hash")]
    [DataRow("revoked")][DataRow("expired")][DataRow("revision")][DataRow("target_side")]
    public void Changed_binding_or_current_authority_is_rejected(string defect)
    {
        var (grant, now) = Fixture(); var policy = Policy(grant);
        if (defect == "revoked") grant = grant with { StatusKey = "revoked", Revision = 3 };
        if (defect == "hash") policy = policy with { PolicyHash = new string('f', 64) };
        if (defect == "revision") policy = policy with { GrantRevision = 3 };
        Assert.IsFalse(WorkflowCollaborationBindings.Matches(grant, policy,
            defect == "definition" ? Guid.CreateVersion7() : grant.SourceDefinitionId,
            defect == "form" ? Guid.CreateVersion7() : grant.SourceFormVersionId, Schema(),
            defect == "scope" ? "host" : defect == "target_side" ? grant.TargetTenantScopeKey : grant.SourceTenantScopeKey,
            defect == "expired" ? grant.ExpiresAtUtc : now));
    }

    [TestMethod]
    [DataRow("amount")][DataRow("decision")]
    public void Input_and_result_schema_types_must_match_the_agreed_target_summary(string field)
    {
        var (grant, now) = Fixture(); Assert.IsFalse(WorkflowCollaborationBindings.Matches(grant, Policy(grant), grant.SourceDefinitionId,
            grant.SourceFormVersionId, Schema(field), grant.SourceTenantScopeKey, now));
    }

    [TestMethod]
    public async Task Ordinary_definitions_do_not_query_collaboration_control_facts()
    {
        var query = Substitute.For<IQueryExecutor>(); var (grant, now) = Fixture();
        var draft = Draft(grant) with { Nodes = [new("start", "start", 1, JsonSerializer.SerializeToElement(new { nextNodeKeys = new[] { "end" } })), Draft(grant).Nodes.Last()] };
        Assert.IsTrue((await WorkflowCollaborationBindings.ValidateAsync(draft, grant.SourceDefinitionId, grant.SourceFormVersionId,
            Schema(), Scope(grant), query, now, default)).IsSuccess); Assert.AreEqual(0, query.ReceivedCalls().Count());
    }

    [TestMethod]
    public async Task Repeated_nodes_share_one_bounded_exact_source_query()
    {
        var (grant, now) = Fixture(); var query = Substitute.For<IQueryExecutor>();
        query.QueryAsync<WorkflowCollaborationGrantFact>(WorkflowCollaborationGrantSql.SourceBindings, Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<WorkflowCollaborationGrantFact>>([grant]));
        var node = Draft(grant).Nodes[1];
        var draft = Draft(grant) with { Nodes = Enumerable.Range(0, 96).Select(index => node with { NodeKey = "call" + index }).ToArray() };
        Assert.IsTrue((await WorkflowCollaborationBindings.ValidateAsync(draft, grant.SourceDefinitionId, grant.SourceFormVersionId,
            Schema(), Scope(grant), query, now, default)).IsSuccess);
        var args = (Dictionary<string, object?>)query.ReceivedCalls().Single().GetArguments()[1]!;
        CollectionAssert.AreEqual(new[] { grant.Id }, (Guid[])args["GrantIds"]!);
        Assert.AreEqual(grant.SourceTenantScopeKey, (string)args["TenantScopeKey"]!);
    }

    [TestMethod]
    [DataRow(false)][DataRow(true)]
    public async Task Missing_or_duplicate_control_rows_fail_closed(bool duplicate)
    {
        var (grant, now) = Fixture(); var query = Substitute.For<IQueryExecutor>();
        query.QueryAsync<WorkflowCollaborationGrantFact>(WorkflowCollaborationGrantSql.SourceBindings, Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<WorkflowCollaborationGrantFact>>(duplicate ? [grant, grant] : []));
        Assert.IsFalse((await WorkflowCollaborationBindings.ValidateAsync(Draft(grant), grant.SourceDefinitionId, grant.SourceFormVersionId,
            Schema(), Scope(grant), query, now, default)).IsSuccess);
    }

    [TestMethod]
    [DataRow("valid")][DataRow("missing")][DataRow("hash")]
    public void Public_compiler_accepts_only_closed_collaboration_configuration(string shape)
    {
        var (grant, _) = Fixture(); var draft = Draft(grant);
        if (shape != "valid") draft = draft with { Nodes = draft.Nodes.Select(node => node.NodeTypeKey == WorkflowCollaborationCallPolicy.NodeType
            ? node with { Config = shape == "hash" ? JsonSerializer.SerializeToElement(new { nextNodeKeys = new[] { "end" }, collaborationPolicy = new {
                schemaVersion = 1, grantId = grant.Id, grantRevision = 2, policyHash = "bad" } }) : JsonSerializer.SerializeToElement(new { nextNodeKeys = new[] { "end" } }) } : node).ToArray() };
        var compilation = WorkflowDefinitionCompiler.Compile(draft, Schema());
        Assert.AreEqual(shape == "valid", compilation.IsSuccess, compilation.ErrorCode);
    }

    [TestMethod]
    public void Structural_plan_does_not_authorize_execution_without_the_fixed_form()
    {
        var (grant, _) = Fixture(); var draft = Draft(grant);
        Assert.IsTrue(WorkflowDefinitionCompiler.Compile(draft).IsSuccess);
        Assert.IsTrue(WorkflowRuntimePlan.TryCreate(draft, out var plan));
        Assert.IsFalse(plan!.TryResolveStart(out _));
    }

    [TestMethod]
    public void Collaboration_source_cannot_be_rebound_as_a_child_slice()
    {
        var (grant, _) = Fixture(); var schema = Schema(); var draft = Draft(grant); var version = Guid.CreateVersion7();
        var asset = new WorkflowRuntimeAssetRecord(version, grant.SourceFormVersionId,
            JsonSerializer.Serialize(draft, WorkflowJsonSerializerContext.Default.WorkflowDefinitionDraft),
            JsonSerializer.Serialize(schema, WorkflowJsonSerializerContext.Default.WorkflowFormSchema), null, "active");
        var policy = new WorkflowSubprocessPolicy(version, new Dictionary<string, string>(), new Dictionary<string, string>(), new Dictionary<string, string>());
        Assert.IsFalse(WorkflowSubprocessBindings.TryBindAsset(asset, policy, schema, out _));
    }

    [TestMethod]
    public void Collaboration_catalog_rejects_unlisted_configuration_properties()
    {
        var (grant, _) = Fixture(); var draft = Draft(grant); var call = draft.Nodes[1];
        Assert.IsTrue(WorkflowNodeTypeCatalog.SupportsConfiguration(call));
        foreach (var key in new[] { "fieldPolicies", "targetTenantId", "targetUsers" })
        {
            var config = call.Config.EnumerateObject().ToDictionary(property => property.Name, property => property.Value.Clone());
            config[key] = JsonSerializer.SerializeToElement(new { });
            var invalid = call with { Config = JsonSerializer.SerializeToElement(config) };
            Assert.IsFalse(WorkflowNodeTypeCatalog.SupportsConfiguration(invalid));
            Assert.IsFalse(WorkflowDefinitionCompiler.Compile(draft with { Nodes = [draft.Nodes[0], invalid, draft.Nodes[2]] }).IsSuccess);
        }
    }

    private static bool Matches(WorkflowCollaborationGrantFact grant, DateTimeOffset now) => WorkflowCollaborationBindings.Matches(grant, Policy(grant),
        grant.SourceDefinitionId, grant.SourceFormVersionId, Schema(), grant.SourceTenantScopeKey, now);
    private static WorkflowCollaborationCallPolicy Policy(WorkflowCollaborationGrantFact grant) => new(grant.Id, 2, grant.PolicyHash!);
    private static WorkflowManagementScope Scope(WorkflowCollaborationGrantFact grant) => new(grant.SourceTenantId, "tenant", grant.SourceTenantScopeKey);
    private static WorkflowFormSchema Schema(string? changed = null) => new(1, 1, [new("main", [
        new("amount", changed == "amount" ? "text" : "integer", false, new Dictionary<string, JsonElement>()),
        new("decision", changed == "decision" ? "integer" : "text", false, new Dictionary<string, JsonElement>())])]);
    private static WorkflowDefinitionDraft Draft(WorkflowCollaborationGrantFact grant) => new(1, [
        new("start", "start", 1, JsonSerializer.SerializeToElement(new { nextNodeKeys = new[] { "call" } })),
        new("call", WorkflowCollaborationCallPolicy.NodeType, 1, JsonSerializer.SerializeToElement(new { nextNodeKeys = new[] { "end" },
            collaborationPolicy = new { schemaVersion = 1, grantId = grant.Id, grantRevision = 2, policyHash = grant.PolicyHash } })),
        new("end", "end", 1, JsonSerializer.SerializeToElement(new { }))], "automation");
    private static (WorkflowCollaborationGrantFact Grant, DateTimeOffset Now) Fixture()
    {
        var (grant, now) = WorkflowCollaborationGrantPolicyTests.Grant();
        var offer = JsonSerializer.Deserialize(grant.OfferJson, WorkflowJsonSerializerContext.Default.WorkflowCollaborationOfferPolicy)!;
        var accepted = JsonSerializer.Deserialize(grant.AcceptanceJson!, WorkflowJsonSerializerContext.Default.WorkflowCollaborationAcceptancePolicy)!;
        var summary = new WorkflowCollaborationTargetSummary(accepted.TargetDefinitionVersionId, "peer.review", new string('a', 64), new string('b', 64),
            new Dictionary<string, string> { ["received"] = "integer" }, new Dictionary<string, string> { ["answer"] = "text" });
        var json = JsonSerializer.Serialize(summary, WorkflowJsonSerializerContext.Default.WorkflowCollaborationTargetSummary);
        accepted = accepted with { TargetSummaryHash = WorkflowCollaborationCallPolicy.Hash(json) };
        return (grant with { AcceptanceJson = JsonSerializer.Serialize(accepted, WorkflowJsonSerializerContext.Default.WorkflowCollaborationAcceptancePolicy),
            TargetSummaryJson = json, TargetSummaryHash = accepted.TargetSummaryHash,
            PolicyHash = WorkflowCollaborationGrantPolicy.Hash(grant.Id, grant.SourceTenantId, grant.TargetTenantId, grant.SourceActorId, grant.TargetActorId!.Value, offer, accepted) }, now);
    }
}
