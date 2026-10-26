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

/// <summary>自选范围、不可变启动选择和当前资格通过真实双库及 Worker 验证，角色名册大于二十人不构成拒绝依据。</summary>
[TestClass]
public sealed class WorkflowStarterSelectionApiTests
{
    [TestMethod]
    public async Task Starter_selections_with_sql_server() => await VerifyAsync(DatabaseProvider.SqlServer, await SharedDatabaseFixture.CreateSqlServerDatabaseAsync());
    [TestMethod]
    public async Task Starter_selections_with_mysql() => await VerifyAsync(DatabaseProvider.MySql, await SharedDatabaseFixture.CreateMySqlDatabaseAsync());
    private static async Task VerifyAsync(DatabaseProvider provider, string connection)
    {
        using var factory = new FullNetApiFactory(provider, connection); await factory.InitializeAsync(useSchemaTemplate: false);
        await using DbConnection db = provider == DatabaseProvider.SqlServer ? new SqlConnection(connection)
            : new MySqlConnection(Migration114To126RecoveryTestSupport.MySqlQueryConnectionString(connection));
        var owner = await factory.CreateHostIdentityAsync("starter-designer-" + Guid.NewGuid().ToString("N"),
            [WorkflowPermissions.FormsCreate, WorkflowPermissions.FormsPublish, WorkflowPermissions.DefinitionsCreate, WorkflowPermissions.DefinitionsPublish, WorkflowPermissions.InstancesStart]);
        var requester = await factory.CreateHostIdentityAsync("starter-requester-" + Guid.NewGuid().ToString("N"), [WorkflowPermissions.InstancesStart]);
        var first = await factory.CreateHostIdentityAsync("starter-first-" + Guid.NewGuid().ToString("N"), [WorkflowPermissions.TodosRead, WorkflowPermissions.TodosComplete]);
        var target = await factory.CreateHostIdentityAsync("zz-starter-target-" + Guid.NewGuid().ToString("N"), [WorkflowPermissions.TodosRead, WorkflowPermissions.TodosComplete, WorkflowPermissions.ClaimPoolsClaim]);
        using var designer = Client(factory, owner); using var starter = Client(factory, requester); using var a = Client(factory, first); using var b = Client(factory, target);
        var form = await FormAsync(designer);
        var role = await CloneRoleAsync(db, target.UserId);
        await db.ExecuteAsync("INSERT INTO fn_identity_user_role(UserId, RoleId) VALUES (@user, @role)", new { user = target.UserId, role });
        for (var index = 0; index < 20; index++)
        {
            var person = await factory.CreateHostIdentityAsync("aa-starter-roster-" + index.ToString("D2") + "-" + Guid.NewGuid().ToString("N"), []);
            await db.ExecuteAsync("INSERT INTO fn_identity_user_role(UserId, RoleId) VALUES (@user, @role)", new { user = person.UserId, role });
        }
        var roleSource = Selection("roleChoice", "single", 1, new { kindKey = "role_members", roleIds = new[] { role } });
        var version = await PublishAsync(designer, form, [Node("start", "start", new { nextNodeKeys = new[] { "claim" } }),
            Node("claim", "human.claim", new { nextNodeKeys = new[] { "end" }, claimPolicy = new { allowRelease = true }, assigneePolicy = new { sources = new[] { roleSource } } }), Node("end", "end", new { })]);
        using (var denied = await starter.GetAsync($"/api/v1/workflow/definition-versions/{version:D}")) Assert.AreEqual(HttpStatusCode.Forbidden, denied.StatusCode);
        var metadata = await starter.GetFromJsonAsync<JsonElement>($"/api/v1/workflow/definition-versions/{version:D}/starter-selections");
        Assert.AreEqual("roleChoice", metadata.GetProperty("items")[0].GetProperty("selectionKey").GetString());
        var page = await starter.GetFromJsonAsync<JsonElement>($"/api/v1/workflow/definition-versions/{version:D}/starter-selections/roleChoice/candidates?page=2&pageSize=20");
        Assert.AreEqual(21, page.GetProperty("total").GetInt32()); Assert.AreEqual(1, page.GetProperty("items").GetArrayLength());
        Assert.AreEqual(target.UserId, page.GetProperty("items")[0].GetProperty("id").GetGuid());
        var choices = new Dictionary<string, Guid[]> { ["roleChoice"] = [target.UserId] };
        var request = StartBody(version, "role-start", choices);
        var started = await PostAsync(starter, "/api/v1/workflow/instances", request, HttpStatusCode.Created);
        var instance = started.GetProperty("id").GetGuid();
        Assert.AreEqual(1, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_starter_selection WHERE Id=@instance", new { instance }));
        var pool = await db.ExecuteScalarAsync<Guid>("SELECT Id FROM fn_workflow_claim WHERE InstanceId=@instance", new { instance });
        await db.ExecuteAsync("DELETE FROM fn_identity_user_role WHERE UserId=@user AND RoleId=@role", new { user = target.UserId, role });
        var replay = await PostAsync(starter, "/api/v1/workflow/instances", request, HttpStatusCode.Created);
        Assert.AreEqual(started.GetRawText(), replay.GetRawText());
        await PostAsync(b, $"/api/v1/workflow/claim-pools/{pool:D}/claim", new { expectedRevision = 1, idempotencyKey = "removed-role" }, HttpStatusCode.Forbidden);
        await PostAsync(starter, "/api/v1/workflow/instances", StartBody(version, "role-start", new Dictionary<string, Guid[]> { ["roleChoice"] = [first.UserId] }), HttpStatusCode.Conflict);
        await PostAsync(starter, "/api/v1/workflow/instances", StartBody(version, "missing-choice", new Dictionary<string, Guid[]>()), HttpStatusCode.BadRequest);
        await db.ExecuteAsync("INSERT INTO fn_identity_user_role(UserId, RoleId) VALUES (@user, @role)", new { user = target.UserId, role });
        var acquired = await PostAsync(b, $"/api/v1/workflow/claim-pools/{pool:D}/claim", new { expectedRevision = 1, idempotencyKey = "restored-role" });
        await CompleteAsync(b, acquired.GetProperty("activeTodoId").GetGuid(), "role-complete");

        var nodes = new List<object> { Node("start", "start", new { nextNodeKeys = new[] { "first" } }), TaskNode("first", "empty0", "sequential",
            Selection("firstChoice", "multiple", 2, new { kindKey = "all" })) };
        // 超过每次三十二个活动的确定性预算，不能依赖机器快慢触发续办。
        const int emptyCount = 40;
        for (var index = 0; index < emptyCount; index++) nodes.Add(Node("empty" + index, "control.empty", new { nextNodeKeys = new[] { index == emptyCount - 1 ? "second" : "empty" + (index + 1) } }));
        nodes.Add(TaskNode("second", "end", "all", Selection("secondChoice", "single", 1, new { kindKey = "specified_users", userIds = new[] { target.UserId } })));
        nodes.Add(Node("end", "end", new { }));
        var durableVersion = await PublishAsync(designer, form, nodes.ToArray());
        var durableChoices = new Dictionary<string, Guid[]> { ["secondChoice"] = [target.UserId], ["firstChoice"] = [target.UserId, first.UserId] };
        var durableRequest = StartBody(durableVersion, "durable-selected", durableChoices);
        var durable = await PostAsync(starter, "/api/v1/workflow/instances", durableRequest, HttpStatusCode.Created);
        var durableId = durable.GetProperty("id").GetGuid(); var firstTodo = durable.GetProperty("activeTodoId").GetGuid();
        Assert.AreEqual(target.UserId, await db.ExecuteScalarAsync<Guid>("SELECT AssigneeUserId FROM fn_workflow_todo WHERE Id=@firstTodo", new { firstTodo }));
        var next = await CompleteAsync(b, firstTodo, "first-member");
        next = await CompleteAsync(a, next.GetProperty("activeTodoId").GetGuid(), "second-member");
        Assert.AreEqual(JsonValueKind.Null, next.GetProperty("activeTodoId").ValueKind);
        Assert.AreEqual(1, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_activity_work WHERE InstanceId=@durableId AND StatusKey='pending'", new { durableId }));
        Guid? continued = null;
        for (var attempt = 0; attempt < 8 && continued is null; attempt++)
        {
            await using var scope = factory.Services.CreateAsyncScope(); await scope.ServiceProvider.GetRequiredService<WorkflowActivityWorkProcessor>().ProcessPendingAsync(default);
            continued = await db.QuerySingleOrDefaultAsync<Guid?>("SELECT Id FROM fn_workflow_todo WHERE InstanceId=@durableId AND StatusKey='active'", new { durableId });
        }
        Assert.IsNotNull(continued); await CompleteAsync(b, continued.Value, "durable-finish");
        var reorderedKeys = new Dictionary<string, Guid[]> { ["firstChoice"] = [target.UserId, first.UserId], ["secondChoice"] = [target.UserId] };
        Assert.AreEqual(durable.GetRawText(), (await PostAsync(starter, "/api/v1/workflow/instances", StartBody(durableVersion, "durable-selected", reorderedKeys), HttpStatusCode.Created)).GetRawText());
        var reorderedMembers = new Dictionary<string, Guid[]> { ["firstChoice"] = [first.UserId, target.UserId], ["secondChoice"] = [target.UserId] };
        await PostAsync(starter, "/api/v1/workflow/instances", StartBody(durableVersion, "durable-selected", reorderedMembers), HttpStatusCode.Conflict);
        await VerifyTenantAsync(factory, db, starter);

        await db.ExecuteAsync(provider == DatabaseProvider.SqlServer ? "ALTER TABLE fn_workflow_starter_selection DROP CONSTRAINT ck_fn_workflow_starter_selection_hash" : "ALTER TABLE fn_workflow_starter_selection DROP CHECK ck_fn_workflow_starter_selection_hash");
        await db.ExecuteAsync(provider == DatabaseProvider.SqlServer ? "DELETE FROM dbo.SchemaVersions WHERE ScriptName LIKE '%304_WorkflowStarterSelections.sql'" : "DELETE FROM schemaversions WHERE ScriptName LIKE '%304_WorkflowStarterSelections.sql'");
        var recovered = provider == DatabaseProvider.SqlServer ? Migration114To126RecoveryTestSupport.MigrateSqlServer(connection, 304) : Migration114To126RecoveryTestSupport.MigrateMySql(connection, 304);
        Assert.IsTrue(recovered.Successful, recovered.Error?.Message);
    }
    private static async Task VerifyTenantAsync(FullNetApiFactory factory, DbConnection db, HttpClient host)
    {
        using var client = factory.CreateClientForHost("localhost"); client.DefaultRequestHeaders.Add("Origin", "http://localhost");
        var login = await PostAsync(client, "/api/v1/auth/login", new LoginRequest("admin", FullNetApiFactory.TestPassword));
        var token = login.GetProperty("accessToken").GetString()!; client.DefaultRequestHeaders.Authorization = new("Bearer", token);
        var tenants = await client.GetFromJsonAsync<TenantContextSummary[]>("/api/v1/tenancy/available"); var tenant = tenants!.Single(item => item.Identifier == "acme");
        client.DefaultRequestHeaders.Authorization = new("Bearer", await IntegrationTestTenantContextHelper.SwitchToTenantAsync(client, token, tenant.Id));
        var actor = (await client.GetFromJsonAsync<JsonElement>("/api/v1/me")).GetProperty("id").GetGuid();
        // 正式租户成员资格独立于角色；本场景显式建立选人范围，不能假设管理员已有普通租户角色。
        var role = Guid.CreateVersion7();
        await db.ExecuteAsync("""
            INSERT INTO fn_identity_role (Id,TenantId,ScopeKey,Code,Name,IsSystem,IsActive,IsSuperAdministrator,DataScopeKind,CreatedAtUtc,Version)
            VALUES (@role,@tenant,@scope,@code,'发起人自选范围',0,1,0,@dataScope,@now,1)
            """, new { role, tenant = tenant.Id, scope = $"tenant:{tenant.Id:N}", code = "starter-range-" + role.ToString("N"),
                dataScope = RoleDataScopeKinds.All, now = DateTimeOffset.UtcNow });
        await db.ExecuteAsync("INSERT INTO fn_identity_user_role(UserId, RoleId) VALUES (@actor,@role)", new { actor, role });
        var form = await FormAsync(client);
        var version = await PublishAsync(client, form, [Node("start", "start", new { nextNodeKeys = new[] { "claim" } }),
            Node("claim", "human.claim", new { nextNodeKeys = new[] { "end" }, claimPolicy = new { allowRelease = false }, assigneePolicy = new { sources = new[] {
                Selection("tenantChoice", "single", 1, new { kindKey = "role_members", roleIds = new[] { role } }) } } }), Node("end", "end", new { })]);
        using (var denied = await host.GetAsync($"/api/v1/workflow/definition-versions/{version:D}/starter-selections")) Assert.AreEqual(HttpStatusCode.NotFound, denied.StatusCode);
        var page = await client.GetFromJsonAsync<JsonElement>($"/api/v1/workflow/definition-versions/{version:D}/starter-selections/tenantChoice/candidates?page=1&pageSize=20");
        Assert.AreEqual(actor, page.GetProperty("items")[0].GetProperty("id").GetGuid());
        var started = await PostAsync(client, "/api/v1/workflow/instances", StartBody(version, "tenant-selected", new Dictionary<string, Guid[]> { ["tenantChoice"] = [actor] }), HttpStatusCode.Created);
        var instance = started.GetProperty("id").GetGuid(); var pool = await db.ExecuteScalarAsync<Guid>("SELECT Id FROM fn_workflow_claim WHERE InstanceId=@instance", new { instance });
        await db.ExecuteAsync("DELETE FROM fn_identity_user_role WHERE UserId=@actor AND RoleId=@role", new { actor, role });
        await PostAsync(client, $"/api/v1/workflow/claim-pools/{pool:D}/claim", new { expectedRevision = 1, idempotencyKey = "tenant-removed-role" }, HttpStatusCode.Forbidden);
        await db.ExecuteAsync("INSERT INTO fn_identity_user_role(UserId, RoleId) VALUES (@actor,@role)", new { actor, role });
        await PostAsync(client, $"/api/v1/workflow/claim-pools/{pool:D}/claim", new { expectedRevision = 1, idempotencyKey = "tenant-current-role" });
    }
    private static async Task<Guid> CloneRoleAsync(DbConnection db, Guid user)
    {
        var source = await db.QueryFirstAsync<Guid>("SELECT RoleId FROM fn_identity_user_role WHERE UserId=@user", new { user }); var role = Guid.CreateVersion7();
        await db.ExecuteAsync("""
            INSERT INTO fn_identity_role (Id,TenantId,ScopeKey,Code,Name,IsSystem,IsActive,IsSuperAdministrator,DataScopeKind,CreatedAtUtc,Version)
            SELECT @role,TenantId,ScopeKey,@code,Name,IsSystem,IsActive,IsSuperAdministrator,DataScopeKind,CreatedAtUtc,Version FROM fn_identity_role WHERE Id=@source
            """, new { role, source, code = "starter-range-" + role.ToString("N") }); return role;
    }
    private static object Selection(string key, string mode, int maximum, object range) => new { resolverKindKey = "starter_selected_users", selectionPolicy = new { selectionKey = key, modeKey = mode, maximumUsers = maximum, range } };
    private static object StartBody(Guid version, string key, object choices) => new { definitionVersionId = version, businessType = "starter-selection", businessId = key, initialValues = new { title = "initial" }, idempotencyKey = key, assigneeSelections = choices };
    private static async Task<Guid> FormAsync(HttpClient client)
    {
        var form = await PostAsync(client, "/api/v1/workflow/forms", new { formKey = "selected." + Guid.NewGuid().ToString("N"), draft = new { schemaVersion = 1, adapterVersion = 1,
            sections = new[] { new { sectionKey = "main", fields = new[] { new { fieldKey = "title", fieldTypeKey = "text", required = false, constraints = new { } } } } } } }, HttpStatusCode.Created);
        return (await PostAsync(client, $"/api/v1/workflow/forms/{form.GetProperty("id").GetGuid():D}/publish", new { expectedRevision = 1 })).GetProperty("id").GetGuid();
    }
    private static async Task<Guid> PublishAsync(HttpClient client, Guid form, object[] nodes)
    {
        var definition = await PostAsync(client, "/api/v1/workflow/definitions", new { definitionKey = "selected." + Guid.NewGuid().ToString("N"), draft = new { schemaVersion = 1, workflowKindKey = "automation", nodes } }, HttpStatusCode.Created);
        return (await PostAsync(client, $"/api/v1/workflow/definitions/{definition.GetProperty("id").GetGuid():D}/publish", new { expectedRevision = 1, formVersionId = form })).GetProperty("id").GetGuid();
    }
    private static async Task<JsonElement> CompleteAsync(HttpClient client, Guid todo, string key)
    { var runtime = await client.GetFromJsonAsync<JsonElement>($"/api/v1/workflow/todos/{todo:D}/runtime"); return await PostAsync(client, $"/api/v1/workflow/todos/{todo:D}/complete", new {
        expectedRevision = runtime.GetProperty("revision").GetInt64(), expectedFormRevision = runtime.GetProperty("submissionRevision").GetInt64(), fieldPatch = new { }, idempotencyKey = key }); }
    private static object Node(string key, string kind, object config) => new { nodeKey = key, nodeTypeKey = kind, nodeSchemaVersion = 1, config };
    private static object TaskNode(string key, string next, string mode, object source) => Node(key, "human.task", new { nextNodeKeys = new[] { next }, taskPolicy = new { completionModeKey = mode }, assigneePolicy = new { sources = new[] { source } } });
    private static HttpClient Client(FullNetApiFactory factory, HostTestIdentity user) { var client = factory.CreateClientForHost("localhost"); client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", user.AccessToken); return client; }
    private static async Task<JsonElement> PostAsync(HttpClient client, string path, object body, HttpStatusCode expected = HttpStatusCode.OK)
    { using var response = await client.PostAsJsonAsync(path, body); var text = await response.Content.ReadAsStringAsync(); Assert.AreEqual(expected, response.StatusCode, path + ": " + text); return JsonSerializer.Deserialize<JsonElement>(text); }
}
