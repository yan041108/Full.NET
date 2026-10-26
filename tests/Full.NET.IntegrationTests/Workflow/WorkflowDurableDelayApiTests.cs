using System.Data.Common;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Dapper;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.IntegrationTests.Migrations;
using Full.NET.Modules.Workflow.Contracts;
using Full.NET.Modules.Workflow.Execution;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Tenancy.Contracts;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MySqlConnector;

namespace Full.NET.IntegrationTests.Workflow;

/// <summary>真实双库覆盖延时的暂停冻结、绝对截止、原子唤醒、取消、固定回执及部分DDL恢复。</summary>
[TestClass]
public sealed class WorkflowDurableDelayApiTests
{
    [TestMethod]
    public async Task Durable_delay_with_sql_server() => await VerifyAsync(DatabaseProvider.SqlServer, await SharedDatabaseFixture.CreateSqlServerDatabaseAsync());
    [TestMethod]
    public async Task Durable_delay_with_mysql() => await VerifyAsync(DatabaseProvider.MySql, await SharedDatabaseFixture.CreateMySqlDatabaseAsync());

    private static async Task VerifyAsync(DatabaseProvider provider, string connection)
    {
        var clock = new TestClock { UtcNow = DateTimeOffset.UtcNow };
        using var factory = new FullNetApiFactory(provider, connection,
            configureTestServices: services => services.Replace(ServiceDescriptor.Singleton<IClock>(clock)));
        await factory.InitializeAsync(useSchemaTemplate: false);
        clock.UtcNow = DateTimeOffset.UtcNow;
        await using DbConnection db = provider == DatabaseProvider.SqlServer ? new SqlConnection(connection)
            : new MySqlConnection(Migration114To126RecoveryTestSupport.MySqlQueryConnectionString(connection));
        using var client = factory.CreateClientForHost("localhost");
        var identity = await factory.CreateHostIdentityAsync("delay-" + Guid.NewGuid().ToString("N"),
            [WorkflowPermissions.FormsCreate, WorkflowPermissions.FormsPublish, WorkflowPermissions.DefinitionsCreate,
                WorkflowPermissions.DefinitionsPublish, WorkflowPermissions.InstancesStart, WorkflowPermissions.InstancesRead,
                WorkflowPermissions.InstancesPause, WorkflowPermissions.InstancesResume, WorkflowPermissions.InstancesCancel]);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", identity.AccessToken);
        var form = await PostAsync(client, "/api/v1/workflow/forms", new { formKey = "delay." + Guid.NewGuid().ToString("N"),
            draft = new { schemaVersion = 1, adapterVersion = 1, sections = new[] { new { sectionKey = "main", fields = new object[] {
                new { fieldKey = "when", fieldTypeKey = "datetime", required = false, constraints = new { } },
                new { fieldKey = "flag", fieldTypeKey = "switch", required = false, constraints = new { } } } } } } }, HttpStatusCode.Created);
        var formVersion = (await PostAsync(client, $"/api/v1/workflow/forms/{form.GetProperty("id").GetGuid():D}/publish", new { expectedRevision = 1 })).GetProperty("id").GetGuid();
        var relative = await PublishAsync(client, formVersion, new { modeKey = "relative", timeZoneKey = "UTC", duration = 2, unitKey = "minute" });
        object Request(Guid version) => new { definitionVersionId = version, businessType = "delay", businessId = Guid.NewGuid().ToString("N"),
            initialValues = new { flag = true, when = clock.UtcNow.AddSeconds(10).ToString("yyyy-MM-dd'T'HH:mm:ss'Z'") }, idempotencyKey = Guid.NewGuid().ToString("N") };

        var request = Request(relative);
        var started = await PostAsync(client, "/api/v1/workflow/instances", request, HttpStatusCode.Created);
        var id = started.GetProperty("id").GetGuid();
        Assert.AreEqual("active", started.GetProperty("statusKey").GetString()); Assert.AreEqual(JsonValueKind.Null, started.GetProperty("activeTodoId").ValueKind);
        Assert.AreEqual(0, await ProcessAsync(factory));
        Assert.AreEqual(0, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_todo WHERE InstanceId=@id", new { id }));
        clock.UtcNow = clock.UtcNow.AddSeconds(30);
        await PostAsync(client, $"/api/v1/workflow/instances/{id:D}/pause", new { expectedRevision = 1, idempotencyKey = "pause" });
        var remaining = await db.ExecuteScalarAsync<long>("SELECT RemainingMilliseconds FROM fn_workflow_delay WHERE InstanceId=@id", new { id });
        Assert.IsTrue(remaining is >= 89999 and <= 90000, remaining.ToString());
        clock.UtcNow = clock.UtcNow.AddSeconds(40);
        Assert.AreEqual(0, await ProcessAsync(factory));
        await PostAsync(client, $"/api/v1/workflow/instances/{id:D}/resume", new { expectedRevision = 2, idempotencyKey = "resume" });
        var due = await ReadDueAsync(db, id);
        Assert.IsTrue(Math.Abs((due - clock.UtcNow).TotalMilliseconds - remaining) < 1);
        Assert.AreEqual(1, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_step AS step INNER JOIN fn_workflow_delay AS delay ON delay.Id=step.Id WHERE delay.InstanceId=@id AND step.DueAtUtc=delay.DueAtUtc", new { id }));
        clock.UtcNow = clock.UtcNow.AddSeconds(89); Assert.AreEqual(0, await ProcessAsync(factory));
        clock.UtcNow = clock.UtcNow.AddSeconds(2);
        var concurrent = await Task.WhenAll(ProcessAsync(factory), ProcessAsync(factory)); Assert.AreEqual(1, concurrent.Sum());
        Assert.AreEqual(1, await WorkAsync(factory)); Assert.AreEqual(0, await ProcessAsync(factory)); Assert.AreEqual(0, await WorkAsync(factory));
        Assert.AreEqual("completed", await db.ExecuteScalarAsync<string>("SELECT StatusKey FROM fn_workflow_instance WHERE Id=@id", new { id }));
        Assert.AreEqual(1, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_step WHERE InstanceId=@id AND NodeKey='yes'", new { id }));
        Assert.AreEqual(0, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_step WHERE InstanceId=@id AND NodeKey='no'", new { id }));
        Assert.AreEqual(started.GetRawText(), (await PostAsync(client, "/api/v1/workflow/instances", request, HttpStatusCode.Created)).GetRawText());

        // 绝对字段时间在激活时固化，暂停不平移，恢复后已到期只唤醒一次。
        var absolute = await PublishAsync(client, formVersion, new { modeKey = "absolute", timeZoneKey = "UTC", fieldKey = "when" });
        var absoluteStart = await PostAsync(client, "/api/v1/workflow/instances", Request(absolute), HttpStatusCode.Created);
        var absoluteId = absoluteStart.GetProperty("id").GetGuid(); var absoluteDue = await ReadDueAsync(db, absoluteId);
        await PostAsync(client, $"/api/v1/workflow/instances/{absoluteId:D}/pause", new { expectedRevision = 1, idempotencyKey = "pause" });
        clock.UtcNow = clock.UtcNow.AddSeconds(20); Assert.AreEqual(0, await ProcessAsync(factory));
        await PostAsync(client, $"/api/v1/workflow/instances/{absoluteId:D}/resume", new { expectedRevision = 2, idempotencyKey = "resume" });
        Assert.AreEqual(absoluteDue, await ReadDueAsync(db, absoluteId)); Assert.AreEqual(1, await ProcessAsync(factory)); Assert.AreEqual(1, await WorkAsync(factory));

        var cancelled = await PostAsync(client, "/api/v1/workflow/instances", Request(relative), HttpStatusCode.Created); var cancelledId = cancelled.GetProperty("id").GetGuid();
        await PostAsync(client, $"/api/v1/workflow/instances/{cancelledId:D}/cancel", new { expectedRevision = 1, reason = "取消等待", idempotencyKey = "cancel" });
        clock.UtcNow = clock.UtcNow.AddMinutes(3); Assert.AreEqual(0, await ProcessAsync(factory));
        Assert.AreEqual("cancelled", await db.ExecuteScalarAsync<string>("SELECT StatusKey FROM fn_workflow_delay WHERE InstanceId=@cancelledId", new { cancelledId }));
        Assert.AreEqual(0, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_activity_work WHERE InstanceId=@cancelledId", new { cancelledId }));

        // 分支唤醒必须保留原Fork/Branch身份，不能把延时当作主路径直接结束。
        var parallelVersion = await PublishNodesAsync(client, formVersion, [
            Node("start", "start", new { nextNodeKeys = new[] { "fork" } }),
            Node("fork", "gateway.parallel", new { gatewayRoleKey = "fork", joinNodeKey = "join", branches = new[] {
                new { branchKey = "left", nextNodeKey = "delay" }, new { branchKey = "right", nextNodeKey = "empty" } } }),
            Node("delay", "timer.delay", new { nextNodeKeys = new[] { "join" }, delayPolicy = new { modeKey = "relative", timeZoneKey = "UTC", duration = 1, unitKey = "second" } }),
            Node("empty", "control.empty", new { nextNodeKeys = new[] { "join" } }),
            Node("join", "gateway.parallel", new { gatewayRoleKey = "join", forkNodeKey = "fork", nextNodeKeys = new[] { "end" } }), Node("end", "end", new { })]);
        var parallelId = (await PostAsync(client, "/api/v1/workflow/instances", Request(parallelVersion), HttpStatusCode.Created)).GetProperty("id").GetGuid();
        Assert.AreEqual("active", await db.ExecuteScalarAsync<string>("SELECT StatusKey FROM fn_workflow_instance WHERE Id=@parallelId", new { parallelId }));
        clock.UtcNow = clock.UtcNow.AddSeconds(2); Assert.AreEqual(1, await ProcessAsync(factory)); Assert.AreEqual(1, await WorkAsync(factory));
        Assert.AreEqual("completed", await db.ExecuteScalarAsync<string>("SELECT StatusKey FROM fn_workflow_instance WHERE Id=@parallelId", new { parallelId }));
        Assert.AreEqual(1, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_delay WHERE InstanceId=@parallelId AND ParallelJoinId IS NOT NULL AND ParallelBranchKey='left'", new { parallelId }));

        // 正式租户上下文启动后由后台扫描推进；Host不能读取或取消该租户实例。
        // 前面的时间推进只测试已关闭实例；新登录回到真实时刻，避免伪造未来JWT的NotBefore。
        clock.UtcNow = DateTimeOffset.UtcNow;
        using var tenantClient = factory.CreateClientForHost("localhost"); tenantClient.DefaultRequestHeaders.Add("Origin", "http://localhost");
        var login = await PostAsync(tenantClient, "/api/v1/auth/login", new LoginRequest("admin", FullNetApiFactory.TestPassword));
        tenantClient.DefaultRequestHeaders.Authorization = new("Bearer", login.GetProperty("accessToken").GetString());
        var tenants = await tenantClient.GetFromJsonAsync<TenantContextSummary[]>("/api/v1/tenancy/available"); var tenant = tenants!.Single(item => item.Identifier == "acme");
        tenantClient.DefaultRequestHeaders.Authorization = new("Bearer", await IntegrationTestTenantContextHelper.SwitchToTenantAsync(tenantClient, login.GetProperty("accessToken").GetString()!, tenant.Id));
        var tenantForm = await PostAsync(tenantClient, "/api/v1/workflow/forms", new { formKey = "tenant.delay." + Guid.NewGuid().ToString("N"), draft = new {
            schemaVersion = 1, adapterVersion = 1, sections = new[] { new { sectionKey = "main", fields = new[] { new { fieldKey = "flag", fieldTypeKey = "switch", required = false, constraints = new { } } } } } } }, HttpStatusCode.Created);
        var tenantFormVersion = (await PostAsync(tenantClient, $"/api/v1/workflow/forms/{tenantForm.GetProperty("id").GetGuid():D}/publish", new { expectedRevision = 1 })).GetProperty("id").GetGuid();
        var tenantVersion = await PublishAsync(tenantClient, tenantFormVersion, new { modeKey = "relative", timeZoneKey = "Asia/Shanghai", duration = 1, unitKey = "second" });
        var tenantInstance = (await PostAsync(tenantClient, "/api/v1/workflow/instances", new { definitionVersionId = tenantVersion, businessType = "tenant.delay", businessId = "one", initialValues = new { flag = true }, idempotencyKey = "tenant-start" }, HttpStatusCode.Created)).GetProperty("id").GetGuid();
        using (var foreign = await client.GetAsync($"/api/v1/workflow/instances/{tenantInstance:D}")) Assert.AreEqual(HttpStatusCode.NotFound, foreign.StatusCode);
        await PostAsync(client, $"/api/v1/workflow/instances/{tenantInstance:D}/cancel", new { expectedRevision = 1, reason = "forbidden", idempotencyKey = "foreign" }, HttpStatusCode.NotFound);
        clock.UtcNow = clock.UtcNow.AddSeconds(2); Assert.AreEqual(1, await ProcessAsync(factory)); Assert.AreEqual(1, await WorkAsync(factory));
        Assert.AreEqual("completed", await db.ExecuteScalarAsync<string>("SELECT StatusKey FROM fn_workflow_instance WHERE Id=@tenantInstance AND TenantId=@tenantId", new { tenantInstance, tenantId = tenant.Id }));

        // 已有数据上模拟表已建、索引未建、DbUp未记账；重复重跑保留每次激活。
        var count = await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_delay");
        await db.ExecuteAsync("DROP INDEX ix_fn_workflow_delay_due ON fn_workflow_delay");
        if (provider == DatabaseProvider.SqlServer)
        {
            // 恢复旧精度且索引/约束已存在的中间态，验证依赖对象先解除再按当前结构收敛。
            await db.ExecuteAsync("ALTER TABLE dbo.fn_workflow_delay DROP CONSTRAINT ck_fn_workflow_delay_pause");
            await db.ExecuteAsync("ALTER TABLE dbo.fn_workflow_delay ALTER COLUMN DueAtUtc datetimeoffset(7) NOT NULL");
            await db.ExecuteAsync("ALTER TABLE dbo.fn_workflow_delay ALTER COLUMN PausedAtUtc datetimeoffset(7) NULL");
            await db.ExecuteAsync("ALTER TABLE dbo.fn_workflow_delay ADD CONSTRAINT ck_fn_workflow_delay_pause CHECK ((PausedAtUtc IS NULL AND RemainingMilliseconds IS NULL) OR (ModeKey='relative' AND PausedAtUtc IS NOT NULL AND RemainingMilliseconds BETWEEN 0 AND 31622400000))");
            await db.ExecuteAsync("CREATE INDEX ix_fn_workflow_delay_due ON dbo.fn_workflow_delay(StatusKey,DueAtUtc,Id) INCLUDE(InstanceId,PausedAtUtc)");
        }
        await db.ExecuteAsync(provider == DatabaseProvider.SqlServer ? "DELETE FROM dbo.SchemaVersions WHERE ScriptName LIKE '%310_WorkflowDurableDelay.sql'"
            : "DELETE FROM schemaversions WHERE ScriptName LIKE '%310_WorkflowDurableDelay.sql'");
        await WorkflowHumanMigrationRecoveryTests.MigrateAsync(provider, connection); await WorkflowHumanMigrationRecoveryTests.MigrateAsync(provider, connection);
        Assert.AreEqual(count, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_delay"));
        if (provider == DatabaseProvider.SqlServer) Assert.AreEqual(2, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM sys.columns WHERE object_id=OBJECT_ID('fn_workflow_delay') AND name IN ('DueAtUtc','PausedAtUtc') AND system_type_id=42 AND scale=6"));
        Assert.AreEqual(1, await db.ExecuteScalarAsync<int>(provider == DatabaseProvider.SqlServer
            ? "SELECT COUNT(*) FROM sys.indexes WHERE object_id=OBJECT_ID('fn_workflow_delay') AND name='ix_fn_workflow_delay_due'"
            : "SELECT COUNT(*) FROM information_schema.STATISTICS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_delay' AND INDEX_NAME='ix_fn_workflow_delay_due' AND SEQ_IN_INDEX=1"));
        if (provider == DatabaseProvider.MySql) Assert.AreEqual(0, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM information_schema.ROUTINES WHERE ROUTINE_SCHEMA=DATABASE() AND ROUTINE_NAME='fn_workflow_ensure_delay'"));
    }

    private static async Task<Guid> PublishAsync(HttpClient client, Guid form, object policy)
    {
        return await PublishNodesAsync(client, form, [
                Node("start", "start", new { nextNodeKeys = new[] { "delay" } }),
                Node("delay", "timer.delay", new { nextNodeKeys = new[] { "route" }, delayPolicy = policy }),
                Node("route", "gateway.exclusive", new { nextNodeKeys = new[] { "yes", "no" }, branches = new[] { new { branchKey = "yes", nextNodeKey = "yes", condition = new { fieldKey = "flag", @operator = "equals", value = true } } }, defaultNextNodeKey = "no" }),
                Node("yes", "control.empty", new { nextNodeKeys = new[] { "end" } }), Node("no", "control.empty", new { nextNodeKeys = new[] { "end" } }), Node("end", "end", new { })]);
    }
    internal static async Task<Guid> PublishNodesAsync(HttpClient client, Guid form, object[] nodes)
    {
        var definition = await PostAsync(client, "/api/v1/workflow/definitions", new { definitionKey = "delay." + Guid.NewGuid().ToString("N"),
            draft = new { schemaVersion = 1, workflowKindKey = "automation", nodes } }, HttpStatusCode.Created);
        return (await PostAsync(client, $"/api/v1/workflow/definitions/{definition.GetProperty("id").GetGuid():D}/publish", new { expectedRevision = 1, formVersionId = form })).GetProperty("id").GetGuid();
    }
    private static object Node(string key, string type, object config) => new { nodeKey = key, nodeTypeKey = type, nodeSchemaVersion = 1, config };
    private static async Task<DateTimeOffset> ReadDueAsync(DbConnection db, Guid id)
    {
        var value = await db.ExecuteScalarAsync<object>("SELECT DueAtUtc FROM fn_workflow_delay WHERE InstanceId=@id", new { id });
        return value switch
        {
            DateTimeOffset offset => offset.ToUniversalTime(),
            DateTime date => new DateTimeOffset(DateTime.SpecifyKind(date, DateTimeKind.Utc)),
            _ => throw new InvalidOperationException("The persisted workflow delay deadline was not found."),
        };
    }
    private static async Task<int> ProcessAsync(FullNetApiFactory factory)
    { await using var scope = factory.Services.CreateAsyncScope(); return await scope.ServiceProvider.GetRequiredService<WorkflowDelayProcessor>().ProcessPendingAsync(default); }
    private static async Task<int> WorkAsync(FullNetApiFactory factory)
    { await using var scope = factory.Services.CreateAsyncScope(); return await scope.ServiceProvider.GetRequiredService<WorkflowActivityWorkProcessor>().ProcessPendingAsync(default); }
    internal static async Task<JsonElement> PostAsync(HttpClient client, string path, object body, HttpStatusCode expected = HttpStatusCode.OK)
    {
        using var response = await client.PostAsJsonAsync(path, body); var text = await response.Content.ReadAsStringAsync(); Assert.AreEqual(expected, response.StatusCode, path + " " + text);
        using var document = JsonDocument.Parse(text); return document.RootElement.Clone();
    }
    private sealed class TestClock : IClock { public DateTimeOffset UtcNow { get; set; } }
}
