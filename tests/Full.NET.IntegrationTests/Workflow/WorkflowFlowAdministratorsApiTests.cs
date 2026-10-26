using System.Data.Common;
using System.Net;
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

/// <summary>固定管理员名单经真实双库的发布、耐久续办、替补、当前身份和租户隔离检验。</summary>
[TestClass]
public sealed class WorkflowFlowAdministratorsApiTests
{
    [TestMethod]
    public async Task Versioned_administrators_with_sql_server() => await VerifyAsync(DatabaseProvider.SqlServer, await SharedDatabaseFixture.CreateSqlServerDatabaseAsync());
    [TestMethod]
    public async Task Versioned_administrators_with_mysql() => await VerifyAsync(DatabaseProvider.MySql, await SharedDatabaseFixture.CreateMySqlDatabaseAsync());

    private static async Task VerifyAsync(DatabaseProvider provider, string connection)
    {
        using var factory = new FullNetApiFactory(provider, connection); await factory.InitializeAsync(useSchemaTemplate: false);
        await using DbConnection db = provider == DatabaseProvider.SqlServer ? new SqlConnection(connection) : new MySqlConnection(Migration114To126RecoveryTestSupport.MySqlQueryConnectionString(connection));
        var owner = await factory.CreateHostIdentityAsync("flow-admin-owner-" + Guid.NewGuid().ToString("N"), [WorkflowPermissions.FormsCreate, WorkflowPermissions.FormsPublish,
            WorkflowPermissions.DefinitionsCreate, WorkflowPermissions.DefinitionsRead, WorkflowPermissions.DefinitionsUpdate, WorkflowPermissions.DefinitionsPublish, WorkflowPermissions.InstancesStart, WorkflowPermissions.ClaimPoolsRead, WorkflowPermissions.ClaimPoolsClaim]);
        var administrator = await factory.CreateHostIdentityAsync("flow-admin-person-" + Guid.NewGuid().ToString("N"), [WorkflowPermissions.TodosRead, WorkflowPermissions.TodosComplete, WorkflowPermissions.ClaimPoolsRead, WorkflowPermissions.ClaimPoolsClaim, "tenancy.tenants.switch"], password: FullNetApiFactory.TestPassword);
        var alternate = await factory.CreateHostIdentityAsync("flow-admin-other-" + Guid.NewGuid().ToString("N"), [WorkflowPermissions.TodosRead, WorkflowPermissions.ClaimPoolsRead, WorkflowPermissions.ClaimPoolsClaim]);
        using var designer = Client(factory, owner.AccessToken); using var actor = Client(factory, administrator.AccessToken); using var other = Client(factory, alternate.AccessToken);
        var form = await FormAsync(designer); var nodes = new List<object> { Node("start", "start", new { nextNodeKeys = new[] { "empty0" }, administratorUserIds = new[] { administrator.UserId } }) };
        for (var index = 0; index < 40; index++) nodes.Add(Node("empty" + index, "control.empty", new { nextNodeKeys = new[] { index == 39 ? "claim" : "empty" + (index + 1) } }));
        nodes.Add(Claim()); nodes.Add(Node("end", "end", new { }));
        var definition = await DefinitionAsync(designer, nodes); var definitionId = definition.GetProperty("id").GetGuid();
        var version = await PublishAsync(designer, definitionId, form);
        var body = Start(version, "durable-original"); var started = await PostAsync(designer, "/api/v1/workflow/instances", body, HttpStatusCode.Created); var instance = started.GetProperty("id").GetGuid();
        Assert.AreEqual(JsonValueKind.Null, started.GetProperty("activeTodoId").ValueKind);
        nodes[0] = Node("start", "start", new { nextNodeKeys = new[] { "empty0" }, administratorUserIds = new[] { alternate.UserId } });
        var currentDraft = await designer.GetFromJsonAsync<JsonElement>($"/api/v1/workflow/definitions/{definitionId:D}");
        long nextRevision;
        using (var updated = await designer.PutAsJsonAsync($"/api/v1/workflow/definitions/{definitionId:D}/draft", new { expectedRevision = currentDraft.GetProperty("draftRevision").GetInt64(), draft = Draft(nodes) }))
        {
            Assert.AreEqual(HttpStatusCode.OK, updated.StatusCode, await updated.Content.ReadAsStringAsync());
            nextRevision = (await updated.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("draftRevision").GetInt64();
        }
        await PublishAsync(designer, definitionId, form, nextRevision);
        Guid? pool = null;
        for (var index = 0; index < 8 && pool is null; index++)
        {
            await using var scope = factory.Services.CreateAsyncScope(); await scope.ServiceProvider.GetRequiredService<WorkflowActivityWorkProcessor>().ProcessPendingAsync(default);
            pool = await db.QuerySingleOrDefaultAsync<Guid?>("SELECT Id FROM fn_workflow_claim WHERE InstanceId=@instance", new { instance });
        }
        Assert.IsNotNull(pool); Assert.AreEqual(administrator.UserId, await db.ExecuteScalarAsync<Guid>("SELECT UserId FROM fn_workflow_claim_candidate WHERE PoolId=@pool", new { pool }));
        Assert.AreEqual(started.GetRawText(), (await PostAsync(designer, "/api/v1/workflow/instances", body, HttpStatusCode.Created)).GetRawText());
        await PostAsync(other, $"/api/v1/workflow/claim-pools/{pool:D}/claim", new { expectedRevision = 1, idempotencyKey = "later-version-admin" }, HttpStatusCode.Forbidden);
        using (var denied = await actor.GetAsync($"/api/v1/workflow/definition-versions/{version:D}")) Assert.AreEqual(HttpStatusCode.Forbidden, denied.StatusCode);
        var claimed = await PostAsync(actor, $"/api/v1/workflow/claim-pools/{pool:D}/claim", new { expectedRevision = 1, idempotencyKey = "original-admin" });
        await CompleteAsync(actor, claimed.GetProperty("activeTodoId").GetGuid(), "original-complete");

        var role = Guid.CreateVersion7(); var sourceRole = await db.QueryFirstAsync<Guid>("SELECT RoleId FROM fn_identity_user_role WHERE UserId=@user", new { user = owner.UserId });
        await db.ExecuteAsync("""
            INSERT INTO fn_identity_role(Id,TenantId,ScopeKey,Code,Name,IsSystem,IsActive,IsSuperAdministrator,DataScopeKind,CreatedAtUtc,Version)
            SELECT @role,TenantId,ScopeKey,@code,Name,IsSystem,IsActive,IsSuperAdministrator,DataScopeKind,CreatedAtUtc,Version FROM fn_identity_role WHERE Id=@sourceRole
            """, new { role, sourceRole, code = "flow-admin-empty-" + role.ToString("N") });
        var fallbackNodes = new[] { Node("start", "start", new { nextNodeKeys = new[] { "claim" }, administratorUserIds = new[] { administrator.UserId } }),
            Node("claim", "human.claim", new { nextNodeKeys = new[] { "end" }, claimPolicy = new { allowRelease = false }, assigneePolicy = new { sources = new[] { new { resolverKindKey = "role_members", roleIds = new[] { role } } },
                handling = new { whenEmpty = new { actionKey = "assign", fallbackPolicies = new[] { new { sources = new[] { new { resolverKindKey = "flow_administrators" } } } } } } } }), Node("end", "end", new { }) };
        var fallbackDefinition = await DefinitionAsync(designer, fallbackNodes); var fallbackVersion = await PublishAsync(designer, fallbackDefinition.GetProperty("id").GetGuid(), form);
        await db.ExecuteAsync("UPDATE fn_identity_user SET IsActive=0 WHERE Id=@id", new { id = administrator.UserId });
        await PostAsync(designer, "/api/v1/workflow/instances", Start(fallbackVersion, "inactive-admin"), HttpStatusCode.BadRequest);
        Assert.AreEqual(0, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_instance WHERE BusinessType='flow.admins' AND BusinessId='inactive-admin'"));
        await db.ExecuteAsync("UPDATE fn_identity_user SET IsActive=1 WHERE Id=@id", new { id = administrator.UserId });
        var fallback = await PostAsync(designer, "/api/v1/workflow/instances", Start(fallbackVersion, "actual-fallback"), HttpStatusCode.Created); var fallbackInstance = fallback.GetProperty("id").GetGuid();
        var fallbackPool = await db.ExecuteScalarAsync<Guid>("SELECT Id FROM fn_workflow_claim WHERE InstanceId=@fallbackInstance", new { fallbackInstance });
        var fallbackClaim = await PostAsync(actor, $"/api/v1/workflow/claim-pools/{fallbackPool:D}/claim", new { expectedRevision = 1, idempotencyKey = "fallback-admin" });
        await CompleteAsync(actor, fallbackClaim.GetProperty("activeTodoId").GetGuid(), "fallback-complete");
        await VerifyTenantAsync(factory, db, designer, administrator);
    }

    private static async Task VerifyTenantAsync(FullNetApiFactory factory, DbConnection db, HttpClient hostReader, HostTestIdentity administrator)
    {
        using var owner = factory.CreateClientForHost("localhost"); owner.DefaultRequestHeaders.Add("Origin", "http://localhost");
        var login = await PostAsync(owner, "/api/v1/auth/login", new LoginRequest("admin", FullNetApiFactory.TestPassword)); var token = login.GetProperty("accessToken").GetString()!; owner.DefaultRequestHeaders.Authorization = new("Bearer", token);
        var tenant = (await owner.GetFromJsonAsync<TenantContextSummary[]>("/api/v1/tenancy/available"))!.Single(value => value.Identifier == "acme");
        owner.DefaultRequestHeaders.Authorization = new("Bearer", await IntegrationTestTenantContextHelper.SwitchToTenantAsync(owner, token, tenant.Id));
        var email = "flow-admin-" + administrator.UserId.ToString("N") + "@example.com";
        await factory.EnsureHostUserProfileEmailAsync(administrator.UserId, email, "Flow administrator");
        using var actor = factory.CreateClientForHost("localhost"); actor.DefaultRequestHeaders.Add("Origin", "http://localhost");
        var hostToken = await IntegrationTestAuthHelper.LoginAsHostUserAsync(actor, administrator.Username, FullNetApiFactory.TestPassword); actor.DefaultRequestHeaders.Authorization = new("Bearer", hostToken);
        var invitation = await PostAsync(owner, "/api/v1/identity/tenant-members/invitations", new CreateTenantInvitationRequest(email, TenantMemberRoles.Member));
        await PostAsync(actor, $"/api/v1/me/tenant-invitations/{invitation.GetProperty("invitation").GetProperty("id").GetGuid():D}/accept", new { });
        actor.DefaultRequestHeaders.Authorization = new("Bearer", await IntegrationTestTenantContextHelper.SwitchToTenantAsync(actor, hostToken, tenant.Id));
        var form = await FormAsync(owner); var definition = await DefinitionAsync(owner, [Node("start", "start", new { nextNodeKeys = new[] { "claim" }, administratorUserIds = new[] { administrator.UserId } }), Claim(), Node("end", "end", new { })]);
        var version = await PublishAsync(owner, definition.GetProperty("id").GetGuid(), form); var started = await PostAsync(owner, "/api/v1/workflow/instances", Start(version, "tenant-admin"), HttpStatusCode.Created);
        var pool = await db.ExecuteScalarAsync<Guid>("SELECT claim.Id FROM fn_workflow_claim AS claim INNER JOIN fn_workflow_instance AS instance ON instance.Id=claim.InstanceId WHERE claim.InstanceId=@instance AND instance.TenantScopeKey=@scope",
            new { instance = started.GetProperty("id").GetGuid(), scope = $"tenant:{tenant.Id:N}" });
        await PostAsync(hostReader, $"/api/v1/workflow/claim-pools/{pool:D}/claim", new { expectedRevision = 1, idempotencyKey = "foreign-host" }, HttpStatusCode.NotFound);
        var claimed = await PostAsync(actor, $"/api/v1/workflow/claim-pools/{pool:D}/claim", new { expectedRevision = 1, idempotencyKey = "tenant-flow-admin" });
        await CompleteAsync(actor, claimed.GetProperty("activeTodoId").GetGuid(), "tenant-admin-complete");
    }

    private static HttpClient Client(FullNetApiFactory factory, string token) { var client = factory.CreateClientForHost("localhost"); client.DefaultRequestHeaders.Authorization = new("Bearer", token); return client; }
    private static object Node(string key, string type, object config) => new { nodeKey = key, nodeTypeKey = type, nodeSchemaVersion = 1, config };
    private static object Claim() => Node("claim", "human.claim", new { nextNodeKeys = new[] { "end" }, claimPolicy = new { allowRelease = false }, assigneePolicy = new { sources = new[] { new { resolverKindKey = "flow_administrators" } } } });
    private static object Draft(IEnumerable<object> nodes) => new { schemaVersion = 1, workflowKindKey = "automation", nodes = nodes.ToArray() };
    private static object Start(Guid version, string key) => new { definitionVersionId = version, businessType = "flow.admins", businessId = key, initialValues = new { title = "Initial" }, idempotencyKey = "start-" + key };
    private static Task<JsonElement> DefinitionAsync(HttpClient client, IEnumerable<object> nodes) => PostAsync(client, "/api/v1/workflow/definitions", new { definitionKey = "flow.admins." + Guid.NewGuid().ToString("N"), draft = Draft(nodes) }, HttpStatusCode.Created);
    private static async Task<Guid> PublishAsync(HttpClient client, Guid id, Guid form, long revision = 1) => (await PostAsync(client, $"/api/v1/workflow/definitions/{id:D}/publish", new { expectedRevision = revision, formVersionId = form })).GetProperty("id").GetGuid();
    private static async Task<Guid> FormAsync(HttpClient client)
    {
        var form = await PostAsync(client, "/api/v1/workflow/forms", new { formKey = "flow.admins." + Guid.NewGuid().ToString("N"), draft = new { schemaVersion = 1, adapterVersion = 1,
            sections = new[] { new { sectionKey = "main", fields = new[] { new { fieldKey = "title", fieldTypeKey = "text", required = false, constraints = new { } } } } } } }, HttpStatusCode.Created);
        return (await PostAsync(client, $"/api/v1/workflow/forms/{form.GetProperty("id").GetGuid():D}/publish", new { expectedRevision = 1 })).GetProperty("id").GetGuid();
    }
    private static async Task CompleteAsync(HttpClient client, Guid todo, string key)
    {
        var runtime = await client.GetFromJsonAsync<JsonElement>($"/api/v1/workflow/todos/{todo:D}/runtime");
        await PostAsync(client, $"/api/v1/workflow/todos/{todo:D}/complete", new { expectedRevision = 1, expectedFormRevision = runtime.GetProperty("submissionRevision").GetInt64(), fieldPatch = new { }, idempotencyKey = key });
    }
    private static async Task<JsonElement> PostAsync(HttpClient client, string path, object body, HttpStatusCode expected = HttpStatusCode.OK)
    { using var response = await client.PostAsJsonAsync(path, body); var text = await response.Content.ReadAsStringAsync(); Assert.AreEqual(expected, response.StatusCode, path + ": " + text); return JsonSerializer.Deserialize<JsonElement>(text); }
}
