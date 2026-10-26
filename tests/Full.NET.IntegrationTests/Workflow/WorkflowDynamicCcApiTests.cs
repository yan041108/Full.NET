using System.Data.Common;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Dapper;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.IntegrationTests.Migrations;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Tenancy.Contracts;
using Full.NET.Modules.Workflow.Contracts;
using Full.NET.Modules.Workflow.Execution;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using MySqlConnector;

namespace Full.NET.IntegrationTests.Workflow;

/// <summary>真实双库验证抄送在激活时取受信来源，不等待已读、不预判转办后的实际处理人。</summary>
[TestClass]
public sealed class WorkflowDynamicCcApiTests
{
    [TestMethod]
    public async Task Dynamic_cc_with_sql_server() => await VerifyAsync(DatabaseProvider.SqlServer, await SharedDatabaseFixture.CreateSqlServerDatabaseAsync());
    [TestMethod]
    public async Task Dynamic_cc_with_mysql() => await VerifyAsync(DatabaseProvider.MySql, await SharedDatabaseFixture.CreateMySqlDatabaseAsync());

    private static async Task VerifyAsync(DatabaseProvider provider, string connection)
    {
        using var factory = new FullNetApiFactory(provider, connection); await factory.InitializeAsync(useSchemaTemplate: false);
        await using DbConnection db = provider == DatabaseProvider.SqlServer ? new SqlConnection(connection) : new MySqlConnection(Migration114To126RecoveryTestSupport.MySqlQueryConnectionString(connection));
        var owner = await factory.CreateHostIdentityAsync("cc-owner-" + Guid.NewGuid().ToString("N"), [WorkflowPermissions.FormsCreate, WorkflowPermissions.FormsPublish,
            WorkflowPermissions.DefinitionsCreate, WorkflowPermissions.DefinitionsPublish, WorkflowPermissions.InstancesStart, WorkflowPermissions.InstancesRead, WorkflowPermissions.CcRead, WorkflowPermissions.CcMarkRead]);
        var first = await factory.CreateHostIdentityAsync("cc-first-" + Guid.NewGuid().ToString("N"), [WorkflowPermissions.TodosRead, WorkflowPermissions.TodosComplete, WorkflowPermissions.TodosTransfer, WorkflowPermissions.CcRead, WorkflowPermissions.CcMarkRead]);
        var second = await factory.CreateHostIdentityAsync("cc-second-" + Guid.NewGuid().ToString("N"), [WorkflowPermissions.TodosRead, WorkflowPermissions.TodosComplete, WorkflowPermissions.CcRead, WorkflowPermissions.CcMarkRead]);
        using var starter = Client(factory, owner); using var original = Client(factory, first); using var handler = Client(factory, second);
        var form = await FormAsync(starter, "users", [first.UserId, second.UserId]);
        var nodes = new List<object> { Node("start", "start", new { nextNodeKeys = new[] { "cc0" } }) };
        // 超出单次预算后仍逐节点保存来源快照；实例级去重不删除后续抄送轨迹。
        for (var index = 0; index < 40; index++) nodes.Add(Cc("cc" + index, index == 39 ? "end" : "cc" + (index + 1), new { resolverKindKey = "form_users", fieldKey = "users" }));
        nodes.Add(Node("end", "end", new { })); var version = await PublishAsync(starter, form, nodes.ToArray());
        var receipt = await StartResponseAsync(starter, version, "cc-budget", new { users = second.UserId.ToString("D") }); var instance = receipt.GetProperty("id").GetGuid();
        Assert.AreEqual("active", await StatusAsync(db, instance));
        await DrainAsync(factory);
        Assert.AreEqual("completed", await StatusAsync(db, instance));
        Assert.AreEqual(40, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_step WHERE InstanceId=@instance AND NodeTypeKey='notify.cc' AND AssigneeSnapshotHash IS NOT NULL", new { instance }));
        Assert.AreEqual(1, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_cc WHERE InstanceId=@instance AND RecipientUserId=@user", new { instance, user = second.UserId }));
        var replay = await StartResponseAsync(starter, version, "cc-budget", new { users = second.UserId.ToString("D") }); Assert.AreEqual(receipt.GetRawText(), replay.GetRawText());
        var ccId = await db.ExecuteScalarAsync<Guid>("SELECT Id FROM fn_workflow_cc WHERE InstanceId=@instance", new { instance });
        await PostAsync(original, $"/api/v1/workflow/cc/{ccId:D}/read", new { }, HttpStatusCode.NotFound);
        await PostAsync(handler, $"/api/v1/workflow/cc/{ccId:D}/read", new { });

        var empty = await StartResponseAsync(starter, version, "cc-empty-field", new { }); await DrainAsync(factory); var emptyId = empty.GetProperty("id").GetGuid();
        Assert.AreEqual("completed", await StatusAsync(db, emptyId)); Assert.AreEqual(0, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_cc WHERE InstanceId=@emptyId", new { emptyId }));
        var previousVersion = await PublishAsync(starter, form, [Node("start", "start", new { nextNodeKeys = new[] { "task" } }),
            Node("task", "human.task", new { nextNodeKeys = new[] { "cc" }, taskPolicy = new { completionModeKey = "all" }, assigneePolicy = new { sources = new[] { new { resolverKindKey = "specified_users", userIds = new[] { first.UserId } } } } }),
            Cc("cc", "end", new { resolverKindKey = "previous_node_actors", nodeKey = "task" }), Node("end", "end", new { })]);
        var previous = (await StartResponseAsync(starter, previousVersion, "cc-previous", new { })).GetProperty("id").GetGuid();
        var todo = await db.ExecuteScalarAsync<Guid>("SELECT Id FROM fn_workflow_todo WHERE InstanceId=@previous AND StatusKey='active'", new { previous });
        var runtime = await original.GetFromJsonAsync<JsonElement>($"/api/v1/workflow/todos/{todo:D}/runtime");
        var moved = await PostAsync(original, $"/api/v1/workflow/todos/{todo:D}/transfer", new { assigneeUserId = second.UserId, expectedRevision = runtime.GetProperty("revision").GetInt64(), expectedFormRevision = runtime.GetProperty("submissionRevision").GetInt64(), idempotencyKey = "cc-transfer" });
        var movedId = moved.GetProperty("activeTodoId").GetGuid(); runtime = await handler.GetFromJsonAsync<JsonElement>($"/api/v1/workflow/todos/{movedId:D}/runtime");
        await PostAsync(handler, $"/api/v1/workflow/todos/{movedId:D}/complete", new { expectedRevision = runtime.GetProperty("revision").GetInt64(), expectedFormRevision = runtime.GetProperty("submissionRevision").GetInt64(), fieldPatch = new { }, idempotencyKey = "cc-complete" });
        Assert.AreEqual(0, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_cc WHERE InstanceId=@previous", new { previous }));
        await DrainAsync(factory); Assert.AreEqual(second.UserId, await db.ExecuteScalarAsync<Guid>("SELECT RecipientUserId FROM fn_workflow_cc WHERE InstanceId=@previous", new { previous }));

        var role = Guid.CreateVersion7(); var sourceRole = await db.QueryFirstAsync<Guid>("SELECT RoleId FROM fn_identity_user_role WHERE UserId=@user", new { user = first.UserId });
        await db.ExecuteAsync("""
            INSERT INTO fn_identity_role(Id,TenantId,ScopeKey,Code,Name,IsSystem,IsActive,IsSuperAdministrator,DataScopeKind,CreatedAtUtc,Version)
            SELECT @role,TenantId,ScopeKey,@code,Name,0,1,0,DataScopeKind,CreatedAtUtc,Version FROM fn_identity_role WHERE Id=@sourceRole
            """, new { role, sourceRole, code = "cc-" + role.ToString("N") });
        var roleVersion = await PublishAsync(starter, form, [Node("start", "start", new { nextNodeKeys = new[] { "cc" } }), Cc("cc", "end", new { resolverKindKey = "role_members", roleIds = new[] { role } }), Node("end", "end", new { })]);
        var emptyRole = (await StartResponseAsync(starter, roleVersion, "cc-empty-role", new { })).GetProperty("id").GetGuid(); Assert.AreEqual("completed", await StatusAsync(db, emptyRole));
        await db.ExecuteAsync("UPDATE fn_identity_role SET IsActive=0 WHERE Id=@role", new { role });
        await StartResponseAsync(starter, roleVersion, "cc-invalid-role", new { }, HttpStatusCode.BadRequest);
        Assert.AreEqual(0, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_instance WHERE BusinessId='cc-invalid-role'"));
        await VerifyTenantAsync(factory, db, starter);
    }
    private static async Task VerifyTenantAsync(FullNetApiFactory factory, DbConnection db, HttpClient host)
    {
        using var tenantClient = factory.CreateClientForHost("localhost"); tenantClient.DefaultRequestHeaders.Add("Origin", "http://localhost");
        var login = await PostAsync(tenantClient, "/api/v1/auth/login", new LoginRequest("admin", FullNetApiFactory.TestPassword)); var token = login.GetProperty("accessToken").GetString()!;
        tenantClient.DefaultRequestHeaders.Authorization = new("Bearer", token); var tenants = await tenantClient.GetFromJsonAsync<TenantContextSummary[]>("/api/v1/tenancy/available"); var tenant = tenants!.Single(item => item.Identifier == "acme");
        tenantClient.DefaultRequestHeaders.Authorization = new("Bearer", await IntegrationTestTenantContextHelper.SwitchToTenantAsync(tenantClient, token, tenant.Id));
        var actor = (await tenantClient.GetFromJsonAsync<JsonElement>("/api/v1/me")).GetProperty("id").GetGuid();
        var unit = (await PostAsync(tenantClient, "/api/v1/organization/units", new { parentId = (Guid?)null, code = "cc-" + Guid.NewGuid().ToString("N")[..12], name = "抄送部门", displayOrder = 1 }, HttpStatusCode.Created)).GetProperty("id").GetGuid();
        await PostAsync(tenantClient, "/api/v1/organization/user-units", new { userId = actor, unitId = unit, isPrimary = false }, HttpStatusCode.Created);
        var form = await FormAsync(tenantClient, "units", [unit]); var version = await PublishAsync(tenantClient, form, [Node("start", "start", new { nextNodeKeys = new[] { "cc" } }), Cc("cc", "end", new { resolverKindKey = "form_units", fieldKey = "units" }), Node("end", "end", new { })]);
        var instance = (await StartResponseAsync(tenantClient, version, "cc-tenant", new { units = unit.ToString("D") })).GetProperty("id").GetGuid();
        var ccId = await db.ExecuteScalarAsync<Guid>("SELECT Id FROM fn_workflow_cc WHERE InstanceId=@instance AND RecipientUserId=@actor", new { instance, actor });
        await PostAsync(host, $"/api/v1/workflow/cc/{ccId:D}/read", new { }, HttpStatusCode.NotFound);
        await PostAsync(tenantClient, $"/api/v1/workflow/cc/{ccId:D}/read", new { });
    }
    private static async Task DrainAsync(FullNetApiFactory factory)
    { for (var index = 0; index < 5; index++) { await using var scope = factory.Services.CreateAsyncScope(); if (await scope.ServiceProvider.GetRequiredService<WorkflowActivityWorkProcessor>().ProcessPendingAsync(default) == 0) break; } }
    private static Task<string?> StatusAsync(DbConnection db, Guid instance) => db.ExecuteScalarAsync<string>("SELECT StatusKey FROM fn_workflow_instance WHERE Id=@instance", new { instance });
    private static object Node(string key, string kind, object config) => new { nodeKey = key, nodeTypeKey = kind, nodeSchemaVersion = 1, config };
    private static object Cc(string key, string next, object source) => Node(key, "notify.cc", new { nextNodeKeys = new[] { next }, recipientPolicy = new { sources = new[] { source } } });
    private static async Task<Guid> FormAsync(HttpClient client, string field, Guid[] choices)
    { var form = await PostAsync(client, "/api/v1/workflow/forms", new { formKey = "cc." + Guid.NewGuid().ToString("N"), draft = new { schemaVersion = 1, adapterVersion = 1, sections = new[] { new { sectionKey = "main", fields = new[] { new { fieldKey = field, fieldTypeKey = "select", required = false, constraints = new { options = choices.Select(id => id.ToString("D")).ToArray() } } } } } } }, HttpStatusCode.Created); return (await PostAsync(client, $"/api/v1/workflow/forms/{form.GetProperty("id").GetGuid():D}/publish", new { expectedRevision = 1 })).GetProperty("id").GetGuid(); }
    private static async Task<Guid> PublishAsync(HttpClient client, Guid form, object[] nodes)
    { var draft = await PostAsync(client, "/api/v1/workflow/definitions", new { definitionKey = "cc." + Guid.NewGuid().ToString("N"), draft = new { schemaVersion = 1, workflowKindKey = "automation", nodes } }, HttpStatusCode.Created); return (await PostAsync(client, $"/api/v1/workflow/definitions/{draft.GetProperty("id").GetGuid():D}/publish", new { expectedRevision = 1, formVersionId = form })).GetProperty("id").GetGuid(); }
    private static Task<JsonElement> StartResponseAsync(HttpClient client, Guid version, string key, object values, HttpStatusCode expected = HttpStatusCode.Created) => PostAsync(client, "/api/v1/workflow/instances", new { definitionVersionId = version, businessType = "dynamic-cc", businessId = key, initialValues = values, idempotencyKey = key }, expected);
    private static HttpClient Client(FullNetApiFactory factory, HostTestIdentity identity) { var client = factory.CreateClientForHost("localhost"); client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", identity.AccessToken); return client; }
    private static async Task<JsonElement> PostAsync(HttpClient client, string path, object request, HttpStatusCode expected = HttpStatusCode.OK)
    { using var response = await client.PostAsJsonAsync(path, request); var text = await response.Content.ReadAsStringAsync(); Assert.AreEqual(expected, response.StatusCode, path + ": " + text); return JsonSerializer.Deserialize<JsonElement>(text); }
}
