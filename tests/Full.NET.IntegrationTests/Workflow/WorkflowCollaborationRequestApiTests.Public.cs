using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Dapper;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Workflow.Contracts;
using Full.NET.Modules.Workflow.Domain;

namespace Full.NET.IntegrationTests.Workflow;

public sealed partial class WorkflowCollaborationRequestApiTests
{
    [TestMethod]
    public async Task Collaboration_public_node_with_sql_server() => await VerifyAsync(DatabaseProvider.SqlServer,
        await SharedDatabaseFixture.CreateSqlServerDatabaseAsync(), withWorker: true, withFiles: true, treeShape: WorkerTreeShape.Parallel, publicSource: true);
    [TestMethod]
    public async Task Collaboration_public_node_with_mysql() => await VerifyAsync(DatabaseProvider.MySql,
        await SharedDatabaseFixture.CreateMySqlDatabaseAsync(), withWorker: true, withFiles: true, treeShape: WorkerTreeShape.Parallel, publicSource: true);

    [TestMethod]
    public async Task Collaboration_public_node_deep_with_sql_server() => await VerifyAsync(DatabaseProvider.SqlServer,
        await SharedDatabaseFixture.CreateSqlServerDatabaseAsync(), withWorker: true, withFiles: true, treeShape: WorkerTreeShape.Deep, publicSource: true);
    [TestMethod]
    public async Task Collaboration_public_node_deep_with_mysql() => await VerifyAsync(DatabaseProvider.MySql,
        await SharedDatabaseFixture.CreateMySqlDatabaseAsync(), withWorker: true, withFiles: true, treeShape: WorkerTreeShape.Deep, publicSource: true);

