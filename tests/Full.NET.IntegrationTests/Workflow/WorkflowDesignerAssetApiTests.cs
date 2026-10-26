using System.Net;
using Dapper;
using Microsoft.Data.SqlClient;
using MySqlConnector;
using Full.NET.IntegrationTests.Migrations;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.Workflow.Contracts;
using Full.NET.Modules.Workflow.Domain;
namespace Full.NET.IntegrationTests.Workflow;

/// <summary>只验证完整设计稿的真实 API/双库往返、并发、权限及发布原子性。</summary>
[TestClass]
public sealed class WorkflowDesignerAssetApiTests
{
    [TestMethod]
    public async Task Full_design_asset_roundtrips_with_sql_server() => await VerifyAsync(DatabaseProvider.SqlServer, await SharedDatabaseFixture.CreateSqlServerDatabaseAsync());
    [TestMethod]
    public async Task Full_design_asset_roundtrips_with_mysql() => await VerifyAsync(DatabaseProvider.MySql, await SharedDatabaseFixture.CreateMySqlDatabaseAsync());

    private static async Task VerifyAsync(DatabaseProvider provider, string connectionString)
    {
        using var factory = new FullNetApiFactory(provider, connectionString);
        await factory.InitializeAsync(useSchemaTemplate: provider != DatabaseProvider.SqlServer);
        // 当前测试独占新数据库：模拟第一张资产表成功、第二张快照表尚未创建且 DbUp 未记账。
        if (provider == DatabaseProvider.SqlServer)
        {
            await using var connection = new SqlConnection(connectionString);
            await connection.ExecuteAsync("DROP TABLE dbo.fn_workflow_form_design_snapshot; DELETE FROM dbo.SchemaVersions WHERE ScriptName LIKE '%286_WorkflowDesignerAsset.sql';");
            var recovery = Migration114To126RecoveryTestSupport.MigrateSqlServer(connectionString, 286);
            Assert.IsTrue(recovery.Successful, recovery.Error?.Message);
        }
        else
        {
            await using var connection = new MySqlConnection(Migration114To126RecoveryTestSupport.MySqlQueryConnectionString(connectionString));
            await connection.ExecuteAsync("DROP TABLE fn_workflow_form_design_snapshot; DELETE FROM schemaversions WHERE ScriptName LIKE '%286_WorkflowDesignerAsset.sql';");
            var recovery = Migration114To126RecoveryTestSupport.MigrateMySql(connectionString, 286);
            Assert.IsTrue(recovery.Successful, recovery.Error?.Message);
        }
        using var client = factory.CreateClientForHost("localhost");
        var identity = await factory.CreateHostIdentityAsync("asset-" + Guid.NewGuid().ToString("N"), [WorkflowPermissions.FormsRead, WorkflowPermissions.FormsCreate, WorkflowPermissions.FormsUpdate, WorkflowPermissions.FormsPublish,
            WorkflowPermissions.DefinitionsRead, WorkflowPermissions.DefinitionsCreate, WorkflowPermissions.DefinitionsPublish, WorkflowPermissions.InstancesStart, WorkflowPermissions.InstancesRead, WorkflowPermissions.TodosRead, WorkflowPermissions.TodosApprove]);
        var token = identity.AccessToken;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var created = await client.PostAsJsonAsync("/api/v1/workflow/forms", new { formKey = "asset." + Guid.NewGuid().ToString("N"), draft = new { schemaVersion = 1, adapterVersion = 1, sections = Array.Empty<object>() } });
        Assert.AreEqual(HttpStatusCode.Created, created.StatusCode, await created.Content.ReadAsStringAsync());
        using var form = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        var id = form.RootElement.GetProperty("id").GetGuid();
        var path = $"/api/v1/workflow/forms/{id:D}/design-asset";
        using var designDocument = JsonDocument.Parse("""{"widgetList":[{"id":"grid","type":"grid","category":"container","options":{"name":"grid","gutter":20},"cols":[{"id":"col","type":"grid-col","category":"container","options":{"name":"col","span":24},"widgetList":[{"id":"title","type":"input","formItemFlag":true,"options":{"name":"title","label":"申请标题","placeholder":"请输入标题","defaultValue":"默认值","required":true,"extra":{"keep":[1,true,null]}}}]}]}],"formConfig":{"jsonVersion":3,"labelWidth":180,"labelPosition":"top","size":"large","cssCode":".a { color:red; }","onFormMounted":"console.log('preview')"}}""");
        var json = designDocument.RootElement;
        object Body(long revision, JsonElement value) => new { expectedRevision = revision, sourceCommit = "c67479e496bab56a93a3dff168a4f529d8293c67", formatVersion = 3, designerJson = value };
        using var save = await client.PutAsJsonAsync(path, Body(1, json));
        Assert.AreEqual(HttpStatusCode.OK, save.StatusCode, await save.Content.ReadAsStringAsync());
        using var saved = JsonDocument.Parse(await save.Content.ReadAsStringAsync());
        Assert.IsTrue(JsonElement.DeepEquals(json, saved.RootElement.GetProperty("designerJson")));
        using var read = await client.GetAsync(path);
        using var reloaded = JsonDocument.Parse(await read.Content.ReadAsStringAsync());
        Assert.IsTrue(JsonElement.DeepEquals(json, reloaded.RootElement.GetProperty("designerJson")));
        Assert.AreEqual(2L, reloaded.RootElement.GetProperty("draftRevision").GetInt64());
        using var conflict = await client.PutAsJsonAsync(path, Body(1, json));
        Assert.AreEqual(HttpStatusCode.Conflict, conflict.StatusCode);
        object Publish(long revision, string[]? targets = null, string? hash = null) => new { expectedRevision = revision, expectedBindingRevision = revision, targets = targets ?? ["web"], rendererHash = hash ?? WorkflowDesignerRelease.ContentHash };
        using var wrongTarget = await client.PostAsJsonAsync($"/api/v1/workflow/forms/{id:D}/publish", Publish(2, ["uniapp"]));
        Assert.AreEqual(HttpStatusCode.BadRequest, wrongTarget.StatusCode);
        using var wrongRuntime = await client.PostAsJsonAsync($"/api/v1/workflow/forms/{id:D}/publish", Publish(2, hash: new string('0', 64)));
        Assert.AreEqual(HttpStatusCode.BadRequest, wrongRuntime.StatusCode);
        using var staleBinding = await client.PostAsJsonAsync($"/api/v1/workflow/forms/{id:D}/publish", new { expectedRevision = 2, expectedBindingRevision = 1, targets = new[] { "web" }, rendererHash = WorkflowDesignerRelease.ContentHash });
        Assert.AreEqual(HttpStatusCode.Conflict, staleBinding.StatusCode);
        using var published = await client.PostAsJsonAsync($"/api/v1/workflow/forms/{id:D}/publish", Publish(2));
        Assert.AreEqual(HttpStatusCode.OK, published.StatusCode, await published.Content.ReadAsStringAsync());
        using var version = JsonDocument.Parse(await published.Content.ReadAsStringAsync());
        using var web = JsonDocument.Parse(version.RootElement.GetProperty("webRenderSchemaJson").GetString()!);
        Assert.IsTrue(JsonElement.DeepEquals(json, web.RootElement));
        using var business = JsonDocument.Parse(version.RootElement.GetProperty("formSchemaJson").GetString()!);
        Assert.AreEqual("title", business.RootElement.GetProperty("sections")[0].GetProperty("fields")[0].GetProperty("fieldKey").GetString());
        await VerifyRuntimeAsync(client, identity.UserId, version.RootElement.GetProperty("id").GetGuid(), provider, connectionString);
        using var invalid = JsonDocument.Parse("""{"widgetList":[{"id":"unknown","type":"future-input","options":{"name":"unknown"}}],"formConfig":{"jsonVersion":3,"keep":true}}""");
        using var saveUnknown = await client.PutAsJsonAsync(path, Body(3, invalid.RootElement));
        Assert.AreEqual(HttpStatusCode.OK, saveUnknown.StatusCode);
        using var rejected = await client.PostAsJsonAsync($"/api/v1/workflow/forms/{id:D}/publish", Publish(4));
        Assert.AreEqual(HttpStatusCode.BadRequest, rejected.StatusCode);
        using var after = await client.GetAsync(path); using var afterJson = JsonDocument.Parse(await after.Content.ReadAsStringAsync());
        Assert.IsTrue(JsonElement.DeepEquals(invalid.RootElement, afterJson.RootElement.GetProperty("designerJson")));
        using var state = await client.GetAsync($"/api/v1/workflow/forms/{id:D}"); using var stateJson = JsonDocument.Parse(await state.Content.ReadAsStringAsync());
        Assert.AreEqual(version.RootElement.GetProperty("id").GetGuid(), stateJson.RootElement.GetProperty("latestPublishedVersionId").GetGuid());
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await factory.CreateHostAccessTokenAsync([WorkflowPermissions.FormsRead]));
        using var forbidden = await client.PutAsJsonAsync(path, Body(4, json));
        Assert.AreEqual(HttpStatusCode.Forbidden, forbidden.StatusCode);
        client.DefaultRequestHeaders.Authorization = null;
        using var anonymous = await client.GetAsync(path);
        Assert.AreEqual(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        await VerifyTenantBoundaryAsync(factory, path, Body(4, json));
    }

