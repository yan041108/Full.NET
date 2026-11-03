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
using Microsoft.Data.SqlClient;
using MySqlConnector;

namespace Full.NET.IntegrationTests.Workflow;

public sealed partial class WorkflowCollaborationRequestApiTests
{
    [TestMethod] public Task R5_simulation_operations_with_sql_server() => VerifyR5Async(DatabaseProvider.SqlServer);
    [TestMethod] public Task R5_simulation_operations_with_mysql() => VerifyR5Async(DatabaseProvider.MySql);

    private static async Task VerifyR5Async(DatabaseProvider provider, bool selectionsOnly = false)
    {
        var connection = provider == DatabaseProvider.SqlServer ? await SharedDatabaseFixture.CreateSqlServerDatabaseAsync() : await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        using var factory = new FullNetApiFactory(provider, connection); await factory.InitializeAsync();
        var permissions = WorkflowPermissions.All.Concat(["tenancy.tenants.switch"]).ToArray();
        var hostIdentity = await factory.CreateHostIdentityAsync("r5.host." + Guid.CreateVersion7().ToString("N"), permissions);
        using var host = factory.CreateClientForHost("localhost"); host.DefaultRequestHeaders.Authorization = new("Bearer", hostIdentity.AccessToken);
        using var owner = factory.CreateClientForHost("localhost"); owner.DefaultRequestHeaders.Add("Origin", "http://localhost");
        var admin = await IntegrationTestAuthHelper.LoginAsHostUserAsync(owner, "admin"); owner.DefaultRequestHeaders.Authorization = new("Bearer", admin);
        var tenant = (await owner.GetFromJsonAsync<TenantContextSummary[]>("/api/v1/tenancy/available"))!.Single(item => item.Identifier == "acme");
        owner.DefaultRequestHeaders.Authorization = new("Bearer", await IntegrationTestTenantContextHelper.SwitchToTenantAsync(owner, admin, tenant.Id));
        var name = "r5.member." + Guid.CreateVersion7().ToString("N"); var identity = await factory.CreateHostIdentityAsync(name, permissions, password: FullNetApiFactory.TestPassword);
        var email = name + "@example.com"; await factory.EnsureHostUserProfileEmailAsync(identity.UserId, email, name);
        var invitation = await PostAsync(owner, "/api/v1/identity/tenant-members/invitations", new CreateTenantInvitationRequest(email, TenantMemberRoles.Member));
        using var member = factory.CreateClientForHost("localhost"); member.DefaultRequestHeaders.Add("Origin", "http://localhost");
        var login = await IntegrationTestAuthHelper.LoginAsHostUserAsync(member, name); member.DefaultRequestHeaders.Authorization = new("Bearer", login);
        await PostAsync(member, $"/api/v1/me/tenant-invitations/{invitation.GetProperty("invitation").GetProperty("id").GetGuid():D}/accept", new { });
        member.DefaultRequestHeaders.Authorization = new("Bearer", await IntegrationTestTenantContextHelper.SwitchToTenantAsync(member, login, tenant.Id));
        await using DbConnection db = provider == DatabaseProvider.SqlServer ? new SqlConnection(connection) : new MySqlConnection(Migration114To126RecoveryTestSupport.MySqlQueryConnectionString(connection));
        if (selectionsOnly)
        {
            var hostPreview = await CreateSimulationPreviewerAsync(factory, () => factory.CreateClientForHost("localhost"), owner, null);
            var tenantPreview = await CreateSimulationPreviewerAsync(factory, () => factory.CreateClientForHost("localhost"), owner, tenant.Id);
            using (hostPreview.Client) using (tenantPreview.Client)
            {
                await VerifySimulationSelectionsAsync(host, hostPreview.Client, member, hostPreview.UserId, db);
                await VerifySimulationSelectionsAsync(member, tenantPreview.Client, host, tenantPreview.UserId, db);
            }
            if (Environment.GetEnvironmentVariable("FULLNET_CLIENT_OPENAPI_EXPORT_PATH") is { Length: > 0 } selectedExport)
                await File.WriteAllTextAsync(selectedExport, await host.GetStringAsync("/openapi/v1.json"));
            return;
        }
        foreach (var (client, wrongScope) in new[] { (host, member), (member, host) })
        {
            var asset = await R5AssetAsync(client, false); var path = $"/api/v1/workflow/definitions/{asset.Definition:D}/simulate";
            var before = await R5FactsAsync(db);
            var simulation = await PostAsync(client, path, new { expectedRevision = 1, formVersionId = asset.Form, values = new { amount = 12 } });
            Assert.AreEqual(1L, simulation.GetProperty("draftRevision").GetInt64()); Assert.AreEqual(asset.Form, simulation.GetProperty("formVersionId").GetGuid());
            Assert.IsFalse(simulation.GetProperty("reachesEnd").GetBoolean());
            var trace = simulation.GetProperty("trace").EnumerateArray().ToArray();
            Assert.AreEqual("large", trace.Single(item => item.GetProperty("nodeKey").GetString() == "gateway").GetProperty("outcomeKey").GetString());
            Assert.AreEqual(1, trace.Single(item => item.GetProperty("nodeKey").GetString() == "approve").GetProperty("memberCount").GetInt32());
            Assert.AreEqual(before, await R5FactsAsync(db), "只读仿真不能建立版本、实例、步骤、工作或领域审计。");
            var fixedForm = await client.GetFromJsonAsync<JsonElement>($"/api/v1/workflow/form-versions/{asset.Form:D}");
            var formId = fixedForm.GetProperty("formDefinitionId").GetGuid();
            var formDraft = await client.GetFromJsonAsync<JsonElement>($"/api/v1/workflow/forms/{formId:D}");
            var formRevision = formDraft.GetProperty("draftRevision").GetInt64();
            using (var changed = await client.PutAsJsonAsync($"/api/v1/workflow/forms/{formId:D}/draft", new { expectedRevision = formRevision,
                draft = new { schemaVersion = 1, adapterVersion = 1, sections = new[] { new { sectionKey = "main", fields = new[] {
                    new { fieldKey = "amount", fieldTypeKey = "integer", required = true, constraints = new { minimum = 0, maximum = 7 } } } } } } }))
                Assert.AreEqual(HttpStatusCode.OK, changed.StatusCode, await changed.Content.ReadAsStringAsync());
            var newerForm = (await PostAsync(client, $"/api/v1/workflow/forms/{formId:D}/publish", new { expectedRevision = formRevision + 1 })).GetProperty("id").GetGuid();
            await PostAsync(client, path, new { expectedRevision = 1, formVersionId = asset.Form, values = new { amount = 12 } });
            using (var newerInvalid = await client.PostAsJsonAsync(path, new { expectedRevision = 1, formVersionId = newerForm, values = new { amount = 12 } }))
                Assert.AreEqual(HttpStatusCode.BadRequest, newerInvalid.StatusCode);
            foreach (var (reader, status) in new[] { (wrongScope, HttpStatusCode.NotFound) })
            { using var denied = await reader.PostAsJsonAsync(path, new { expectedRevision = 1, formVersionId = asset.Form, values = new { amount = 12 } }); Assert.AreEqual(status, denied.StatusCode); }
            using (var stale = await client.PostAsJsonAsync(path, new { expectedRevision = 2, formVersionId = asset.Form, values = new { amount = 12 } })) Assert.AreEqual(HttpStatusCode.Conflict, stale.StatusCode);
            using (var invalid = await client.PostAsJsonAsync(path, new { expectedRevision = 1, formVersionId = asset.Form, values = new { amount = "12" } })) Assert.AreEqual(HttpStatusCode.BadRequest, invalid.StatusCode);
            using (var injected = await client.PostAsJsonAsync(path, new { expectedRevision = 1, formVersionId = asset.Form, values = new { amount = 12 }, tenantId = tenant.Id })) Assert.AreEqual(HttpStatusCode.BadRequest, injected.StatusCode);
            var version = (await PostAsync(client, $"/api/v1/workflow/definitions/{asset.Definition:D}/publish", new { expectedRevision = 1, formVersionId = asset.Form })).GetProperty("id").GetGuid();
            foreach (var amount in new[] { 12, 5 })
            {
                var preview = await PostAsync(client, path, new { expectedRevision = 2, formVersionId = asset.Form, values = new { amount } });
                var human = preview.GetProperty("trace").EnumerateArray().Single(item => item.GetProperty("nodeTypeKey").GetString() == "human.approval").GetProperty("nodeKey").GetString();
                var instance = await PostAsync(client, "/api/v1/workflow/instances", new { definitionVersionId = version, businessType = "r5", businessId = Guid.CreateVersion7().ToString("N"), initialValues = new { amount }, idempotencyKey = Guid.CreateVersion7().ToString("N") }, HttpStatusCode.Created);
                var id = instance.GetProperty("id").GetGuid();
                Assert.AreEqual(human, await db.ExecuteScalarAsync<string>("SELECT NodeKey FROM fn_workflow_step WHERE InstanceId=@id AND NodeTypeKey='human.approval' AND StatusKey='active'", new { id }));
                // 时间是测试夹具数据，实际激活、SLA及作用域均由生产启动链建立。
                await db.ExecuteAsync("UPDATE fn_workflow_step SET StartedAtUtc=@past WHERE InstanceId=@id AND StatusKey='active'; UPDATE fn_workflow_sla SET DueAtUtc=@due WHERE InstanceId=@id AND StatusKey='waiting'",
                    new { id, past = DateTimeOffset.UtcNow.AddMinutes(-10), due = DateTimeOffset.UtcNow.AddMinutes(-2) });
                var url = $"/api/v1/workflow/operations/steps?definitionVersionId={version:D}&minimumDwellSeconds=300&onlyOverdue=true&pageSize=1";
                var observed = await client.GetFromJsonAsync<JsonElement>(url);
                Assert.IsTrue(observed.GetProperty("total").GetInt64() >= 1); Assert.AreEqual(1, observed.GetProperty("items").GetArrayLength());
                Assert.IsTrue(observed.GetProperty("items")[0].GetProperty("dwellSeconds").GetInt64() >= 600);
                var foreign = await wrongScope.GetFromJsonAsync<JsonElement>(url); Assert.AreEqual(0L, foreign.GetProperty("total").GetInt64());
                var current = await client.GetFromJsonAsync<JsonElement>($"/api/v1/workflow/instances/{id:D}");
                await PostAsync(client, $"/api/v1/workflow/instances/{id:D}/pause", new { expectedRevision = current.GetProperty("revision").GetInt64(), reason = "R5暂停边界", idempotencyKey = Guid.CreateVersion7().ToString("N") });
                var paused = await client.GetFromJsonAsync<JsonElement>(url); Assert.AreEqual(0L, paused.GetProperty("total").GetInt64());
            }
            var queued = await R5AssetAsync(client, true);
            var queuedVersion = (await PostAsync(client, $"/api/v1/workflow/definitions/{queued.Definition:D}/publish", new { expectedRevision = 1, formVersionId = queued.Form })).GetProperty("id").GetGuid();
            var queuedInstance = await PostAsync(client, "/api/v1/workflow/instances", new { definitionVersionId = queuedVersion, businessType = "r5", businessId = Guid.CreateVersion7().ToString("N"), initialValues = new { amount = 1 }, idempotencyKey = Guid.CreateVersion7().ToString("N") }, HttpStatusCode.Created);
            var queuedId = queuedInstance.GetProperty("id").GetGuid(); var backlogPath = $"/api/v1/workflow/operations/backlog?definitionVersionId={queuedVersion:D}";
            var work = await client.GetFromJsonAsync<JsonElement>(backlogPath); Assert.AreEqual(1L, work.GetProperty("total").GetInt64());
            Assert.IsTrue(work.GetProperty("items")[0].GetProperty("isReady").GetBoolean());
            await db.ExecuteAsync("UPDATE fn_workflow_activity_work SET DueAtUtc=@future WHERE InstanceId=@id", new { id = queuedId, future = DateTimeOffset.UtcNow.AddHours(1) });
            work = await client.GetFromJsonAsync<JsonElement>(backlogPath); Assert.IsFalse(work.GetProperty("items")[0].GetProperty("isReady").GetBoolean());
            Assert.IsFalse(work.GetRawText().Contains("inputJson", StringComparison.OrdinalIgnoreCase));
            var hidden = await wrongScope.GetFromJsonAsync<JsonElement>(backlogPath); Assert.AreEqual(0L, hidden.GetProperty("total").GetInt64());
        }
        using var forbidden = factory.CreateClientForHost("localhost"); forbidden.DefaultRequestHeaders.Authorization = new("Bearer", await factory.CreateHostAccessTokenAsync([WorkflowPermissions.DefinitionsRead, WorkflowPermissions.FormsRead, WorkflowPermissions.InstancesRead]));
        using var anonymous = factory.CreateClientForHost("localhost");
        foreach (var (client, status) in new[] { (forbidden, HttpStatusCode.Forbidden), (anonymous, HttpStatusCode.Unauthorized) })
        {
            using var simulation = await client.PostAsJsonAsync($"/api/v1/workflow/definitions/{Guid.CreateVersion7():D}/simulate", new { expectedRevision = 1, formVersionId = Guid.CreateVersion7(), values = new { amount = 1 } }); Assert.AreEqual(status, simulation.StatusCode);
            foreach (var route in new[] { "steps", "backlog" }) { using var response = await client.GetAsync("/api/v1/workflow/operations/" + route); Assert.AreEqual(status, response.StatusCode); }
        }
        using (var invalidPage = await host.GetAsync("/api/v1/workflow/operations/steps?pageSize=101")) Assert.AreEqual(HttpStatusCode.BadRequest, invalidPage.StatusCode);
        if (Environment.GetEnvironmentVariable("FULLNET_CLIENT_OPENAPI_EXPORT_PATH") is { Length: > 0 } export) await File.WriteAllTextAsync(export, await host.GetStringAsync("/openapi/v1.json"));
    }

