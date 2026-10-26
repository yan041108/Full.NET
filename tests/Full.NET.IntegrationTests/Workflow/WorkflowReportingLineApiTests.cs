using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Dapper;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.IntegrationTests.Migrations;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Organization.Contracts;
using Full.NET.Modules.Tenancy.Contracts;
using Full.NET.Modules.Workflow.Contracts;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using MySqlConnector;

namespace Full.NET.IntegrationTests.Workflow;

/// <summary>直属汇报图真实双库竞争、环检测、可信成员、来源快照、领取复核及迁移恢复。</summary>
[TestClass]
public sealed class WorkflowReportingLineApiTests
{
    [TestMethod]
    public async Task Reporting_relationships_with_sql_server() => await VerifyAsync(DatabaseProvider.SqlServer, await SharedDatabaseFixture.CreateSqlServerDatabaseAsync());
    [TestMethod]
    public async Task Reporting_relationships_with_mysql() => await VerifyAsync(DatabaseProvider.MySql, await SharedDatabaseFixture.CreateMySqlDatabaseAsync());
    private static async Task VerifyAsync(DatabaseProvider provider, string connection)
    {
        using var factory = new FullNetApiFactory(provider, connection); await factory.InitializeAsync(useSchemaTemplate: false);
        await using DbConnection db = provider == DatabaseProvider.SqlServer ? new SqlConnection(connection) : new MySqlConnection(Migration114To126RecoveryTestSupport.MySqlQueryConnectionString(connection));
        using var owner = factory.CreateClientForHost("localhost"); owner.DefaultRequestHeaders.Add("Origin", "http://localhost");
        var login = await PostAsync(owner, "/api/v1/auth/login", new LoginRequest("admin", FullNetApiFactory.TestPassword)); var token = login.GetProperty("accessToken").GetString()!;
        owner.DefaultRequestHeaders.Authorization = new("Bearer", token);
        var tenant = (await owner.GetFromJsonAsync<TenantContextSummary[]>("/api/v1/tenancy/available"))!.Single(value => value.Identifier == "acme");
        owner.DefaultRequestHeaders.Authorization = new("Bearer", await IntegrationTestTenantContextHelper.SwitchToTenantAsync(owner, token, tenant.Id));
        var actor = (await owner.GetFromJsonAsync<JsonElement>("/api/v1/me")).GetProperty("id").GetGuid();
        var users = new List<HostTestIdentity>(); var clients = new List<HttpClient>();
        try
        {
            for (var index = 0; index < 2; index++)
            {
                var name = "reporting-" + Guid.NewGuid().ToString("N"); var email = name + "@example.com";
                var user = await factory.CreateHostIdentityAsync(name, [WorkflowPermissions.TodosRead, WorkflowPermissions.TodosComplete, WorkflowPermissions.ClaimPoolsRead, WorkflowPermissions.ClaimPoolsClaim, "tenancy.tenants.switch"], password: FullNetApiFactory.TestPassword);
                users.Add(user); await factory.EnsureHostUserProfileEmailAsync(user.UserId, email, name);
                var client = factory.CreateClientForHost("localhost"); clients.Add(client); client.DefaultRequestHeaders.Add("Origin", "http://localhost");
                var hostToken = await IntegrationTestAuthHelper.LoginAsHostUserAsync(client, name, FullNetApiFactory.TestPassword); client.DefaultRequestHeaders.Authorization = new("Bearer", hostToken);
                var invitation = await PostAsync(owner, "/api/v1/identity/tenant-members/invitations", new CreateTenantInvitationRequest(email, TenantMemberRoles.Member));
                await PostAsync(client, $"/api/v1/me/tenant-invitations/{invitation.GetProperty("invitation").GetProperty("id").GetGuid():D}/accept", new { });
                client.DefaultRequestHeaders.Authorization = new("Bearer", await IntegrationTestTenantContextHelper.SwitchToTenantAsync(client, hostToken, tenant.Id));
            }
            string Path(Guid employee) => $"/api/v1/organization/users/{employee:D}/reporting-line";
            async Task<OrganizationReportingLineResponse> Read(Guid employee) => (await owner.GetFromJsonAsync<OrganizationReportingLineResponse>(Path(employee)))!;
            async Task Put(Guid employee, Guid? manager, long revision, HttpStatusCode status = HttpStatusCode.OK)
            { using var response = await owner.PutAsJsonAsync(Path(employee), new SetOrganizationReportingLineRequest(manager, revision)); Assert.AreEqual(status, response.StatusCode, await response.Content.ReadAsStringAsync()); }
            using (var denied = await clients[0].GetAsync(Path(actor))) Assert.AreEqual(HttpStatusCode.Forbidden, denied.StatusCode);
            await Put(actor, actor, 0, HttpStatusCode.BadRequest);
            await Put(actor, Guid.CreateVersion7(), 0, HttpStatusCode.BadRequest);
            var competing = await Task.WhenAll(owner.PutAsJsonAsync(Path(actor), new SetOrganizationReportingLineRequest(users[0].UserId, 0)),
                owner.PutAsJsonAsync(Path(users[0].UserId), new SetOrganizationReportingLineRequest(actor, 0)));
            try
            {
                Assert.HasCount(1, competing.Where(value => value.StatusCode == HttpStatusCode.OK).ToArray());
                Assert.HasCount(1, competing.Where(value => value.StatusCode == HttpStatusCode.BadRequest).ToArray());
            }
            finally { foreach (var response in competing) response.Dispose(); }
            foreach (var employee in new[] { actor, users[0].UserId }) await Put(employee, null, (await Read(employee)).Revision);
            var before = await Read(actor); await Put(actor, users[0].UserId, before.Revision); var current = await Read(actor);
            Assert.AreEqual(actor, current.UpdatedById); Assert.AreEqual(users[0].UserId, current.ManagerUserId);
            await Put(actor, users[1].UserId, before.Revision, HttpStatusCode.Conflict);
            await using (var scope = factory.Services.CreateAsyncScope())
            {
                scope.ServiceProvider.GetRequiredService<CurrentTenantAccessor>().SetTenant(new(Guid.CreateVersion7(), "other", "Other"));
                var foreign = await scope.ServiceProvider.GetRequiredService<IWorkflowOrganizationAssigneeDirectory>().ResolveDirectManagerAsync(actor);
                Assert.IsTrue(foreign.SourceIsValid); Assert.IsNull(foreign.ManagerUserId);
            }
            var form = await PostAsync(owner, "/api/v1/workflow/forms", new { formKey = "reporting." + Guid.NewGuid().ToString("N"), draft = new { schemaVersion = 1, adapterVersion = 1,
                sections = new[] { new { sectionKey = "main", fields = new[] { new { fieldKey = "title", fieldTypeKey = "text", required = false, constraints = new { } } } } } } }, HttpStatusCode.Created);
            var formVersion = await PostAsync(owner, $"/api/v1/workflow/forms/{form.GetProperty("id").GetGuid():D}/publish", new { expectedRevision = 1 });
            object Node(string key, string kind, object config) => new { nodeKey = key, nodeTypeKey = kind, nodeSchemaVersion = 1, config };
            var definition = await PostAsync(owner, "/api/v1/workflow/definitions", new { definitionKey = "reporting." + Guid.NewGuid().ToString("N"), draft = new { schemaVersion = 1, workflowKindKey = "automation", nodes = new[] {
                Node("start", "start", new { nextNodeKeys = new[] { "claim" } }), Node("claim", "human.claim", new { nextNodeKeys = new[] { "end" }, claimPolicy = new { allowRelease = false },
                    assigneePolicy = new { sources = new[] { new { resolverKindKey = "initiator_direct_manager" } } } }), Node("end", "end", new { }) } } }, HttpStatusCode.Created);
            var version = await PostAsync(owner, $"/api/v1/workflow/definitions/{definition.GetProperty("id").GetGuid():D}/publish", new { expectedRevision = 1, formVersionId = formVersion.GetProperty("id").GetGuid() });
            var started = await PostAsync(owner, "/api/v1/workflow/instances", new { definitionVersionId = version.GetProperty("id").GetGuid(), businessType = "reporting", businessId = "claim", initialValues = new { title = "Initial" }, idempotencyKey = "reporting-start" }, HttpStatusCode.Created);
            var instance = started.GetProperty("id").GetGuid(); var pool = await db.ExecuteScalarAsync<Guid>("SELECT Id FROM fn_workflow_claim WHERE InstanceId=@instance", new { instance });
            var snapshot = await db.ExecuteScalarAsync<string>("SELECT AssigneeSnapshotJson FROM fn_workflow_step WHERE InstanceId=@instance", new { instance });
            using (var parsed = JsonDocument.Parse(snapshot!)) Assert.AreEqual(current.Revision, parsed.RootElement.GetProperty("sources")[0].GetProperty("memberships")[0].GetProperty("sourceRevision").GetInt64());
            await Put(actor, users[1].UserId, current.Revision);
            await PostAsync(clients[0], $"/api/v1/workflow/claim-pools/{pool:D}/claim", new { expectedRevision = 1, idempotencyKey = "old-manager" }, HttpStatusCode.Forbidden);
            await PostAsync(clients[1], $"/api/v1/workflow/claim-pools/{pool:D}/claim", new { expectedRevision = 1, idempotencyKey = "new-manager-not-frozen" }, HttpStatusCode.Forbidden);
            await Put(actor, users[0].UserId, current.Revision + 1);
            var claimed = await PostAsync(clients[0], $"/api/v1/workflow/claim-pools/{pool:D}/claim", new { expectedRevision = 1, idempotencyKey = "restored-manager" });
            var todo = claimed.GetProperty("activeTodoId").GetGuid();
            var runtime = await clients[0].GetFromJsonAsync<JsonElement>($"/api/v1/workflow/todos/{todo:D}/runtime");
            await PostAsync(clients[0], $"/api/v1/workflow/todos/{todo:D}/complete", new { expectedRevision = 1, expectedFormRevision = runtime.GetProperty("submissionRevision").GetInt64(), fieldPatch = new { }, idempotencyKey = "reporting-complete" });
            Assert.AreEqual(snapshot, await db.ExecuteScalarAsync<string>("SELECT AssigneeSnapshotJson FROM fn_workflow_step WHERE InstanceId=@instance", new { instance }));
            await db.ExecuteAsync("UPDATE fn_identity_user SET IsActive=0 WHERE Id=@id", new { id = users[0].UserId });
            await Put(actor, users[0].UserId, current.Revision + 2, HttpStatusCode.BadRequest);
            await db.ExecuteAsync("UPDATE fn_identity_user SET IsActive=1 WHERE Id=@id", new { id = users[0].UserId });
            await db.ExecuteAsync($"ALTER TABLE fn_organization_reporting_line DROP {(provider == DatabaseProvider.SqlServer ? "CONSTRAINT" : "FOREIGN KEY")} fk_fn_organization_reporting_line_guard");
            await db.ExecuteAsync(provider == DatabaseProvider.SqlServer ? "ALTER TABLE fn_organization_reporting_guard DROP CONSTRAINT ux_fn_organization_reporting_guard_tenant" : "ALTER TABLE fn_organization_reporting_guard DROP INDEX ux_fn_organization_reporting_guard_tenant");
            await db.ExecuteAsync($"ALTER TABLE fn_organization_reporting_line DROP {(provider == DatabaseProvider.SqlServer ? "CONSTRAINT" : "CHECK")} ck_fn_organization_reporting_line_state");
            await db.ExecuteAsync(provider == DatabaseProvider.SqlServer ? "DELETE FROM dbo.SchemaVersions WHERE ScriptName LIKE '%308_OrganizationReportingLines.sql'" : "DELETE FROM schemaversions WHERE ScriptName LIKE '%308_OrganizationReportingLines.sql'");
            await WorkflowHumanMigrationRecoveryTests.MigrateAsync(provider, connection);
            Assert.AreEqual(current.Revision + 2, (await Read(actor)).Revision);
            Assert.AreEqual(1, provider == DatabaseProvider.SqlServer ? await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM sys.foreign_keys WHERE name='fk_fn_organization_reporting_line_guard'")
                : await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM information_schema.REFERENTIAL_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND CONSTRAINT_NAME='fk_fn_organization_reporting_line_guard'"));
            if (provider == DatabaseProvider.MySql) Assert.AreEqual(0, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM information_schema.ROUTINES WHERE ROUTINE_SCHEMA=DATABASE() AND ROUTINE_NAME='fn_organization_restore_reporting_308'"));
        }
        finally { foreach (var client in clients) client.Dispose(); }
    }
    private static async Task<JsonElement> PostAsync(HttpClient client, string path, object body, HttpStatusCode expected = HttpStatusCode.OK)
    { using var response = await client.PostAsJsonAsync(path, body); var text = await response.Content.ReadAsStringAsync(); Assert.AreEqual(expected, response.StatusCode, path + ": " + text); return JsonSerializer.Deserialize<JsonElement>(text); }
}