    private static async Task VerifyTenantBoundaryAsync(FullNetApiFactory factory, string assetPath, object body)
    {
        using var client = factory.CreateClientForHost("localhost");
        client.DefaultRequestHeaders.Add("Origin", "http://localhost");
        var token = await IntegrationTestAuthHelper.LoginAsHostUserAsync(client, "admin");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var available = await client.GetAsync("/api/v1/tenancy/available");
        Assert.AreEqual(HttpStatusCode.OK, available.StatusCode, await available.Content.ReadAsStringAsync());
        using var tenants = JsonDocument.Parse(await available.Content.ReadAsStringAsync());
        var acme = tenants.RootElement.EnumerateArray().Single(item => item.GetProperty("identifier").GetString() == "acme");
        using var entered = await client.PutAsJsonAsync("/api/v1/tenancy/context", new { tenantId = acme.GetProperty("id").GetGuid() });
        Assert.AreEqual(HttpStatusCode.OK, entered.StatusCode, await entered.Content.ReadAsStringAsync());
        using var context = JsonDocument.Parse(await entered.Content.ReadAsStringAsync());
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", context.RootElement.GetProperty("accessToken").GetString());
        using var read = await client.GetAsync(assetPath); Assert.AreEqual(HttpStatusCode.NotFound, read.StatusCode);
        using var save = await client.PutAsJsonAsync(assetPath, body); Assert.AreEqual(HttpStatusCode.NotFound, save.StatusCode, await save.Content.ReadAsStringAsync());
    }

