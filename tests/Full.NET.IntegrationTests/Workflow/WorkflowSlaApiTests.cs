using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Dapper;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.IntegrationTests.Migrations;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Tenancy.Contracts;
using Full.NET.Modules.Workflow.Contracts;
using Full.NET.Modules.Workflow.Execution;
using Full.NET.Modules.Workflow.Features.ManageInstances;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MySqlConnector;
using static Full.NET.IntegrationTests.Workflow.WorkflowDurableDelayApiTests;

namespace Full.NET.IntegrationTests.Workflow;

/// <summary>真实双库验证系统截止不伪造个人票、固定时钟、并发和迁移恢复。</summary>
[TestClass]
public sealed class WorkflowSlaApiTests
{
    [TestMethod]
    public async Task Sla_with_sql_server() => await VerifyAsync(DatabaseProvider.SqlServer, await SharedDatabaseFixture.CreateSqlServerDatabaseAsync());
    [TestMethod]
    public async Task Sla_with_mysql() => await VerifyAsync(DatabaseProvider.MySql, await SharedDatabaseFixture.CreateMySqlDatabaseAsync());
    private static async Task VerifyAsync(DatabaseProvider provider, string connection)
    {
        var clock = new SlaClock();
        using var factory = new FullNetApiFactory(provider, connection, configureTestServices: services => {
            // 只推进流程时间；身份认证、会话失效和正式租户授权保留真实宿主时钟。
            services.Replace(ServiceDescriptor.Scoped<WorkflowInstanceManagementService>(scope => ActivatorUtilities.CreateInstance<WorkflowInstanceManagementService>(scope, clock)));
            services.Replace(ServiceDescriptor.Scoped<WorkflowSlaProcessor>(scope => ActivatorUtilities.CreateInstance<WorkflowSlaProcessor>(scope, clock)));
            services.Replace(ServiceDescriptor.Scoped<WorkflowActivityWorkProcessor>(scope => ActivatorUtilities.CreateInstance<WorkflowActivityWorkProcessor>(scope, clock)));
        });
        await factory.InitializeAsync(useSchemaTemplate: false);
        await using DbConnection db = provider == DatabaseProvider.SqlServer ? new SqlConnection(connection) : new MySqlConnection(Migration114To126RecoveryTestSupport.MySqlQueryConnectionString(connection));
        using var client = factory.CreateClientForHost("localhost");
        var identity = await factory.CreateHostIdentityAsync("sla." + Guid.NewGuid().ToString("N"), [WorkflowPermissions.FormsCreate, WorkflowPermissions.FormsPublish,
            WorkflowPermissions.DefinitionsCreate, WorkflowPermissions.DefinitionsPublish, WorkflowPermissions.InstancesStart, WorkflowPermissions.InstancesRead,
            WorkflowPermissions.InstancesPause, WorkflowPermissions.InstancesResume, WorkflowPermissions.InstancesCancel]);
        client.DefaultRequestHeaders.Authorization = new("Bearer", identity.AccessToken);
        var second = await factory.CreateHostIdentityAsync("sla.second." + Guid.NewGuid().ToString("N"), []);
        var form = await PostAsync(client, "/api/v1/workflow/forms", new { formKey = "sla." + Guid.NewGuid().ToString("N"), draft = new { schemaVersion = 1, adapterVersion = 1,
            sections = new[] { new { sectionKey = "main", fields = new[] { new { fieldKey = "flag", fieldTypeKey = "switch", required = false, constraints = new { } } } } } } }, HttpStatusCode.Created);
        var formId = (await PostAsync(client, $"/api/v1/workflow/forms/{form.GetProperty("id").GetGuid():D}/publish", new { expectedRevision = 1 })).GetProperty("id").GetGuid();
        async Task<Guid> Start(string action, string pause = "elapsed", bool grouped = false) {
            object activity = grouped ? new { nextNodeKeys = new[] { "end" }, groupApprovalPolicy = new { modeKey = "all" },
                groups = new[] { new { groupKey = "first", assigneePolicy = new { sources = new[] { new { resolverKindKey = "specified_users", userIds = new[] { identity.UserId } } } } },
                    new { groupKey = "second", assigneePolicy = new { sources = new[] { new { resolverKindKey = "specified_users", userIds = new[] { second.UserId } } } } } },
                slaPolicy = new { dueAfterMinutes = 10, pauseModeKey = pause, actionKey = action } }
                : new { nextNodeKeys = new[] { "end" }, assigneePolicy = new { sources = new[] { new { resolverKindKey = "specified_users", userIds = new[] { identity.UserId } } } },
                    slaPolicy = new { dueAfterMinutes = 10, pauseModeKey = pause, actionKey = action } };
            var version = await PublishNodesAsync(client, formId, [Node("start", "start", new { nextNodeKeys = new[] { "approval" } }),
                Node("approval", grouped ? "human.group_approval" : "human.approval", activity), Node("end", "end", new { })]);
            return (await PostAsync(client, "/api/v1/workflow/instances", new { definitionVersionId = version, businessType = "sla.test", businessId = Guid.NewGuid().ToString("N"),
                initialValues = new { flag = true }, idempotencyKey = Guid.NewGuid().ToString("N") }, HttpStatusCode.Created)).GetProperty("id").GetGuid();
        }
        async Task<int> Process() { await using var scope = factory.Services.CreateAsyncScope(); return await scope.ServiceProvider.GetRequiredService<WorkflowSlaProcessor>().ProcessPendingAsync(default); }
        async Task Work() { await using var scope = factory.Services.CreateAsyncScope(); await scope.ServiceProvider.GetRequiredService<WorkflowActivityWorkProcessor>().ProcessPendingAsync(default); }
        async Task<string?> Status(Guid id) => await db.ExecuteScalarAsync<string>("SELECT StatusKey FROM fn_workflow_instance WHERE Id=@id", new { id });
        async Task<long> Revision(Guid id) => await db.ExecuteScalarAsync<long>("SELECT Revision FROM fn_workflow_instance WHERE Id=@id", new { id });
        async Task Control(Guid id, string action) => await PostAsync(client, $"/api/v1/workflow/instances/{id:D}/{action}", new { expectedRevision = await Revision(id), idempotencyKey = Guid.NewGuid().ToString("N") });
        async Task<DateTimeOffset> Due(Guid id) => new(DateTime.SpecifyKind(await db.ExecuteScalarAsync<DateTime>("SELECT DueAtUtc FROM fn_workflow_sla WHERE InstanceId=@id", new { id }), DateTimeKind.Utc));

        clock.UtcNow = new DateTimeOffset(DateTime.UtcNow.Ticks / 10 * 10, TimeSpan.Zero); var pass = await Start("auto_pass"); Assert.AreEqual(0, await Process());
        clock.UtcNow = clock.UtcNow.AddMinutes(11); var competing = await Task.WhenAll(Process(), Process()); Assert.AreEqual(1, competing.Sum());
        await Work(); Assert.AreEqual("completed", await Status(pass)); Assert.AreEqual(0, await Process());
        Assert.AreEqual(0, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_approval_slot WHERE InstanceId=@pass AND DecisionKey='approve'", new { pass }));
        Assert.AreEqual(0, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_action_record WHERE InstanceId=@pass AND ActionKey='approve'", new { pass }));
        Assert.AreEqual(1, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_execution_log WHERE InstanceId=@pass AND TransitionKey='activity.sla.auto_pass'", new { pass }));
        Assert.AreEqual(1, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_sla WHERE InstanceId=@pass AND StatusKey='fired'", new { pass }));
        var grouped = await Start("auto_pass", grouped: true); clock.UtcNow = clock.UtcNow.AddMinutes(11); Assert.AreEqual(1, await Process()); await Work();
        Assert.AreEqual("completed", await Status(grouped));
        Assert.AreEqual(2, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_approval_group WHERE InstanceId=@grouped AND StatusKey='cancelled'", new { grouped }));
        Assert.AreEqual(0, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_approval_slot WHERE InstanceId=@grouped AND DecisionKey='approve'", new { grouped }));

        var reject = await Start("auto_reject"); clock.UtcNow = clock.UtcNow.AddMinutes(11); Assert.AreEqual(1, await Process()); Assert.AreEqual("rejected", await Status(reject));
        Assert.AreEqual(0, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_todo WHERE InstanceId=@reject AND StatusKey='active'", new { reject }));
        var suspend = await Start("suspend"); clock.UtcNow = clock.UtcNow.AddMinutes(11); Assert.AreEqual(1, await Process()); Assert.AreEqual("suspended", await Status(suspend));
        await Control(suspend, "resume"); Assert.AreEqual(0, await Process()); Assert.AreEqual("active", await Status(suspend));

        var frozen = await Start("suspend", "freeze"); var originalDue = await Due(frozen); clock.UtcNow = clock.UtcNow.AddMinutes(4); await Control(frozen, "pause");
        var remaining = await db.ExecuteScalarAsync<long>("SELECT RemainingMilliseconds FROM fn_workflow_sla WHERE InstanceId=@frozen", new { frozen }); Assert.AreEqual(360000L, remaining);
        clock.UtcNow = clock.UtcNow.AddDays(1); Assert.AreEqual(0, await Process()); await Control(frozen, "resume");
        Assert.AreEqual(clock.UtcNow.AddMinutes(6).ToUnixTimeMilliseconds(), (await Due(frozen)).ToUnixTimeMilliseconds()); Assert.IsTrue(await Due(frozen) > originalDue);
        clock.UtcNow = clock.UtcNow.AddMinutes(7); Assert.AreEqual(1, await Process()); Assert.AreEqual("suspended", await Status(frozen));
        var elapsed = await Start("suspend", "elapsed"); originalDue = await Due(elapsed); await Control(elapsed, "pause");
        clock.UtcNow = clock.UtcNow.AddMinutes(11); Assert.AreEqual(0, await Process()); await Control(elapsed, "resume"); Assert.AreEqual(originalDue, await Due(elapsed));
        Assert.AreEqual(1, await Process()); Assert.AreEqual("suspended", await Status(elapsed));

        var invalid = await Start("suspend");
        await db.ExecuteAsync("UPDATE fn_workflow_sla SET ActionKey='auto_reject' WHERE InstanceId=@invalid", new { invalid });
        clock.UtcNow = clock.UtcNow.AddMinutes(11); Assert.AreEqual(1, await Process()); Assert.AreEqual("suspended", await Status(invalid));
        Assert.AreEqual(1, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_execution_log WHERE InstanceId=@invalid AND Summary='workflow.sla.input_invalid'", new { invalid }));
        Assert.AreEqual(0, await Process());
        var cancelled = await Start("auto_pass");
        await PostAsync(client, $"/api/v1/workflow/instances/{cancelled:D}/cancel", new { expectedRevision = await Revision(cancelled), reason = "cancel before SLA", idempotencyKey = Guid.NewGuid().ToString("N") });
        clock.UtcNow = clock.UtcNow.AddMinutes(11); Assert.AreEqual(0, await Process()); Assert.AreEqual("cancelled", await Status(cancelled));

        using var tenantClient = factory.CreateClientForHost("localhost"); tenantClient.DefaultRequestHeaders.Add("Origin", "http://localhost");
        var login = await PostAsync(tenantClient, "/api/v1/auth/login", new LoginRequest("admin", FullNetApiFactory.TestPassword));
        tenantClient.DefaultRequestHeaders.Authorization = new("Bearer", login.GetProperty("accessToken").GetString());
        var available = await tenantClient.GetFromJsonAsync<TenantContextSummary[]>("/api/v1/tenancy/available"); var tenant = available!.Single(item => item.Identifier == "acme");
        tenantClient.DefaultRequestHeaders.Authorization = new("Bearer", await IntegrationTestTenantContextHelper.SwitchToTenantAsync(tenantClient, login.GetProperty("accessToken").GetString()!, tenant.Id));
        var me = await tenantClient.GetFromJsonAsync<JsonElement>("/api/v1/me");
        var tenantForm = await PostAsync(tenantClient, "/api/v1/workflow/forms", new { formKey = "sla.tenant." + Guid.NewGuid().ToString("N"), draft = new { schemaVersion = 1, adapterVersion = 1,
            sections = new[] { new { sectionKey = "main", fields = new[] { new { fieldKey = "flag", fieldTypeKey = "switch", required = false, constraints = new { } } } } } } }, HttpStatusCode.Created);
        var tenantFormId = (await PostAsync(tenantClient, $"/api/v1/workflow/forms/{tenantForm.GetProperty("id").GetGuid():D}/publish", new { expectedRevision = 1 })).GetProperty("id").GetGuid();
        var tenantVersion = await PublishNodesAsync(tenantClient, tenantFormId, [Node("start", "start", new { nextNodeKeys = new[] { "approval" } }),
            Node("approval", "human.approval", new { nextNodeKeys = new[] { "end" }, assigneePolicy = new { sources = new[] { new { resolverKindKey = "specified_users", userIds = new[] { me.GetProperty("id").GetGuid() } } } },
                slaPolicy = new { dueAfterMinutes = 10, pauseModeKey = "elapsed", actionKey = "auto_pass" } }), Node("end", "end", new { })]);
        var tenantInstance = (await PostAsync(tenantClient, "/api/v1/workflow/instances", new { definitionVersionId = tenantVersion, businessType = "sla.tenant", businessId = Guid.NewGuid().ToString("N"),
            initialValues = new { flag = true }, idempotencyKey = Guid.NewGuid().ToString("N") }, HttpStatusCode.Created)).GetProperty("id").GetGuid();
        using (var foreign = await client.GetAsync($"/api/v1/workflow/instances/{tenantInstance:D}")) Assert.AreEqual(HttpStatusCode.NotFound, foreign.StatusCode);
        clock.UtcNow = clock.UtcNow.AddMinutes(11); Assert.AreEqual(1, await Process()); await Work(); Assert.AreEqual("completed", await Status(tenantInstance));

        // 同一截止时间跨满页推进，真实双库都必须接受非空复合游标并在尾页回绕。
        var page = new List<Guid>(); for (var index = 0; index < 11; index++) page.Add(await Start("suspend"));
        clock.UtcNow = clock.UtcNow.AddMinutes(11);
        Assert.AreEqual(10, await Process()); Assert.AreEqual(1, await Process()); Assert.AreEqual(0, await Process());
        foreach (var item in page) Assert.AreEqual("suspended", await Status(item));

        var count = await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_sla");
        await db.ExecuteAsync(provider == DatabaseProvider.SqlServer ? "DROP INDEX ix_fn_workflow_sla_due ON dbo.fn_workflow_sla; ALTER TABLE dbo.fn_workflow_sla DROP CONSTRAINT fk_fn_workflow_sla_step; DELETE FROM dbo.SchemaVersions WHERE ScriptName LIKE '%315_WorkflowActivitySla.sql'"
            : "ALTER TABLE fn_workflow_sla DROP INDEX ix_fn_workflow_sla_due; ALTER TABLE fn_workflow_sla DROP FOREIGN KEY fk_fn_workflow_sla_step; DELETE FROM schemaversions WHERE ScriptName LIKE '%315_WorkflowActivitySla.sql'");
        await WorkflowHumanMigrationRecoveryTests.MigrateAsync(provider, connection); await WorkflowHumanMigrationRecoveryTests.MigrateAsync(provider, connection);
        Assert.AreEqual(count, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_sla"));
    }
    private static object Node(string key, string kind, object config) => new { nodeKey = key, nodeTypeKey = kind, nodeSchemaVersion = 1, config };
    private sealed class SlaClock : IClock { public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.UtcNow; }
}