    private static async Task<Asset> R5AssetAsync(HttpClient client, bool queued)
    {
        var form = await PostAsync(client, "/api/v1/workflow/forms", new { formKey = "r5." + Guid.CreateVersion7().ToString("N"), draft = new { schemaVersion = 1, adapterVersion = 1,
            sections = new[] { new { sectionKey = "main", fields = new[] { new { fieldKey = "amount", fieldTypeKey = "integer", required = true, constraints = new { minimum = 0, maximum = 100 } } } } } } }, HttpStatusCode.Created);
        var formId = form.GetProperty("id").GetGuid(); var formVersion = (await PostAsync(client, $"/api/v1/workflow/forms/{formId:D}/publish", new { expectedRevision = 1 })).GetProperty("id").GetGuid();
        var nodes = new List<object>();
        if (queued)
        {
            nodes.Add(new { nodeKey = "start", nodeTypeKey = "start", nodeSchemaVersion = 1, config = new { nextNodeKeys = new[] { "empty0" } } });
            for (var i = 0; i < 40; i++) nodes.Add(new { nodeKey = "empty" + i, nodeTypeKey = "control.empty", nodeSchemaVersion = 1, config = new { nextNodeKeys = new[] { i == 39 ? "end" : "empty" + (i + 1) } } });
        }
        else
        {
            nodes.Add(new { nodeKey = "start", nodeTypeKey = "start", nodeSchemaVersion = 1, config = new { nextNodeKeys = new[] { "gateway" } } });
            nodes.Add(new { nodeKey = "gateway", nodeTypeKey = "gateway.exclusive", nodeSchemaVersion = 1, config = new { nextNodeKeys = new[] { "approve", "review" },
                branches = new[] { new { branchKey = "large", nextNodeKey = "approve", condition = new { fieldKey = "amount", @operator = "greaterThanOrEqual", value = 10 } } }, defaultNextNodeKey = "review" } });
            foreach (var key in new[] { "approve", "review" }) nodes.Add(new { nodeKey = key, nodeTypeKey = "human.approval", nodeSchemaVersion = 1,
                config = new { nextNodeKeys = new[] { "end" }, slaPolicy = new { dueAfterMinutes = 30, pauseModeKey = "freeze", actionKey = "suspend" } } });
        }
        nodes.Add(new { nodeKey = "end", nodeTypeKey = "end", nodeSchemaVersion = 1, config = new { } }); var keyName = "r5." + Guid.CreateVersion7().ToString("N");
        var definition = await PostAsync(client, "/api/v1/workflow/definitions", new { definitionKey = keyName, draft = new { schemaVersion = 1, workflowKindKey = queued ? "automation" : "approval", nodes } }, HttpStatusCode.Created);
        return new(definition.GetProperty("id").GetGuid(), Guid.Empty, formVersion, keyName);
    }
    private static async Task<string> R5FactsAsync(DbConnection db) => await db.ExecuteScalarAsync<string>("SELECT CONCAT((SELECT COUNT(*) FROM fn_workflow_instance),':',(SELECT COUNT(*) FROM fn_workflow_step),':',(SELECT COUNT(*) FROM fn_workflow_activity_work),':',(SELECT COUNT(*) FROM fn_workflow_domain_audit),':',(SELECT COUNT(*) FROM fn_workflow_definition_version))") ?? throw new InvalidOperationException("Missing fixture counts.");
}