    /// <summary>公开能力必须经实际保存/发布，不能把内部IR注入结果当作公共节点交付。</summary>
    private static async Task<Asset> PublishPublicSourceAsync(HttpClient source, HttpClient target, Guid? sourceTenant, Asset targetAsset,
        DbConnection db, Asset asset, WorkflowDefinitionDraft draft, bool negatives)
    {
        var catalog = await source.GetFromJsonAsync<JsonElement>("/api/v1/workflow/definitions/node-type-catalog");
        Assert.AreEqual(30, catalog.GetProperty("catalogVersion").GetInt32());
        var capability = catalog.GetProperty("nodeTypes").EnumerateArray().Single(node => node.GetProperty("nodeTypeKey").GetString() == WorkflowCollaborationCallPolicy.NodeType);
        Assert.IsTrue(capability.GetProperty("designable").GetBoolean() && capability.GetProperty("publishable").GetBoolean() && capability.GetProperty("executable").GetBoolean());
        Assert.IsFalse(capability.GetProperty("supportsFieldPolicies").GetBoolean());
        async Task<long> Save(Asset current, WorkflowDefinitionDraft model)
        {
            var detail = await source.GetFromJsonAsync<JsonElement>($"/api/v1/workflow/definitions/{current.Definition:D}");
            using var saved = await source.PutAsJsonAsync($"/api/v1/workflow/definitions/{current.Definition:D}/draft",
                new { expectedRevision = detail.GetProperty("draftRevision").GetInt64(), draft = model });
            Assert.AreEqual(HttpStatusCode.OK, saved.StatusCode, await saved.Content.ReadAsStringAsync());
            return (await source.GetFromJsonAsync<JsonElement>($"/api/v1/workflow/definitions/{current.Definition:D}")).GetProperty("draftRevision").GetInt64();
        }
        async Task Deny(Asset current, long revision, Guid form)
        {
            var before = await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_definition_version WHERE DefinitionId=@id", new { id = current.Definition });
            var problem = await PostAsync(source, $"/api/v1/workflow/definitions/{current.Definition:D}/publish",
                new { expectedRevision = revision, formVersionId = form }, HttpStatusCode.BadRequest);
            Assert.AreEqual(WorkflowErrorCodes.CollaborationGrantInvalid, problem.GetProperty("code").GetString());
            Assert.AreEqual(before, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_definition_version WHERE DefinitionId=@id", new { id = current.Definition }));
        }
        var revision = await Save(asset, draft);
        if (negatives)
        {
            var other = await AssetAsync(source, files: true);
            await Deny(asset, revision, other.Form);
            var otherRevision = await Save(other, draft); await Deny(other, otherRevision, asset.Form);
            Assert.IsTrue(WorkflowCollaborationCallPolicy.TryRead(draft.Nodes.Single(node => node.NodeTypeKey == WorkflowCollaborationCallPolicy.NodeType).Config, out var policy));
            var altered = draft with { Nodes = draft.Nodes.Select(node => node.NodeTypeKey == WorkflowCollaborationCallPolicy.NodeType
                ? node with { Config = JsonSerializer.SerializeToElement(new { nextNodeKeys = new[] { draft.Nodes.Last().NodeKey }, collaborationPolicy = new {
                    schemaVersion = 1, grantId = policy!.GrantId, grantRevision = 2, policyHash = policy.PolicyHash == new string('a', 64) ? new string('b', 64) : new string('a', 64) } }) } : node).ToArray() };
            revision = await Save(asset, altered); await Deny(asset, revision, asset.Form);
            revision = await Save(asset, draft);
        }
        var published = await PostAsync(source, $"/api/v1/workflow/definitions/{asset.Definition:D}/publish", new { expectedRevision = revision, formVersionId = asset.Form });
        asset = asset with { Version = published.GetProperty("id").GetGuid() };
        if (negatives)
        {
            const string path = "/api/v1/workflow/collaboration-grants";
            Assert.IsTrue(WorkflowCollaborationCallPolicy.TryRead(draft.Nodes[1].Config, out var policy));
            var original = await source.GetFromJsonAsync<JsonElement>($"{path}/{policy!.GrantId:D}");
            var sourceActor = original.GetProperty("sourceActorId").GetGuid();
            var reciprocal = await PostAsync(target, path, new { targetTenantId = sourceTenant, targetScopeKey = sourceTenant is null ? "host" : "tenant",
                idempotencyKey = Guid.CreateVersion7().ToString("N"), offer = new { schemaVersion = 1, sourceDefinitionId = targetAsset.Definition,
                    sourceFormVersionId = targetAsset.Form, targetWorkflowKey = asset.Key, inputFieldKeys = new[] { "title" }, outputFieldKeys = new[] { "title" },
                    maximumTargetActivations = 64, expiresAtUtc = DateTimeOffset.UtcNow.AddHours(1) } });
            await PostAsync(source, $"{path}/{reciprocal.GetProperty("id").GetGuid():D}/accept", new { expectedRevision = 1, idempotencyKey = "nested-denied",
                acceptance = new { schemaVersion = 1, targetDefinitionVersionId = asset.Version, starterUserId = sourceActor, maximumActivities = 32,
                    allowedUserIds = new[] { sourceActor }, inputFields = new Dictionary<string, string> { ["title"] = "title" },
                    outputFields = new Dictionary<string, string> { ["title"] = "title" } } }, HttpStatusCode.BadRequest);
            var nested = await PostAsync(source, "/api/v1/workflow/definitions", new { definitionKey = "nested.collab." + Guid.CreateVersion7().ToString("N"),
                draft = new { schemaVersion = 1, workflowKindKey = "automation", nodes = new object[] {
                    new { nodeKey = "start", nodeTypeKey = "start", nodeSchemaVersion = 1, config = new { nextNodeKeys = new[] { "child" } } },
                    new { nodeKey = "child", nodeTypeKey = "process.subprocess", nodeSchemaVersion = 1, config = new { nextNodeKeys = new[] { "end" },
                        fieldPolicies = new { title = "editable" }, subprocessPolicy = new { definitionVersionId = asset.Version,
                            inputFields = new { title = "title" }, outputFields = new { title = "title" } } } },
                    new { nodeKey = "end", nodeTypeKey = "end", nodeSchemaVersion = 1, config = new { } } } } }, HttpStatusCode.Created);
            var nestedId = nested.GetProperty("id").GetGuid();
            var nestedProblem = await PostAsync(source, $"/api/v1/workflow/definitions/{nestedId:D}/publish", new { expectedRevision = 1, formVersionId = asset.Form }, HttpStatusCode.BadRequest);
            Assert.AreEqual(0, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_definition_version WHERE DefinitionId=@nestedId", new { nestedId }));
            Assert.AreEqual(WorkflowErrorCodes.DefinitionSubprocessInvalid, nestedProblem.GetProperty("code").GetString());
        }
        return asset;
    }
}
