using System.Data.Common;
using System.Net;
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
using static Full.NET.IntegrationTests.Workflow.WorkflowDurableDelayApiTests;

namespace Full.NET.IntegrationTests.Workflow;

/// <summary>真实父子截止、暂停冻结、静态跳转及成功封口使用相同双库场景。</summary>
[TestClass]
public sealed class WorkflowSubprocessTimeoutApiTests
{
    [TestMethod]
    public async Task Subprocess_timeouts_with_sql_server() => await VerifyAsync(DatabaseProvider.SqlServer, await SharedDatabaseFixture.CreateSqlServerDatabaseAsync());
    [TestMethod]
    public async Task Subprocess_timeouts_with_mysql() => await VerifyAsync(DatabaseProvider.MySql, await SharedDatabaseFixture.CreateMySqlDatabaseAsync());

    private static async Task VerifyAsync(DatabaseProvider provider, string connection)
    {
        using var factory = new FullNetApiFactory(provider, connection); await factory.InitializeAsync(useSchemaTemplate: false);
        var owner = await factory.CreateHostIdentityAsync("starter.owner." + Guid.NewGuid().ToString("N"), [WorkflowPermissions.FormsCreate, WorkflowPermissions.FormsPublish,
            WorkflowPermissions.DefinitionsCreate, WorkflowPermissions.DefinitionsPublish, WorkflowPermissions.DefinitionsRead, WorkflowPermissions.InstancesStart,
            WorkflowPermissions.InstancesRead, WorkflowPermissions.InstancesCancel, WorkflowPermissions.InstancesPause, WorkflowPermissions.InstancesResume, WorkflowPermissions.TodosRead, WorkflowPermissions.TodosApprove, "tenancy.tenants.switch"]);
        var first = await factory.CreateHostIdentityAsync("starter.first." + Guid.NewGuid().ToString("N"), [WorkflowPermissions.TodosRead, WorkflowPermissions.TodosApprove, "tenancy.tenants.switch"]);
        var second = await factory.CreateHostIdentityAsync("starter.second." + Guid.NewGuid().ToString("N"), [WorkflowPermissions.TodosRead, WorkflowPermissions.TodosApprove, "tenancy.tenants.switch"]);
        var foreign = await factory.CreateHostIdentityAsync("starter.foreign." + Guid.NewGuid().ToString("N"), [WorkflowPermissions.TodosRead]);
        using var ownerClient = factory.CreateClientForHost("localhost"); ownerClient.DefaultRequestHeaders.Authorization = new("Bearer", owner.AccessToken);
        using var firstClient = factory.CreateClientForHost("localhost"); firstClient.DefaultRequestHeaders.Authorization = new("Bearer", first.AccessToken);
        using var secondClient = factory.CreateClientForHost("localhost"); secondClient.DefaultRequestHeaders.Authorization = new("Bearer", second.AccessToken);
        await using DbConnection db = provider == DatabaseProvider.SqlServer ? new SqlConnection(connection) : new MySqlConnection(Migration114To126RecoveryTestSupport.MySqlQueryConnectionString(connection));
        await VerifyScopeAsync(factory, db, ownerClient, new Dictionary<Guid, HttpClient> { [owner.UserId] = ownerClient, [first.UserId] = firstClient, [second.UserId] = secondClient }, owner.UserId, first.UserId, second.UserId, null, null);
        using var probe = factory.CreateClientForHost("acme.localhost"); var tenant = await IntegrationTestTenantContextHelper.GetCurrentTenantAsync(probe);
        foreach (var user in new[] { owner.UserId, first.UserId, second.UserId })
        {
            var hostRole = await db.ExecuteScalarAsync<Guid>("SELECT link.RoleId FROM fn_identity_user_role AS link INNER JOIN fn_identity_role AS role ON role.Id=link.RoleId WHERE link.UserId=@user AND role.TenantId IS NULL", new { user });
            var role = Guid.CreateVersion7();
            await db.ExecuteAsync("INSERT INTO fn_identity_tenant_member (Id,TenantId,UserId,MemberRole,Status,CreatedAtUtc,UpdatedAtUtc,Version) VALUES (@id,@tenant,@user,'Member','Active',@now,@now,1)", new { id = Guid.CreateVersion7(), tenant = tenant.Id, user, now = DateTimeOffset.UtcNow });
            await db.ExecuteAsync("""
                INSERT INTO fn_identity_role (Id,TenantId,ScopeKey,Code,Name,IsSystem,IsActive,IsSuperAdministrator,DataScopeKind,CreatedAtUtc,Version)
                SELECT @role,@tenant,@scope,@code,Name,IsSystem,IsActive,IsSuperAdministrator,DataScopeKind,CreatedAtUtc,Version FROM fn_identity_role WHERE Id=@hostRole
                """, new { role, tenant = tenant.Id, scope = $"tenant:{tenant.Id:N}", code = "starter-" + role.ToString("N"), hostRole });
            await db.ExecuteAsync("INSERT INTO fn_identity_role_permission (RoleId,PermissionCode) SELECT @role,PermissionCode FROM fn_identity_role_permission WHERE RoleId=@hostRole", new { role, hostRole });
            await db.ExecuteAsync("INSERT INTO fn_identity_user_role (UserId,RoleId) VALUES (@user,@role)", new { user, role });
        }
        using var tenantOwner = factory.CreateClientForHost("localhost"); tenantOwner.DefaultRequestHeaders.Authorization = new("Bearer", await IntegrationTestTenantContextHelper.SwitchToTenantAsync(ownerClient, owner.AccessToken, tenant.Id));
        using var tenantFirst = factory.CreateClientForHost("localhost"); tenantFirst.DefaultRequestHeaders.Authorization = new("Bearer", await IntegrationTestTenantContextHelper.SwitchToTenantAsync(firstClient, first.AccessToken, tenant.Id));
        using var tenantSecond = factory.CreateClientForHost("localhost"); tenantSecond.DefaultRequestHeaders.Authorization = new("Bearer", await IntegrationTestTenantContextHelper.SwitchToTenantAsync(secondClient, second.AccessToken, tenant.Id));
        await VerifyScopeAsync(factory, db, tenantOwner, new Dictionary<Guid, HttpClient> { [owner.UserId] = tenantOwner, [first.UserId] = tenantFirst, [second.UserId] = tenantSecond }, owner.UserId, first.UserId, second.UserId, foreign.UserId, tenant.Id);
        var count = await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_subprocess_timeout");
        await db.ExecuteAsync(provider == DatabaseProvider.SqlServer ? "ALTER TABLE fn_workflow_subprocess_timeout DROP CONSTRAINT ck_fn_workflow_subprocess_timeout_state" : "ALTER TABLE fn_workflow_subprocess_timeout DROP CHECK ck_fn_workflow_subprocess_timeout_state");
        await db.ExecuteAsync(provider == DatabaseProvider.SqlServer ? "DELETE FROM dbo.SchemaVersions WHERE ScriptName LIKE '%322_WorkflowSubprocessTimeouts.sql'" : "DELETE FROM schemaversions WHERE ScriptName LIKE '%322_WorkflowSubprocessTimeouts.sql'");
        await WorkflowHumanMigrationRecoveryTests.MigrateAsync(provider, connection); await WorkflowHumanMigrationRecoveryTests.MigrateAsync(provider, connection);
        Assert.AreEqual(count, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_subprocess_timeout"));
        await Assert.ThrowsAsync<DbException>(async () => await db.ExecuteAsync("UPDATE fn_workflow_subprocess_timeout SET StatusKey='invalid'"));
        if (provider == DatabaseProvider.MySql) Assert.AreEqual(0, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM information_schema.ROUTINES WHERE ROUTINE_SCHEMA=DATABASE() AND ROUTINE_NAME='fn_workflow_subprocess_timeout_ensure'"));
    }


    private static async Task VerifyScopeAsync(FullNetApiFactory factory, DbConnection db, HttpClient owner, IReadOnlyDictionary<Guid, HttpClient> actors, Guid ownerId, Guid first, Guid second, Guid? foreign, Guid? tenantId)
    {
        object Node(string key, string kind, object config) => new { nodeKey = key, nodeTypeKey = kind, nodeSchemaVersion = 1, config };
        var created = await PostAsync(owner, "/api/v1/workflow/forms", new { formKey = "timeout." + Guid.NewGuid().ToString("N"), draft = new {
            schemaVersion = 1, adapterVersion = 1, sections = new[] { new { sectionKey = "main", fields = new[] { new { fieldKey = "note", fieldTypeKey = "text", required = false, constraints = new { } } } } } } }, HttpStatusCode.Created);
        var form = (await PostAsync(owner, $"/api/v1/workflow/forms/{created.GetProperty("id").GetGuid():D}/publish", new { expectedRevision = 1 })).GetProperty("id").GetGuid();
        object Approval(string next) => new { nextNodeKeys = new[] { next }, fieldPolicies = new { note = "editable" }, assigneePolicy = new { sources = new[] { new { resolverKindKey = "initiator" } } } };
        var child = await PublishNodesAsync(owner, form, [Node("start", "start", new { nextNodeKeys = new[] { "approve" } }), Node("approve", "human.approval", Approval("end")), Node("end", "end", new { })]);
        async Task<Guid> Version(string pause = "freeze", bool multi = false, bool route = false)
        {
            var policy = new Dictionary<string, object> { ["dueAfterMinutes"] = 60, ["pauseModeKey"] = pause, ["actionKey"] = route ? "route" : "continue" };
            if (route) policy["nextNodeKey"] = "fallback";
            var config = new Dictionary<string, object> { ["nextNodeKeys"] = new[] { route ? "normal" : "end" }, ["fieldPolicies"] = new { note = "editable" },
                ["subprocessPolicy"] = new { definitionVersionId = child, inputFields = new { }, outputFields = new { note = "note" } }, ["childTimeoutPolicy"] = policy };
            if (multi) config["multiInstancePolicy"] = new { modeKey = "parallel", source = new { kindKey = "fixed_count", count = 2 }, requiredPercentage = 100, emptyModeKey = "reject", outputReducers = new { note = "first" } };
            var nodes = new List<object> { Node("start", "start", new { nextNodeKeys = new[] { "child" } }), Node("child", "process.subprocess", config), Node("end", "end", new { }) };
            if (route) { nodes.Add(Node("normal", "human.approval", Approval("fallback"))); nodes.Add(Node("fallback", "human.approval", Approval("end"))); }
            return await PublishNodesAsync(owner, form, nodes.ToArray());
        }
        async Task<Guid> Start(Guid version) => (await PostAsync(owner, "/api/v1/workflow/instances", new { definitionVersionId = version, businessType = "timeout.test", businessId = Guid.NewGuid().ToString("N"), initialValues = new { note = "before" }, idempotencyKey = Guid.NewGuid().ToString("N") }, HttpStatusCode.Created)).GetProperty("id").GetGuid();
        async Task Tick() { await using var scope = factory.Services.CreateAsyncScope(); await scope.ServiceProvider.GetRequiredService<WorkflowSubprocessProcessor>().ProcessPendingAsync(default); await scope.ServiceProvider.GetRequiredService<WorkflowActivityWorkProcessor>().ProcessPendingAsync(default); }
        async Task Until(Func<Task<bool>> condition, string reason) { for (var i = 0; i < 64; i++) { if (await condition()) return; await Tick(); } Assert.Fail(reason); }
        async Task<string?> Status(Guid id) => await db.ExecuteScalarAsync<string>("SELECT StatusKey FROM fn_workflow_instance WHERE Id=@id", new { id });
        async Task<int> Children(Guid root) => await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_subprocess_item WHERE InstanceId=@root AND ChildInstanceId IS NOT NULL", new { root });
        async Task Expire(Guid root) => await db.ExecuteAsync("UPDATE fn_workflow_subprocess_timeout SET DueAtUtc=@past WHERE InstanceId=@root", new { root, past = DateTimeOffset.UtcNow.AddMinutes(-1) });
        async Task Action(Guid id, string action) => _ = await PostAsync(owner, $"/api/v1/workflow/instances/{id:D}/{action}", new { expectedRevision = await db.ExecuteScalarAsync<long>("SELECT Revision FROM fn_workflow_instance WHERE Id=@id", new { id }), reason = "截止验收", idempotencyKey = Guid.NewGuid().ToString("N") });
        async Task Approve(Guid instance)
        {
            var todo = await db.ExecuteScalarAsync<Guid>("SELECT Id FROM fn_workflow_todo WHERE InstanceId=@instance AND StatusKey='active'", new { instance });
            var detail = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/workflow/todos/{todo:D}");
            await PostAsync(owner, $"/api/v1/workflow/todos/{todo:D}/approve", new { expectedRevision = detail.GetProperty("revision").GetInt64(), expectedFormRevision = detail.GetProperty("submissionRevision").GetInt64(), fieldPatch = new { note = "accepted" }, idempotencyKey = Guid.NewGuid().ToString("N") });
        }
        async Task VerifyTimeout(Guid root)
        {
            await Expire(root); await Until(async () => await Status(root) == "completed", "超时没有在真实子项结束后继续");
            Assert.AreEqual(0, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_instance WHERE Id IN (SELECT ChildInstanceId FROM fn_workflow_subprocess_item WHERE InstanceId=@root) AND StatusKey IN ('active','suspended')", new { root }));
            var json = JsonSerializer.Deserialize<JsonElement>((await db.ExecuteScalarAsync<string>("SELECT SubmissionJson FROM fn_workflow_form_submission WHERE InstanceId=@root", new { root }))!);
            Assert.AreEqual("before", json.GetProperty("note").GetString());
            Assert.AreEqual(1, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_domain_audit WHERE InstanceId=@root AND DetailJson LIKE '%timeout_applied%' AND ActorUserId IS NULL", new { root }));
            await Tick(); Assert.AreEqual("applied", await db.ExecuteScalarAsync<string>("SELECT StatusKey FROM fn_workflow_subprocess_timeout WHERE InstanceId=@root", new { root }));
        }
        var single = await Version(); var early = await Start(single); await VerifyTimeout(early); Assert.AreEqual(0, await Children(early));
        var parallel = await Start(await Version(multi: true)); await Until(async () => await Children(parallel) == 2, "并行子项未启动"); await VerifyTimeout(parallel);
        foreach (var mode in new[] { "freeze", "elapsed" })
        {
            var paused = await Start(await Version(mode)); await Until(async () => await Children(paused) == 1, "暂停前子项未启动"); await Action(paused, "pause"); await Expire(paused); await Tick();
            Assert.AreEqual("suspended", await Status(paused)); Assert.AreEqual("waiting", await db.ExecuteScalarAsync<string>("SELECT StatusKey FROM fn_workflow_subprocess_timeout WHERE InstanceId=@paused", new { paused }));
            await Action(paused, "resume");
            if (mode == "freeze")
            {
                var persisted = await db.ExecuteScalarAsync<object>("SELECT DueAtUtc FROM fn_workflow_subprocess_timeout WHERE InstanceId=@paused", new { paused });
                var due = persisted is DateTimeOffset offset ? offset : new DateTimeOffset(DateTime.SpecifyKind((DateTime)persisted!, DateTimeKind.Utc));
                Assert.IsTrue(due > DateTimeOffset.UtcNow.AddMinutes(50));
            }
            await VerifyTimeout(paused);
        }
        var routed = await Start(await Version(route: true)); await Expire(routed);
        await Until(async () => await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_step WHERE InstanceId=@routed AND NodeKey='fallback' AND StatusKey='active'", new { routed }) == 1, "超时没有到达发布的静态目标");
        Assert.AreEqual(0, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_step WHERE InstanceId=@routed AND NodeKey='normal'", new { routed })); await Approve(routed);
        var success = await Start(single); await Until(async () => await Children(success) == 1, "成功子项未启动");
        var actualChild = await db.ExecuteScalarAsync<Guid>("SELECT ChildInstanceId FROM fn_workflow_subprocess_item WHERE InstanceId=@success", new { success }); await Approve(actualChild);
        await Until(async () => await db.ExecuteScalarAsync<string>("SELECT StatusKey FROM fn_workflow_subprocess_call WHERE InstanceId=@success", new { success }) == "result_ready", "子成功未接纳");
        await Expire(success); await Until(async () => await Status(success) == "completed", "已接纳成功被过期覆盖");
        Assert.IsTrue((await db.ExecuteScalarAsync<string>("SELECT SubmissionJson FROM fn_workflow_form_submission WHERE InstanceId=@success", new { success }))!.Contains("accepted", StringComparison.Ordinal));
        var uncertain = await Start(single); await Until(async () => await Children(uncertain) == 1, "未知结果子项未启动");
        var uncertainChild = await db.ExecuteScalarAsync<Guid>("SELECT ChildInstanceId FROM fn_workflow_subprocess_item WHERE InstanceId=@uncertain", new { uncertain });
        var effectStep = await db.ExecuteScalarAsync<Guid>("SELECT Id FROM fn_workflow_step WHERE InstanceId=@uncertainChild AND NodeKey='approve'", new { uncertainChild });
        // 在真实子根注入持久未知结果，验证父接纳边界；这里不宣称真实HTTP出站验收。
        await db.ExecuteAsync("""
            INSERT INTO fn_workflow_connector_call (Id,InstanceId,TenantScopeKey,NodeKey,ContinuationNodeKey,ConnectorKey,ConnectorVersion,ConnectorHash,
                ModeKey,InputJson,InputHash,InputFormRevision,RequestJson,RequestHash,StatusKey,LeaseGeneration,AttemptCount,ApplyAttemptCount,DueAtUtc,Revision,CreatedAtUtc)
            SELECT @effectStep,Id,TenantScopeKey,'approve','end','test.timeout.unknown',1,@hash,'sync','{}',@hash,1,'{}',@hash,'outcome_unknown',0,1,0,@now,1,@now
            FROM fn_workflow_instance WHERE Id=@uncertainChild
            """, new { effectStep, uncertainChild, hash = new string('a',64), now = DateTimeOffset.UtcNow });
        await Expire(uncertain); await Until(async () => await Status(uncertain) == "suspended", "取消没有阻断未知外部效果");
        Assert.AreEqual("closing", await db.ExecuteScalarAsync<string>("SELECT StatusKey FROM fn_workflow_subprocess_timeout WHERE InstanceId=@uncertain", new { uncertain }));
        await PostAsync(owner, $"/api/v1/workflow/instances/{uncertain:D}/resume", new { expectedRevision = await db.ExecuteScalarAsync<long>("SELECT Revision FROM fn_workflow_instance WHERE Id=@uncertain", new { uncertain }), reason = "拒绝盲恢复", idempotencyKey = Guid.NewGuid().ToString("N") }, HttpStatusCode.Conflict);
        await db.ExecuteAsync("UPDATE fn_workflow_connector_call SET StatusKey='cancelled',CompletedAtUtc=@now WHERE Id=@effectStep", new { effectStep, now = DateTimeOffset.UtcNow });
        await Until(async () => await db.ExecuteScalarAsync<string>("SELECT StatusKey FROM fn_workflow_subprocess_timeout WHERE InstanceId=@uncertain", new { uncertain }) == "ready", "明确对账后未恢复为可续办事实");
        Assert.AreEqual("suspended", await Status(uncertain)); await Action(uncertain, "resume");
        await Until(async () => await Status(uncertain) == "completed", "明确对账及恢复后没有继续");
        Assert.AreEqual(1, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_domain_audit WHERE InstanceId=@uncertain AND DetailJson LIKE '%timeout_blocked%' AND ActorUserId IS NULL", new { uncertain }));
        var cancelled = await Start(single); await Action(cancelled, "cancel"); await Until(async () => await db.ExecuteScalarAsync<string>("SELECT StatusKey FROM fn_workflow_subprocess_timeout WHERE InstanceId=@cancelled", new { cancelled }) == "cancelled", "父取消未关闭截止事实");
    }
}
