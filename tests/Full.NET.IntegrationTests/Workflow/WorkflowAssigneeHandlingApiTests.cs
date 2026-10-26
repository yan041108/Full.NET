using System.Data.Common;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Dapper;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.IntegrationTests.Migrations;
using Full.NET.Modules.Workflow.Contracts;
using Full.NET.Modules.Workflow.Execution;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using MySqlConnector;

namespace Full.NET.IntegrationTests.Workflow;

/// <summary>真实发布和首次回执验证系统裁定不冒充人审，恢复消费固定输入，替补领取仍检查当前资格。</summary>
[TestClass]
public sealed class WorkflowAssigneeHandlingApiTests
{
    [TestMethod]
    public async Task Assignee_handling_with_sql_server() => await VerifyAsync(DatabaseProvider.SqlServer, await SharedDatabaseFixture.CreateSqlServerDatabaseAsync());
    [TestMethod]
    public async Task Assignee_handling_with_mysql() => await VerifyAsync(DatabaseProvider.MySql, await SharedDatabaseFixture.CreateMySqlDatabaseAsync());

    private static async Task VerifyAsync(DatabaseProvider provider, string connection)
    {
        using var factory = new FullNetApiFactory(provider, connection); await factory.InitializeAsync(useSchemaTemplate: false);
        await using DbConnection db = provider == DatabaseProvider.SqlServer ? new SqlConnection(connection) : new MySqlConnection(Migration114To126RecoveryTestSupport.MySqlQueryConnectionString(connection));
        var owner = await factory.CreateHostIdentityAsync("handling-owner-" + Guid.NewGuid().ToString("N"),
            [WorkflowPermissions.FormsCreate, WorkflowPermissions.FormsPublish, WorkflowPermissions.DefinitionsCreate, WorkflowPermissions.DefinitionsPublish,
             WorkflowPermissions.InstancesStart, WorkflowPermissions.InstancesRead, WorkflowPermissions.InstancesResume]);
        var member = await factory.CreateHostIdentityAsync("handling-member-" + Guid.NewGuid().ToString("N"), [WorkflowPermissions.TodosRead, WorkflowPermissions.TodosApprove, WorkflowPermissions.ClaimPoolsClaim]);
        using var client = Client(factory, owner); using var actor = Client(factory, member);
        var form = await FormAsync(client); var emptyRole = await RoleAsync(db, member.UserId); var usedRole = await RoleAsync(db, member.UserId); var unusedRole = await RoleAsync(db, member.UserId);
        await db.ExecuteAsync("INSERT INTO fn_identity_user_role(UserId,RoleId) VALUES (@user,@used)", new { user = member.UserId, used = usedRole });
        await db.ExecuteAsync("INSERT INTO fn_identity_user_role(UserId,RoleId) VALUES (@user,@unused)", new { user = member.UserId, unused = unusedRole });
        object Role(Guid id) => new { resolverKindKey = "role_members", roleIds = new[] { id } };
        object Policy(string key) => new { sources = new[] { Role(emptyRole) }, handling = new { whenEmpty = new { actionKey = key } } };
        object Review(object policy, string kind = "human.approval") => Node("review", kind, new { nextNodeKeys = new[] { "end" }, assigneePolicy = policy });

        var skipped = await PublishAsync(client, form, [Start("review"), Review(new { sources = new[] { new { resolverKindKey = "initiator" } }, handling = new { whenInitiator = new { actionKey = "skip" } } }), End()]);
        var skippedResponse = await StartAsync(client, skipped, "skip"); var skippedId = skippedResponse.GetProperty("id").GetGuid();
        Assert.AreEqual("completed", skippedResponse.GetProperty("statusKey").GetString()); await NoFakeHumanAsync(db, skippedId);
        Assert.AreEqual(1, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_domain_audit WHERE InstanceId=@id AND OperationKey='assignee.handle' AND ActorUserId IS NULL", new { id = skippedId }));
        await Assert.ThrowsAsync<DbException>(async () => { await db.ExecuteAsync("""
            INSERT INTO fn_workflow_domain_audit(Id,TenantId,ScopeKey,InstanceId,OperationKey,ActorUserId,ResourceTypeKey,ResourceId,OutcomeKey,DetailJson,CreatedAtUtc)
            SELECT @newId,TenantId,ScopeKey,InstanceId,'todo.approve',NULL,ResourceTypeKey,ResourceId,OutcomeKey,DetailJson,CreatedAtUtc
            FROM fn_workflow_domain_audit WHERE InstanceId=@id AND OperationKey='assignee.handle'
            """, new { newId = Guid.CreateVersion7(), id = skippedId }); });

        var rejectVersion = await PublishAsync(client, form, [Start("review"), Review(Policy("auto_reject")), End()]);
        var rejected = await StartAsync(client, rejectVersion, "reject"); var rejectedId = rejected.GetProperty("id").GetGuid();
        Assert.AreEqual("rejected", rejected.GetProperty("statusKey").GetString()); await NoFakeHumanAsync(db, rejectedId);
        Assert.AreEqual(rejected.GetRawText(), (await StartAsync(client, rejectVersion, "reject")).GetRawText());

        var suspendVersion = await PublishAsync(client, form, [Start("review"), Review(Policy("suspend")), End()]);
        var suspended = await StartAsync(client, suspendVersion, "suspend"); var suspendedId = suspended.GetProperty("id").GetGuid();
        Assert.AreEqual("suspended", suspended.GetProperty("statusKey").GetString()); await NoFakeHumanAsync(db, suspendedId);
        Assert.AreEqual(1, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_activity_work WHERE InstanceId=@id AND StatusKey='pending'", new { id = suspendedId }));
        await db.ExecuteAsync("INSERT INTO fn_identity_user_role(UserId,RoleId) VALUES (@user,@role)", new { user = member.UserId, role = emptyRole });
        await PostAsync(client, $"/api/v1/workflow/instances/{suspendedId:D}/resume", new { expectedRevision = suspended.GetProperty("revision").GetInt64(), reason = "role restored", idempotencyKey = "resume-role" });
        await using (var scope = factory.Services.CreateAsyncScope()) Assert.AreEqual(1, await scope.ServiceProvider.GetRequiredService<WorkflowActivityWorkProcessor>().ProcessPendingAsync(default));
        Assert.AreEqual(member.UserId, await db.ExecuteScalarAsync<Guid>("SELECT AssigneeUserId FROM fn_workflow_todo WHERE InstanceId=@id AND StatusKey='active'", new { id = suspendedId }));
        await db.ExecuteAsync("DELETE FROM fn_identity_user_role WHERE UserId=@user AND RoleId=@role", new { user = member.UserId, role = emptyRole });

        var claim = Node("review", "human.claim", new { nextNodeKeys = new[] { "end" }, claimPolicy = new { allowRelease = false }, assigneePolicy = new {
            sources = new[] { Role(emptyRole) }, handling = new { whenEmpty = new { actionKey = "assign", fallbackPolicies = new[] { new { sources = new[] { Role(usedRole) } }, new { sources = new[] { Role(unusedRole) } } } } } } });
        var claimVersion = await PublishAsync(client, form, [Start("review"), claim, End()]); var claimed = await StartAsync(client, claimVersion, "fallback"); var claimId = claimed.GetProperty("id").GetGuid();
        var pool = await db.ExecuteScalarAsync<Guid>("SELECT Id FROM fn_workflow_claim WHERE InstanceId=@id", new { id = claimId });
        await db.ExecuteAsync("DELETE FROM fn_identity_user_role WHERE UserId=@user AND RoleId=@role", new { user = member.UserId, role = usedRole });
        await PostAsync(actor, $"/api/v1/workflow/claim-pools/{pool:D}/claim", new { expectedRevision = 1, idempotencyKey = "revoked-used" }, HttpStatusCode.Forbidden);
        await db.ExecuteAsync("INSERT INTO fn_identity_user_role(UserId,RoleId) VALUES (@user,@role)", new { user = member.UserId, role = usedRole });
        await PostAsync(actor, $"/api/v1/workflow/claim-pools/{pool:D}/claim", new { expectedRevision = 1, idempotencyKey = "restored-used" });

        var nodes = Enumerable.Range(0, 40).Select(index => Node("empty" + index, "control.empty", new { nextNodeKeys = new[] { index == 39 ? "review" : "empty" + (index + 1) } })).ToList();
        nodes.Insert(0, Start("empty0")); nodes.Add(Review(Policy("auto_reject"))); nodes.Add(End());
        var workerVersion = await PublishAsync(client, form, nodes.ToArray()); var deferred = await StartAsync(client, workerVersion, "worker-reject"); var deferredId = deferred.GetProperty("id").GetGuid();
        Assert.AreEqual("active", deferred.GetProperty("statusKey").GetString());
        await using (var scope = factory.Services.CreateAsyncScope()) Assert.AreEqual(1, await scope.ServiceProvider.GetRequiredService<WorkflowActivityWorkProcessor>().ProcessPendingAsync(default));
        Assert.AreEqual("rejected", await db.ExecuteScalarAsync<string>("SELECT StatusKey FROM fn_workflow_instance WHERE Id=@id", new { id = deferredId }));
        Assert.AreEqual(1, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_activity_work WHERE InstanceId=@id AND StatusKey='completed' AND ResultStatusKey='rejected'", new { id = deferredId }));
        await NoFakeHumanAsync(db, deferredId);
    }

    private static async Task NoFakeHumanAsync(DbConnection db, Guid id)
    {
        Assert.AreEqual(0, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_todo WHERE InstanceId=@id", new { id }));
        Assert.AreEqual(0, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_action_record WHERE InstanceId=@id AND ActionKey IN ('approve','reject','complete','vote')", new { id }));
        Assert.AreEqual(0, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_approval_slot WHERE InstanceId=@id", new { id }));
    }
    private static async Task<Guid> RoleAsync(DbConnection db, Guid user)
    {
        var source = await db.QueryFirstAsync<Guid>("SELECT RoleId FROM fn_identity_user_role WHERE UserId=@user", new { user }); var role = Guid.CreateVersion7();
        await db.ExecuteAsync("""
            INSERT INTO fn_identity_role(Id,TenantId,ScopeKey,Code,Name,IsSystem,IsActive,IsSuperAdministrator,DataScopeKind,CreatedAtUtc,Version)
            SELECT @role,TenantId,ScopeKey,@code,Name,0,1,0,DataScopeKind,CreatedAtUtc,Version FROM fn_identity_role WHERE Id=@source
            """, new { role, source, code = "handling-" + role.ToString("N") }); return role;
    }
    private static async Task<Guid> FormAsync(HttpClient client)
    {
        var form = await PostAsync(client, "/api/v1/workflow/forms", new { formKey = "handling." + Guid.NewGuid().ToString("N"), draft = new { schemaVersion = 1, adapterVersion = 1,
            sections = new[] { new { sectionKey = "main", fields = new[] { new { fieldKey = "title", fieldTypeKey = "text", required = false, constraints = new { } } } } } } }, HttpStatusCode.Created);
        return (await PostAsync(client, $"/api/v1/workflow/forms/{form.GetProperty("id").GetGuid():D}/publish", new { expectedRevision = 1 })).GetProperty("id").GetGuid();
    }
    private static async Task<Guid> PublishAsync(HttpClient client, Guid form, object[] nodes)
    {
        var definition = await PostAsync(client, "/api/v1/workflow/definitions", new { definitionKey = "handling." + Guid.NewGuid().ToString("N"), draft = new { schemaVersion = 1, workflowKindKey = "automation", nodes } }, HttpStatusCode.Created);
        return (await PostAsync(client, $"/api/v1/workflow/definitions/{definition.GetProperty("id").GetGuid():D}/publish", new { expectedRevision = 1, formVersionId = form })).GetProperty("id").GetGuid();
    }
    private static Task<JsonElement> StartAsync(HttpClient client, Guid version, string key) => PostAsync(client, "/api/v1/workflow/instances", new { definitionVersionId = version, businessType = "handling", businessId = key, initialValues = new { title = "initial" }, idempotencyKey = key }, HttpStatusCode.Created);
    private static object Node(string key, string kind, object config) => new { nodeKey = key, nodeTypeKey = kind, nodeSchemaVersion = 1, config };
    private static object Start(string next) => Node("start", "start", new { nextNodeKeys = new[] { next } });
    private static object End() => Node("end", "end", new { });
    private static HttpClient Client(FullNetApiFactory factory, HostTestIdentity identity) { var client = factory.CreateClientForHost("localhost"); client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", identity.AccessToken); return client; }
    private static async Task<JsonElement> PostAsync(HttpClient client, string path, object body, HttpStatusCode expected = HttpStatusCode.OK)
    { using var response = await client.PostAsJsonAsync(path, body); var text = await response.Content.ReadAsStringAsync(); Assert.AreEqual(expected, response.StatusCode, path + ": " + text); return JsonSerializer.Deserialize<JsonElement>(text); }
}