    private static async Task VerifyRuntimeAsync(HttpClient client, Guid actorId, Guid formVersionId, DatabaseProvider provider, string connectionString)
    {
        using var created = await client.PostAsJsonAsync("/api/v1/workflow/definitions", new { definitionKey = "vform." + Guid.NewGuid().ToString("N"), draft = new { schemaVersion = 1, nodes = new object[] {
            new { nodeKey = "start", nodeTypeKey = "start", nodeSchemaVersion = 1, config = new { nextNodeKeys = new[] { "approve" } } },
            new { nodeKey = "approve", nodeTypeKey = "human.approval", nodeSchemaVersion = 1, config = new { nextNodeKeys = new[] { "end" }, fieldPolicies = new { title = "required" } } },
            new { nodeKey = "end", nodeTypeKey = "end", nodeSchemaVersion = 1, config = new { nextNodeKeys = Array.Empty<string>() } }
        } } });
        Assert.AreEqual(HttpStatusCode.Created, created.StatusCode, await created.Content.ReadAsStringAsync());
        using var definition = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        using var published = await client.PostAsJsonAsync($"/api/v1/workflow/definitions/{definition.RootElement.GetProperty("id").GetGuid():D}/publish", new { expectedRevision = 1, formVersionId });
        Assert.AreEqual(HttpStatusCode.OK, published.StatusCode, await published.Content.ReadAsStringAsync());
        using var definitionVersion = JsonDocument.Parse(await published.Content.ReadAsStringAsync());
        using var started = await client.PostAsJsonAsync("/api/v1/workflow/instances", new { definitionVersionId = definitionVersion.RootElement.GetProperty("id").GetGuid(), businessType = "vform.test", businessId = Guid.NewGuid().ToString("N"), initialValues = new { title = "initial" }, idempotencyKey = Guid.NewGuid().ToString("N") });
        Assert.AreEqual(HttpStatusCode.Created, started.StatusCode, await started.Content.ReadAsStringAsync());
        using var instance = JsonDocument.Parse(await started.Content.ReadAsStringAsync());
        var todoId = instance.RootElement.GetProperty("activeTodoId").GetGuid();
        var runtimePath = $"/api/v1/workflow/todos/{todoId:D}/runtime";
        using var runtime = await client.GetAsync(runtimePath);
        Assert.AreEqual(HttpStatusCode.OK, runtime.StatusCode, await runtime.Content.ReadAsStringAsync());
        using var view = JsonDocument.Parse(await runtime.Content.ReadAsStringAsync());
        Assert.AreEqual(actorId, view.RootElement.GetProperty("assigneeUserId").GetGuid());
        Assert.AreEqual(WorkflowDesignerRelease.ContentHash, view.RootElement.GetProperty("webRenderJson").GetProperty("fullNetRuntime").GetProperty("rendererHash").GetString());
        Assert.AreEqual("title", view.RootElement.GetProperty("webRenderJson").GetProperty("fullNetRuntime").GetProperty("bindings")[0].GetProperty("fieldKey").GetString());
        object Approve(object patch) => new { expectedRevision = 1, expectedFormRevision = view.RootElement.GetProperty("submissionRevision").GetInt64(), fieldPatch = patch, idempotencyKey = Guid.NewGuid().ToString("N") };
        using var unknown = await client.PostAsJsonAsync($"/api/v1/workflow/todos/{todoId:D}/approve", Approve(new { unbound = "blocked" }));
        Assert.IsFalse(unknown.IsSuccessStatusCode);

        // 仅污染本测试专属版本，模拟部署运行产物不匹配；之后恢复原始快照，不触碰共享数据。
        await using System.Data.Common.DbConnection connection = provider == DatabaseProvider.SqlServer ? new SqlConnection(connectionString) : new MySqlConnection(Migration114To126RecoveryTestSupport.MySqlQueryConnectionString(connectionString));
        var predicate = provider == DatabaseProvider.SqlServer ? "@id" : "UNHEX(REPLACE(@id, '-', ''))";
        var original = await connection.QuerySingleAsync<string>($"SELECT BindingJson FROM fn_workflow_form_design_snapshot WHERE FormVersionId = {predicate}", new { id = formVersionId.ToString("D") });
        Assert.IsTrue(WorkflowDesignerRelease.Matches(original));
        await connection.ExecuteAsync($"UPDATE fn_workflow_form_design_snapshot SET BindingJson = @json WHERE FormVersionId = {predicate}", new { id = formVersionId.ToString("D"), json = "{}" });
        using var blockedRead = await client.GetAsync(runtimePath); Assert.AreEqual(HttpStatusCode.Conflict, blockedRead.StatusCode);
        using var blockedWrite = await client.PostAsJsonAsync($"/api/v1/workflow/todos/{todoId:D}/approve", Approve(new { title = "blocked" })); Assert.AreEqual(HttpStatusCode.Conflict, blockedWrite.StatusCode);
        await connection.ExecuteAsync($"UPDATE fn_workflow_form_design_snapshot SET BindingJson = @json WHERE FormVersionId = {predicate}", new { id = formVersionId.ToString("D"), json = original });
        using var approved = await client.PostAsJsonAsync($"/api/v1/workflow/todos/{todoId:D}/approve", Approve(new { title = "actual submitted value" }));
        Assert.AreEqual(HttpStatusCode.OK, approved.StatusCode, await approved.Content.ReadAsStringAsync());
        using var completed = JsonDocument.Parse(await approved.Content.ReadAsStringAsync());
        Assert.AreEqual("completed", completed.RootElement.GetProperty("statusKey").GetString());
    }
}
