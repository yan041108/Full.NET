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
using Full.NET.Modules.Files.Contracts;
using Full.NET.Modules.Tenancy.Contracts;
using Full.NET.Modules.Workflow.Contracts;
using Full.NET.Modules.Workflow.Execution;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using MySqlConnector;
using static Full.NET.IntegrationTests.Workflow.WorkflowDurableDelayApiTests;

namespace Full.NET.IntegrationTests.Workflow;

/// <summary>固定字段活动的双库事务、重复消费、条件、分支、取消与可信租户边界。</summary>
[TestClass]
public sealed class WorkflowFieldActivityApiTests
{
    [TestMethod]
    public async Task Field_activities_with_sql_server() => await VerifyAsync(DatabaseProvider.SqlServer, await SharedDatabaseFixture.CreateSqlServerDatabaseAsync());
    [TestMethod]
    public async Task Field_activities_with_mysql() => await VerifyAsync(DatabaseProvider.MySql, await SharedDatabaseFixture.CreateMySqlDatabaseAsync());

    private static async Task VerifyAsync(DatabaseProvider provider, string connection)
    {
        using var factory = new FullNetApiFactory(provider, connection);
        await factory.InitializeAsync(useSchemaTemplate: false);
        await using DbConnection db = provider == DatabaseProvider.SqlServer ? new SqlConnection(connection)
            : new MySqlConnection(Migration114To126RecoveryTestSupport.MySqlQueryConnectionString(connection));
        // 升级前的305约束只接纳选人系统审计；311须按实际定义替换，不能仅按同名约束存在跳过。
        await db.ExecuteAsync(provider == DatabaseProvider.SqlServer
            ? "ALTER TABLE dbo.fn_workflow_domain_audit DROP CONSTRAINT ck_fn_workflow_domain_audit_actor"
            : "ALTER TABLE fn_workflow_domain_audit DROP CHECK ck_fn_workflow_domain_audit_actor");
        await db.ExecuteAsync("ALTER TABLE fn_workflow_domain_audit ADD CONSTRAINT ck_fn_workflow_domain_audit_actor CHECK (ActorUserId IS NOT NULL OR (OperationKey='assignee.handle' AND ResourceTypeKey='step'))");
        await db.ExecuteAsync(provider == DatabaseProvider.SqlServer
            ? "DELETE FROM dbo.SchemaVersions WHERE ScriptName LIKE '%311_WorkflowFieldActivityResult.sql'"
            : "DELETE FROM schemaversions WHERE ScriptName LIKE '%311_WorkflowFieldActivityResult.sql'");
        await WorkflowHumanMigrationRecoveryTests.MigrateAsync(provider, connection);
        using var host = factory.CreateClientForHost("localhost");
        var actor = await factory.CreateHostIdentityAsync("field-" + Guid.NewGuid().ToString("N"),
            [WorkflowPermissions.FormsCreate, WorkflowPermissions.FormsPublish, WorkflowPermissions.DefinitionsCreate,
                WorkflowPermissions.DefinitionsPublish, WorkflowPermissions.InstancesStart, WorkflowPermissions.InstancesRead, WorkflowPermissions.InstancesCancel, HostFilePermissions.Upload]);
        host.DefaultRequestHeaders.Authorization = new("Bearer", actor.AccessToken);
        await VerifyScopeAsync(factory, db, host);
        await VerifyAttachmentsAsync(factory, db, host);

        using var tenantClient = factory.CreateClientForHost("localhost"); tenantClient.DefaultRequestHeaders.Add("Origin", "http://localhost");
        var login = await PostAsync(tenantClient, "/api/v1/auth/login", new LoginRequest("admin", FullNetApiFactory.TestPassword));
        tenantClient.DefaultRequestHeaders.Authorization = new("Bearer", login.GetProperty("accessToken").GetString());
        var tenants = await tenantClient.GetFromJsonAsync<TenantContextSummary[]>("/api/v1/tenancy/available");
        var tenant = tenants!.Single(item => item.Identifier == "acme");
        tenantClient.DefaultRequestHeaders.Authorization = new("Bearer", await IntegrationTestTenantContextHelper.SwitchToTenantAsync(
            tenantClient, login.GetProperty("accessToken").GetString()!, tenant.Id));
        var tenantInstance = await VerifyScopeAsync(factory, db, tenantClient);
        using (var foreign = await host.GetAsync($"/api/v1/workflow/instances/{tenantInstance:D}")) Assert.AreEqual(HttpStatusCode.NotFound, foreign.StatusCode);
        Assert.AreEqual(3, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_activity_result WHERE InstanceId=@tenantInstance AND TenantScopeKey=@scope", new { tenantInstance, scope = $"tenant:{tenant.Id:N}" }));

        var total = await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_activity_result");
        var auditTotal = await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_domain_audit WHERE OperationKey='activity.execute'");
        await db.ExecuteAsync(provider == DatabaseProvider.SqlServer
            ? "ALTER TABLE dbo.fn_workflow_domain_audit DROP CONSTRAINT ck_fn_workflow_domain_audit_actor"
            : "ALTER TABLE fn_workflow_domain_audit DROP CHECK ck_fn_workflow_domain_audit_actor");
        await db.ExecuteAsync(provider == DatabaseProvider.SqlServer
            ? "DELETE FROM dbo.SchemaVersions WHERE ScriptName LIKE '%311_WorkflowFieldActivityResult.sql'"
            : "DELETE FROM schemaversions WHERE ScriptName LIKE '%311_WorkflowFieldActivityResult.sql'");
        await WorkflowHumanMigrationRecoveryTests.MigrateAsync(provider, connection);
        await WorkflowHumanMigrationRecoveryTests.MigrateAsync(provider, connection);
        Assert.AreEqual(total, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_activity_result"));
        Assert.AreEqual(auditTotal, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_domain_audit WHERE OperationKey='activity.execute'"));
    }

    private static async Task<Guid> VerifyScopeAsync(FullNetApiFactory factory, DbConnection db, HttpClient client)
    {
        var form = await PostAsync(client, "/api/v1/workflow/forms", new { formKey = "field." + Guid.NewGuid().ToString("N"), draft = new {
            schemaVersion = 1, adapterVersion = 1, sections = new[] { new { sectionKey = "main", fields = new object[] {
                new { fieldKey = "first", fieldTypeKey = "text", required = false, constraints = new { } },
                new { fieldKey = "second", fieldTypeKey = "text", required = false, constraints = new { } },
                new { fieldKey = "flag", fieldTypeKey = "switch", required = false, constraints = new { } } } } } } }, HttpStatusCode.Created);
        var formVersion = (await PostAsync(client, $"/api/v1/workflow/forms/{form.GetProperty("id").GetGuid():D}/publish", new { expectedRevision = 1 })).GetProperty("id").GetGuid();
        var version = await PublishNodesAsync(client, formVersion, [
            Node("start", "start", new { nextNodeKeys = new[] { "modify" } }),
            Node("modify", "data.modify", new { nextNodeKeys = new[] { "route" }, fieldPolicies = new { first = "editable", second = "editable", flag = "editable" },
                mutationPolicy = new { assignments = new object[] { new { fieldKey = "first", sourceFieldKey = "second" }, new { fieldKey = "second", sourceFieldKey = "first" }, new { fieldKey = "flag", value = true } } } }),
            Node("route", "gateway.exclusive", new { nextNodeKeys = new[] { "skip", "wrong" }, branches = new[] { new { branchKey = "yes", nextNodeKey = "skip", condition = new { fieldKey = "flag", @operator = "equals", value = true } } }, defaultNextNodeKey = "wrong" }),
            Node("skip", "data.clear", new { nextNodeKeys = new[] { "clear" }, fieldPolicies = new { first = "editable", flag = "readOnly" }, mutationPolicy = new { fieldKeys = new[] { "first" }, condition = new { fieldKey = "flag", @operator = "equals", value = false } } }),
            Node("clear", "data.clear", new { nextNodeKeys = new[] { "end" }, fieldPolicies = new { second = "editable" }, mutationPolicy = new { fieldKeys = new[] { "second" } } }),
            Node("wrong", "control.empty", new { nextNodeKeys = new[] { "end" } }), Node("end", "end", new { })]);
        object Request(Guid target) => new { definitionVersionId = target, businessType = "field", businessId = Guid.NewGuid().ToString("N"), initialValues = new { first = "A", second = "B", flag = false }, idempotencyKey = Guid.NewGuid().ToString("N") };
        var request = Request(version); var started = await PostAsync(client, "/api/v1/workflow/instances", request, HttpStatusCode.Created);
        var id = started.GetProperty("id").GetGuid();
        Assert.AreEqual("active", started.GetProperty("statusKey").GetString());
        Assert.AreEqual(JsonValueKind.Null, started.GetProperty("activeTodoId").ValueKind);
        Assert.AreEqual(1, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_activity_work WHERE InstanceId=@id AND StatusKey='pending'", new { id }));
        var consumed = (await Task.WhenAll(WorkAsync(factory), WorkAsync(factory))).Sum();
        // 后一个扫描也可能看到前一个已提交的新后继；按总活动数验证，而非假定扫描时间完全重合。
        for (var attempt = 0; attempt < 4; attempt++) consumed += await WorkAsync(factory);
        Assert.AreEqual(3, consumed); Assert.AreEqual(0, await WorkAsync(factory));
        Assert.AreEqual("completed", await db.ExecuteScalarAsync<string>("SELECT StatusKey FROM fn_workflow_instance WHERE Id=@id", new { id }));
        Assert.AreEqual(3, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_activity_result WHERE InstanceId=@id", new { id }));
        Assert.AreEqual(3, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_domain_audit WHERE InstanceId=@id AND OperationKey='activity.execute' AND ResourceTypeKey='step' AND ActorUserId IS NULL", new { id }));
        await Assert.ThrowsAsync<DbException>(async () => { await db.ExecuteAsync("""
            INSERT INTO fn_workflow_domain_audit (Id,TenantId,ScopeKey,InstanceId,OperationKey,ActorUserId,ResourceTypeKey,ResourceId,OutcomeKey,DetailJson,CreatedAtUtc,CorrelationJson)
            SELECT @auditId,TenantId,ScopeKey,InstanceId,'todo.approve',NULL,'todo',ResourceId,'succeeded',NULL,CreatedAtUtc,CorrelationJson
            FROM fn_workflow_domain_audit WHERE InstanceId=@id AND ResourceId IN (SELECT Id FROM fn_workflow_activity_result WHERE InstanceId=@id AND InputFormRevision=1)
            """, new { auditId = Guid.CreateVersion7(), id }); });
        Assert.AreEqual(1, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_activity_result WHERE InstanceId=@id AND OutcomeKey='skipped' AND InputFormRevision=OutputFormRevision", new { id }));
        Assert.AreEqual(0, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_todo WHERE InstanceId=@id", new { id }));
        Assert.AreEqual(0, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_step WHERE InstanceId=@id AND NodeKey='wrong'", new { id }));
        var json = await db.ExecuteScalarAsync<string>("SELECT SubmissionJson FROM fn_workflow_form_submission WHERE InstanceId=@id", new { id });
        using (var output = JsonDocument.Parse(json!)) { Assert.AreEqual("B", output.RootElement.GetProperty("first").GetString()); Assert.AreEqual(JsonValueKind.Null, output.RootElement.GetProperty("second").ValueKind); Assert.IsTrue(output.RootElement.GetProperty("flag").GetBoolean()); }
        Assert.AreEqual(3L, await db.ExecuteScalarAsync<long>("SELECT Revision FROM fn_workflow_form_submission WHERE InstanceId=@id", new { id }));
        Assert.AreEqual(started.GetRawText(), (await PostAsync(client, "/api/v1/workflow/instances", request, HttpStatusCode.Created)).GetRawText());

        var cancel = (await PostAsync(client, "/api/v1/workflow/instances", Request(version), HttpStatusCode.Created)).GetProperty("id").GetGuid();
        await PostAsync(client, $"/api/v1/workflow/instances/{cancel:D}/cancel", new { expectedRevision = 1, reason = "取消字段活动", idempotencyKey = "cancel" });
        Assert.AreEqual(0, await WorkAsync(factory));
        Assert.AreEqual(0, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_activity_result WHERE InstanceId=@cancel", new { cancel }));

        var conflict = (await PostAsync(client, "/api/v1/workflow/instances", Request(version), HttpStatusCode.Created)).GetProperty("id").GetGuid();
        await db.ExecuteAsync("UPDATE fn_workflow_form_submission SET Revision=Revision+1 WHERE InstanceId=@conflict", new { conflict });
        Assert.AreEqual(1, await WorkAsync(factory)); Assert.AreEqual(0, await WorkAsync(factory));
        Assert.AreEqual("suspended", await db.ExecuteScalarAsync<string>("SELECT StatusKey FROM fn_workflow_instance WHERE Id=@conflict", new { conflict }));
        Assert.AreEqual("faulted", await db.ExecuteScalarAsync<string>("SELECT StatusKey FROM fn_workflow_activity_work WHERE InstanceId=@conflict", new { conflict }));
        Assert.AreEqual(0, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_activity_result WHERE InstanceId=@conflict", new { conflict }));

        var parallel = await PublishNodesAsync(client, formVersion, [
            Node("start", "start", new { nextNodeKeys = new[] { "fork" } }),
            Node("fork", "gateway.parallel", new { gatewayRoleKey = "fork", joinNodeKey = "join", branches = new[] { new { branchKey = "left", nextNodeKey = "left" }, new { branchKey = "right", nextNodeKey = "right" } } }),
            Node("left", "data.modify", new { nextNodeKeys = new[] { "join" }, fieldPolicies = new { first = "editable" }, mutationPolicy = new { assignments = new[] { new { fieldKey = "first", value = "left" } } } }),
            Node("right", "data.modify", new { nextNodeKeys = new[] { "join" }, fieldPolicies = new { second = "editable" }, mutationPolicy = new { assignments = new[] { new { fieldKey = "second", value = "right" } } } }),
            Node("join", "gateway.parallel", new { gatewayRoleKey = "join", forkNodeKey = "fork", nextNodeKeys = new[] { "end" } }), Node("end", "end", new { })]);
        var parallelId = (await PostAsync(client, "/api/v1/workflow/instances", Request(parallel), HttpStatusCode.Created)).GetProperty("id").GetGuid();
        Assert.AreEqual(2, await WorkAsync(factory)); Assert.AreEqual(0, await WorkAsync(factory));
        Assert.AreEqual("completed", await db.ExecuteScalarAsync<string>("SELECT StatusKey FROM fn_workflow_instance WHERE Id=@parallelId", new { parallelId }));
        var merged = await db.ExecuteScalarAsync<string>("SELECT SubmissionJson FROM fn_workflow_form_submission WHERE InstanceId=@parallelId", new { parallelId });
        using (var output = JsonDocument.Parse(merged!)) { Assert.AreEqual("left", output.RootElement.GetProperty("first").GetString()); Assert.AreEqual("right", output.RootElement.GetProperty("second").GetString()); }
        return id;
    }

    private static object Node(string key, string kind, object config) => new { nodeKey = key, nodeTypeKey = kind, nodeSchemaVersion = 1, config };

    private static async Task VerifyAttachmentsAsync(FullNetApiFactory factory, DbConnection db, HttpClient host)
    {
        var file = await UploadAsync(host);
        var form = await PostAsync(host, "/api/v1/workflow/forms", new { formKey = "field.files." + Guid.NewGuid().ToString("N"), draft = new {
            schemaVersion = 1, adapterVersion = 1, sections = new[] { new { sectionKey = "main", fields = new[] {
                new { fieldKey = "sourceEvidence", fieldTypeKey = "attachment", required = false, constraints = new { maxCount = 1, maxSizeBytes = 1024, allowedExtensions = new[] { "png" } } },
                new { fieldKey = "targetEvidence", fieldTypeKey = "attachment", required = false, constraints = new { maxCount = 1, maxSizeBytes = 1024, allowedExtensions = new[] { "png" } } } } } } } }, HttpStatusCode.Created);
        var formVersion = (await PostAsync(host, $"/api/v1/workflow/forms/{form.GetProperty("id").GetGuid():D}/publish", new { expectedRevision = 1 })).GetProperty("id").GetGuid();
        var copy = await PublishNodesAsync(host, formVersion, [Node("start", "start", new { nextNodeKeys = new[] { "copy" } }),
            Node("copy", "data.modify", new { nextNodeKeys = new[] { "end" }, fieldPolicies = new { sourceEvidence = "readOnly", targetEvidence = "editable" },
                mutationPolicy = new { assignments = new[] { new { fieldKey = "targetEvidence", sourceFieldKey = "sourceEvidence" } } } }), Node("end", "end", new { })]);
        var id = (await PostAsync(host, "/api/v1/workflow/instances", new { definitionVersionId = copy, businessType = "field.files", businessId = "copy",
            initialValues = new { sourceEvidence = new[] { file }, targetEvidence = Array.Empty<Guid>() }, idempotencyKey = "copy" }, HttpStatusCode.Created)).GetProperty("id").GetGuid();
        Assert.AreEqual(1, await WorkAsync(factory));
        Assert.AreEqual("completed", await db.ExecuteScalarAsync<string>("SELECT StatusKey FROM fn_workflow_instance WHERE Id=@id", new { id }));
        Assert.AreEqual(2, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_form_submission_attachment WHERE InstanceId=@id AND FileId=@file", new { id, file }));
        using (var submission = JsonDocument.Parse((await db.ExecuteScalarAsync<string>("SELECT SubmissionJson FROM fn_workflow_form_submission WHERE InstanceId=@id", new { id }))!))
            Assert.AreEqual(file, submission.RootElement.GetProperty("targetEvidence")[0].GetGuid());

        var stranger = await factory.CreateHostIdentityAsync("field-file-stranger-" + Guid.NewGuid().ToString("N"), [HostFilePermissions.Upload]);
        using var foreign = factory.CreateClientForHost("localhost"); foreign.DefaultRequestHeaders.Authorization = new("Bearer", stranger.AccessToken);
        var foreignFile = await UploadAsync(foreign);
        var forged = await PublishNodesAsync(host, formVersion, [Node("start", "start", new { nextNodeKeys = new[] { "write" } }),
            Node("write", "data.modify", new { nextNodeKeys = new[] { "end" }, fieldPolicies = new { targetEvidence = "editable" },
                mutationPolicy = new { assignments = new[] { new { fieldKey = "targetEvidence", value = new[] { foreignFile } } } } }), Node("end", "end", new { })]);
        var denied = (await PostAsync(host, "/api/v1/workflow/instances", new { definitionVersionId = forged, businessType = "field.files", businessId = "denied",
            initialValues = new { sourceEvidence = Array.Empty<Guid>(), targetEvidence = Array.Empty<Guid>() }, idempotencyKey = "denied" }, HttpStatusCode.Created)).GetProperty("id").GetGuid();
        Assert.AreEqual(1, await WorkAsync(factory));
        Assert.AreEqual("suspended", await db.ExecuteScalarAsync<string>("SELECT StatusKey FROM fn_workflow_instance WHERE Id=@denied", new { denied }));
        Assert.AreEqual(0, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_form_submission_attachment WHERE InstanceId=@denied", new { denied }));
        Assert.AreEqual(0, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_activity_result WHERE InstanceId=@denied", new { denied }));
    }

    private static async Task<Guid> UploadAsync(HttpClient client)
    {
        using var body = new MultipartFormDataContent(); var bytes = new ByteArrayContent(Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+j2e8AAAAASUVORK5CYII="));
        bytes.Headers.ContentType = new("image/png"); body.Add(bytes, "file", "field.png");
        using var response = await client.PostAsync("/api/v1/files/host-files/", body);
        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }
    private static async Task<int> WorkAsync(FullNetApiFactory factory)
    { await using var scope = factory.Services.CreateAsyncScope(); return await scope.ServiceProvider.GetRequiredService<WorkflowActivityWorkProcessor>().ProcessPendingAsync(default); }
}
