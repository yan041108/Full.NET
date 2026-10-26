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
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Tenancy.Contracts;
using Microsoft.Data.SqlClient;
using MySqlConnector;

namespace Full.NET.IntegrationTests.Workflow;

/// <summary>人工催办双库验证真实节点、并发回执、限频、发布动作和迁移恢复，不改变自动 SLA。</summary>
[TestClass]
public sealed class WorkflowManualReminderApiTests
{
    [TestMethod]
    public async Task Manual_reminder_with_sql_server() => await VerifyAsync(DatabaseProvider.SqlServer, await SharedDatabaseFixture.CreateSqlServerDatabaseAsync());
    [TestMethod]
    public async Task Manual_reminder_with_mysql() => await VerifyAsync(DatabaseProvider.MySql, await SharedDatabaseFixture.CreateMySqlDatabaseAsync());
    private static async Task VerifyAsync(DatabaseProvider provider, string connection)
    {
        using var factory = new FullNetApiFactory(provider, connection); await factory.InitializeAsync(useSchemaTemplate: false);
        await using DbConnection db = provider == DatabaseProvider.SqlServer ? new SqlConnection(connection)
            : new MySqlConnection(Migration114To126RecoveryTestSupport.MySqlQueryConnectionString(connection));
        var starter = await factory.CreateHostIdentityAsync("remind-owner-" + Guid.NewGuid().ToString("N"),
            [WorkflowPermissions.FormsCreate, WorkflowPermissions.FormsPublish, WorkflowPermissions.DefinitionsCreate, WorkflowPermissions.DefinitionsPublish,
                WorkflowPermissions.InstancesStart, WorkflowPermissions.InstancesRead, WorkflowPermissions.InstancesRemind, WorkflowPermissions.InstancesWithdraw]);
        var actor = await factory.CreateHostIdentityAsync("remind-actor-" + Guid.NewGuid().ToString("N"), [WorkflowPermissions.TodosRead,
            WorkflowPermissions.TodosComplete, WorkflowPermissions.InstancesRemind]);
        using var owner = Client(factory, starter); using var other = Client(factory, actor);
        var form = await PostAsync(owner, "/api/v1/workflow/forms", new { formKey = "remind." + Guid.NewGuid().ToString("N"), draft = new { schemaVersion = 1, adapterVersion = 1,
            sections = new[] { new { sectionKey = "main", fields = new[] { new { fieldKey = "title", fieldTypeKey = "text", required = false, constraints = new { } } } } } } }, HttpStatusCode.Created);
        var formVersion = (await PostAsync(owner, $"/api/v1/workflow/forms/{form.GetProperty("id").GetGuid():D}/publish", new { expectedRevision = 1 })).GetProperty("id").GetGuid();
        foreach (var enabled in new[] { true, false })
        {
            var draft = new { schemaVersion = 1, workflowKindKey = "automation", nodes = new[] {
                Node("start", "start", new { nextNodeKeys = new[] { "task" }, withdrawalPolicy = new { modeKey = "before_any_human_action" } }),
                Node("task", "human.task", new { nextNodeKeys = new[] { "end" }, taskPolicy = new { completionModeKey = "all" },
                    operationPermissions = new { complete = true, remind = enabled },
                    assigneePolicy = new { sources = new[] { new { resolverKindKey = "specified_users", userIds = new[] { actor.UserId, starter.UserId } } } } }),
                Node("end", "end", new { }) } };
            var definition = await PostAsync(owner, "/api/v1/workflow/definitions", new { definitionKey = "remind." + Guid.NewGuid().ToString("N"), draft }, HttpStatusCode.Created);
            var version = (await PostAsync(owner, $"/api/v1/workflow/definitions/{definition.GetProperty("id").GetGuid():D}/publish", new { expectedRevision = 1, formVersionId = formVersion })).GetProperty("id").GetGuid();
            var instance = (await PostAsync(owner, "/api/v1/workflow/instances", new { definitionVersionId = version, businessType = "manual-reminder",
                businessId = enabled.ToString(), initialValues = new { title = "private" }, idempotencyKey = "start-" + enabled }, HttpStatusCode.Created)).GetProperty("id").GetGuid();
            var step = await db.ExecuteScalarAsync<Guid>("SELECT Id FROM fn_workflow_step WHERE InstanceId=@instance AND NodeTypeKey='human.task'", new { instance });
            var path = $"/api/v1/workflow/instances/{instance:D}/remind";
            var request = new { stepId = step, expectedInstanceRevision = 1, expectedStepRevision = 1, idempotencyKey = "manual-first" };
            await PostAsync(other, path, request, HttpStatusCode.Forbidden);
            using (var denied = await other.GetAsync($"/api/v1/workflow/instances/{instance:D}/reminder-targets")) Assert.AreEqual(HttpStatusCode.Forbidden, denied.StatusCode);
            var targets = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/workflow/instances/{instance:D}/reminder-targets");
            Assert.AreEqual(enabled ? 1 : 0, targets.GetProperty("items").GetArrayLength());
            if (!enabled) { await PostAsync(owner, path, request, HttpStatusCode.Forbidden); continue; }
            await PostAsync(owner, path, new { stepId = Guid.CreateVersion7(), expectedInstanceRevision = 1, expectedStepRevision = 1, idempotencyKey = "wrong-step" }, HttpStatusCode.Conflict);
            var first = await Task.WhenAll(PostAsync(owner, path, request), PostAsync(owner, path, request));
            Assert.AreEqual(first[0].GetRawText(), first[1].GetRawText()); Assert.AreEqual(2, first[0].GetProperty("recipientCount").GetInt32());
            Assert.AreEqual(1, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_manual_reminder WHERE InstanceId=@instance", new { instance }));
            Assert.AreEqual(1L, await db.ExecuteScalarAsync<long>("SELECT Revision FROM fn_workflow_instance WHERE Id=@instance", new { instance }));
            Assert.AreEqual(0, await db.ExecuteScalarAsync<int>("SELECT SUM(ReminderCount) FROM fn_workflow_todo WHERE InstanceId=@instance", new { instance }));
            await PostAsync(owner, path, new { stepId = step, expectedInstanceRevision = 1, expectedStepRevision = 2, idempotencyKey = "manual-first" }, HttpStatusCode.Conflict);
            await PostAsync(owner, path, new { stepId = step, expectedInstanceRevision = 1, expectedStepRevision = 1, idempotencyKey = "too-soon" }, HttpStatusCode.TooManyRequests);
            // 夹具只推进服务端持久时间以验证第二次受理；不修改生产时钟和自动 SLA。
            await db.ExecuteAsync("UPDATE fn_workflow_manual_reminder SET CreatedAtUtc=@old WHERE InstanceId=@instance", new { instance, old = DateTime.UtcNow.AddMinutes(-6) });
            await PostAsync(owner, path, new { stepId = step, expectedInstanceRevision = 1, expectedStepRevision = 1, idempotencyKey = "manual-second" });
            var detail = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/workflow/instances/{instance:D}");
            Assert.IsTrue(detail.GetProperty("withdrawal").GetProperty("canWithdraw").GetBoolean());
            await PostAsync(owner, $"/api/v1/workflow/instances/{instance:D}/withdraw", new { expectedRevision = 1, idempotencyKey = "withdraw-after-remind" });
            var replay = await PostAsync(owner, path, request);
            Assert.AreEqual(first[0].GetProperty("requestId").GetGuid(), replay.GetProperty("requestId").GetGuid());
            Assert.AreEqual(1, replay.GetProperty("manualSequence").GetInt32());
        }
        await VerifyTenantAsync(factory, db, owner);
        await db.ExecuteAsync("DROP INDEX ix_fn_workflow_manual_reminder_step_time ON fn_workflow_manual_reminder");
        await db.ExecuteAsync(provider == DatabaseProvider.SqlServer ? "DELETE FROM dbo.SchemaVersions WHERE ScriptName LIKE '%303_WorkflowManualReminders.sql'"
            : "DELETE FROM schemaversions WHERE ScriptName LIKE '%303_WorkflowManualReminders.sql'");
        var recovered = provider == DatabaseProvider.SqlServer ? Migration114To126RecoveryTestSupport.MigrateSqlServer(connection, 303)
            : Migration114To126RecoveryTestSupport.MigrateMySql(connection, 303);
        Assert.IsTrue(recovered.Successful, recovered.Error?.Message);
    }
    private static async Task VerifyTenantAsync(FullNetApiFactory factory, DbConnection db, HttpClient hostReader)
    {
        using var owner = factory.CreateClientForHost("localhost"); owner.DefaultRequestHeaders.Add("Origin", "http://localhost");
        var login = await PostAsync(owner, "/api/v1/auth/login", new LoginRequest("admin", FullNetApiFactory.TestPassword));
        var hostToken = login.GetProperty("accessToken").GetString()!; owner.DefaultRequestHeaders.Authorization = new("Bearer", hostToken);
        var tenants = await owner.GetFromJsonAsync<TenantContextSummary[]>("/api/v1/tenancy/available"); var tenant = tenants!.Single(x => x.Identifier == "acme");
        owner.DefaultRequestHeaders.Authorization = new("Bearer", await IntegrationTestTenantContextHelper.SwitchToTenantAsync(owner, hostToken, tenant.Id));
        var actor = await owner.GetFromJsonAsync<JsonElement>("/api/v1/me"); var actorId = actor.GetProperty("id").GetGuid();
        var form = await PostAsync(owner, "/api/v1/workflow/forms", new { formKey = "tenant-remind." + Guid.NewGuid().ToString("N"), draft = new { schemaVersion = 1, adapterVersion = 1,
            sections = new[] { new { sectionKey = "main", fields = new[] { new { fieldKey = "title", fieldTypeKey = "text", required = false, constraints = new { } } } } } } }, HttpStatusCode.Created);
        var formVersion = (await PostAsync(owner, $"/api/v1/workflow/forms/{form.GetProperty("id").GetGuid():D}/publish", new { expectedRevision = 1 })).GetProperty("id").GetGuid();
        var definition = await PostAsync(owner, "/api/v1/workflow/definitions", new { definitionKey = "tenant-remind." + Guid.NewGuid().ToString("N"), draft = new { schemaVersion = 1, workflowKindKey = "automation", nodes = new[] {
            Node("start", "start", new { nextNodeKeys = new[] { "task" } }), Node("task", "human.task", new { nextNodeKeys = new[] { "end" },
                taskPolicy = new { completionModeKey = "all" }, assigneePolicy = new { sources = new[] { new { resolverKindKey = "specified_users", userIds = new[] { actorId } } } } }), Node("end", "end", new { }) } } }, HttpStatusCode.Created);
        var version = (await PostAsync(owner, $"/api/v1/workflow/definitions/{definition.GetProperty("id").GetGuid():D}/publish", new { expectedRevision = 1, formVersionId = formVersion })).GetProperty("id").GetGuid();
        var instance = (await PostAsync(owner, "/api/v1/workflow/instances", new { definitionVersionId = version, businessType = "manual-reminder",
            businessId = "tenant", initialValues = new { title = "tenant-private" }, idempotencyKey = "tenant-start" }, HttpStatusCode.Created)).GetProperty("id").GetGuid();
        var targets = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/workflow/instances/{instance:D}/reminder-targets");
        var step = targets.GetProperty("items")[0].GetProperty("stepId").GetGuid();
        var request = new { stepId = step, expectedInstanceRevision = 1, expectedStepRevision = 1, idempotencyKey = "tenant-reminder" };
        await PostAsync(hostReader, $"/api/v1/workflow/instances/{instance:D}/remind", request, HttpStatusCode.NotFound);
        await PostAsync(owner, $"/api/v1/workflow/instances/{instance:D}/remind", request);
        Assert.AreEqual(1, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_manual_reminder AS reminder INNER JOIN fn_workflow_instance AS instance ON instance.Id=reminder.InstanceId WHERE instance.Id=@instance AND instance.TenantScopeKey=@scope", new { instance, scope = $"tenant:{tenant.Id:N}" }));
        // 真实租户账号读取 Host 资源同样失败，不能靠拥有权限跨作用域读取回执。
        var hostInstance = await db.ExecuteScalarAsync<Guid>("SELECT InstanceId FROM fn_workflow_manual_reminder WHERE IdempotencyKey='manual-first'");
        using var denied = await owner.GetAsync($"/api/v1/workflow/instances/{hostInstance:D}/reminder-targets");
        Assert.AreEqual(HttpStatusCode.NotFound, denied.StatusCode);
    }
    private static object Node(string key, string kind, object config) => new { nodeKey = key, nodeTypeKey = kind, nodeSchemaVersion = 1, config };
    private static HttpClient Client(FullNetApiFactory factory, HostTestIdentity identity) { var client = factory.CreateClientForHost("localhost"); client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", identity.AccessToken); return client; }
    private static async Task<JsonElement> PostAsync(HttpClient client, string path, object request, HttpStatusCode expected = HttpStatusCode.OK)
    { using var response = await client.PostAsJsonAsync(path, request); var text = await response.Content.ReadAsStringAsync(); Assert.AreEqual(expected, response.StatusCode, path + ": " + text); return JsonSerializer.Deserialize<JsonElement>(text); }
}
