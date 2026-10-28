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

/// <summary>真实用户办理验证系统委托、冻结身份及成功集合回填；不能用伪造个人启动动作代替来源证据。</summary>
[TestClass]
public sealed class WorkflowSubprocessStarterApiTests
{
    [TestMethod]
    public async Task Subprocess_starters_with_sql_server() => await VerifyAsync(DatabaseProvider.SqlServer, await SharedDatabaseFixture.CreateSqlServerDatabaseAsync());
    [TestMethod]
    public async Task Subprocess_starters_with_mysql() => await VerifyAsync(DatabaseProvider.MySql, await SharedDatabaseFixture.CreateMySqlDatabaseAsync());

    private static async Task VerifyAsync(DatabaseProvider provider, string connection)
    {
        using var factory = new FullNetApiFactory(provider, connection); await factory.InitializeAsync(useSchemaTemplate: false);
        var owner = await factory.CreateHostIdentityAsync("starter.owner." + Guid.NewGuid().ToString("N"), [WorkflowPermissions.FormsCreate, WorkflowPermissions.FormsPublish,
            WorkflowPermissions.DefinitionsCreate, WorkflowPermissions.DefinitionsPublish, WorkflowPermissions.DefinitionsRead, WorkflowPermissions.InstancesStart,
            WorkflowPermissions.InstancesRead, WorkflowPermissions.InstancesCancel, WorkflowPermissions.TodosRead, WorkflowPermissions.TodosApprove, "tenancy.tenants.switch"]);
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
    }

    private static async Task VerifyScopeAsync(FullNetApiFactory factory, DbConnection db, HttpClient owner, IReadOnlyDictionary<Guid, HttpClient> actors, Guid ownerId, Guid first, Guid second, Guid? foreign, Guid? tenantId)
    {
        object Node(string key, string kind, object config) => new { nodeKey = key, nodeTypeKey = kind, nodeSchemaVersion = 1, config };
        async Task<Guid> Form(object[] fields)
        {
            var created = await PostAsync(owner, "/api/v1/workflow/forms", new { formKey = "starter." + Guid.NewGuid().ToString("N"), draft = new {
                schemaVersion = 1, adapterVersion = 1, sections = new[] { new { sectionKey = "main", fields } } } }, HttpStatusCode.Created);
            return (await PostAsync(owner, $"/api/v1/workflow/forms/{created.GetProperty("id").GetGuid():D}/publish", new { expectedRevision = 1 })).GetProperty("id").GetGuid();
        }
        var childForm = await Form([new { fieldKey = "note", fieldTypeKey = "text", required = false, constraints = new { } }, new { fieldKey = "member", fieldTypeKey = "text", required = false, constraints = new { } }]);
        var childVersion = await PublishNodesAsync(owner, childForm, [Node("start", "start", new { nextNodeKeys = new[] { "approve" } }), Node("approve", "human.approval", new {
            nextNodeKeys = new[] { "end" }, fieldPolicies = new { note = "editable", member = "readOnly" }, assigneePolicy = new { sources = new[] { new { resolverKindKey = "initiator" } } } }), Node("end", "end", new { })]);
        var choices = new[] { ownerId, first, second }.Concat(foreign is { } id ? [id] : Array.Empty<Guid>()).Select(id => id.ToString("D")).ToArray();
        var form = await Form([new { fieldKey = "selectedUser", fieldTypeKey = "select", required = false, constraints = new { options = choices } },
            new { fieldKey = "actor", fieldTypeKey = "text", required = false, constraints = new { } }, new { fieldKey = "actors", fieldTypeKey = "checkbox", required = false, constraints = new { options = choices } }]);
        async Task<Guid> Publish(object source, object? multi = null, bool denied = false)
        {
            var config = new Dictionary<string, object> { ["nextNodeKeys"] = new[] { "end" }, ["fieldPolicies"] = new { selectedUser = "readOnly", actor = "editable", actors = "editable" },
                ["subprocessPolicy"] = new { definitionVersionId = childVersion, inputFields = new { }, outputFields = new { } }, ["starterPolicy"] = new { source, resultFieldKey = multi is null ? "actor" : "actors" } };
            if (multi is not null) config["multiInstancePolicy"] = multi;
            var draft = new { schemaVersion = 1, workflowKindKey = "automation", nodes = new[] { Node("start", "start", new { nextNodeKeys = new[] { "child" } }), Node("child", "process.subprocess", config), Node("end", "end", new { }) } };
            var definition = await PostAsync(owner, "/api/v1/workflow/definitions", new { definitionKey = "starter." + Guid.NewGuid().ToString("N"), draft }, HttpStatusCode.Created);
            var published = await PostAsync(owner, $"/api/v1/workflow/definitions/{definition.GetProperty("id").GetGuid():D}/publish", new { expectedRevision = 1, formVersionId = form }, denied ? HttpStatusCode.BadRequest : HttpStatusCode.OK);
            return denied ? Guid.Empty : published.GetProperty("id").GetGuid();
        }
        async Task<Guid> Start(Guid version, Guid? selected = null) => (await PostAsync(owner, "/api/v1/workflow/instances", new { definitionVersionId = version, businessType = "starter.test", businessId = Guid.NewGuid().ToString("N"),
            initialValues = new { selectedUser = (selected ?? second).ToString("D"), actor = "before", actors = Array.Empty<string>() }, idempotencyKey = Guid.NewGuid().ToString("N") }, HttpStatusCode.Created)).GetProperty("id").GetGuid();
        async Task Tick()
        { await using var scope = factory.Services.CreateAsyncScope(); await scope.ServiceProvider.GetRequiredService<WorkflowSubprocessProcessor>().ProcessPendingAsync(default); await scope.ServiceProvider.GetRequiredService<WorkflowActivityWorkProcessor>().ProcessPendingAsync(default); }
        async Task Until(Func<Task<bool>> condition, string failure)
        { for (var i = 0; i < 48; i++) { if (await condition()) return; await Tick(); } Assert.Fail(failure); }
        async Task Complete(Guid child)
        {
            var starter = await db.ExecuteScalarAsync<Guid>("SELECT StartedById FROM fn_workflow_instance WHERE Id=@child", new { child }); var client = actors[starter];
            var todo = await db.ExecuteScalarAsync<Guid>("SELECT Id FROM fn_workflow_todo WHERE InstanceId=@child AND StatusKey='active'", new { child });
            var detail = await client.GetFromJsonAsync<JsonElement>($"/api/v1/workflow/todos/{todo:D}");
            await PostAsync(client, $"/api/v1/workflow/todos/{todo:D}/approve", new { expectedRevision = detail.GetProperty("revision").GetInt64(), expectedFormRevision = detail.GetProperty("submissionRevision").GetInt64(), fieldPatch = new { note = "actual actor" }, idempotencyKey = Guid.NewGuid().ToString("N") });
            Assert.AreEqual(0, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_action_record WHERE InstanceId=@child AND ActionKey='start'", new { child }));
        }
        async Task VerifySingle(object source, Guid expected)
        {
            var root = await Start(await Publish(source));
            await Until(async () => await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_subprocess_item WHERE InstanceId=@root AND ChildInstanceId IS NOT NULL", new { root }) == 1, "单项委托没有创建真实子根");
            var child = await db.ExecuteScalarAsync<Guid>("SELECT ChildInstanceId FROM fn_workflow_subprocess_item WHERE InstanceId=@root", new { root });
            Assert.AreEqual(expected, await db.ExecuteScalarAsync<Guid>("SELECT StartedById FROM fn_workflow_instance WHERE Id=@child", new { child }));
            await Complete(child); await Until(async () => await db.ExecuteScalarAsync<string>("SELECT StatusKey FROM fn_workflow_instance WHERE Id=@root", new { root }) == "completed", "单项发起人没有回填");
            var result = JsonSerializer.Deserialize<JsonElement>((await db.ExecuteScalarAsync<string>("SELECT SubmissionJson FROM fn_workflow_form_submission WHERE InstanceId=@root", new { root }))!); Assert.AreEqual(expected.ToString("D"), result.GetProperty("actor").GetString());
            Assert.AreEqual(1, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_domain_audit WHERE InstanceId=@root AND OperationKey='activity.execute' AND ActorUserId IS NULL AND DetailJson LIKE '%child_started%'", new { root }));
        }
        await VerifySingle(new { kindKey = "parent" }, ownerId);
        await VerifySingle(new { kindKey = "specified_user", userId = first }, first);
        await VerifySingle(new { kindKey = "field_user", fieldKey = "selectedUser" }, second);
        var role = Guid.CreateVersion7(); var template = await db.ExecuteScalarAsync<Guid>("SELECT link.RoleId FROM fn_identity_user_role AS link INNER JOIN fn_identity_role AS role ON role.Id=link.RoleId WHERE link.UserId=@first AND role.ScopeKey=@scope", new { first, scope = tenantId is { } tenant ? $"tenant:{tenant:N}" : "host" });
        await db.ExecuteAsync("""
            INSERT INTO fn_identity_role (Id,TenantId,ScopeKey,Code,Name,IsSystem,IsActive,IsSuperAdministrator,DataScopeKind,CreatedAtUtc,Version)
            SELECT @role,TenantId,ScopeKey,@code,Name,IsSystem,IsActive,IsSuperAdministrator,DataScopeKind,CreatedAtUtc,Version FROM fn_identity_role WHERE Id=@template
            """, new { role, code = "starter-source-" + role.ToString("N"), template });
        foreach (var user in new[] { first, second }) await db.ExecuteAsync("INSERT INTO fn_identity_user_role (UserId,RoleId) VALUES (@user,@role)", new { user, role });
        var parallel = await Start(await Publish(new { kindKey = "item_user" }, new { modeKey = "parallel", source = new { kindKey = "role_members", roleId = role, itemFieldKey = "member", maximumCount = 2 }, requiredPercentage = 50, emptyModeKey = "reject", outputReducers = new { } }));
        await Until(async () => await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_subprocess_item WHERE InstanceId=@parallel AND ChildInstanceId IS NOT NULL", new { parallel }) == 2, "角色项委托没有创建两名真实业务发起人");
        var children = (await db.QueryAsync<Guid>("SELECT ChildInstanceId FROM fn_workflow_subprocess_item WHERE InstanceId=@parallel ORDER BY SequenceNo", new { parallel })).ToArray();
        var chosen = await db.ExecuteScalarAsync<Guid>("SELECT StartedById FROM fn_workflow_instance WHERE Id=@id", new { id = children[0] }); await Complete(children[0]);
        await Until(async () => await db.ExecuteScalarAsync<string>("SELECT StatusKey FROM fn_workflow_instance WHERE Id=@parallel", new { parallel }) == "completed", "角色项成功集合没有回填");
        var merged = JsonSerializer.Deserialize<JsonElement>((await db.ExecuteScalarAsync<string>("SELECT SubmissionJson FROM fn_workflow_form_submission WHERE InstanceId=@parallel", new { parallel }))!);
        CollectionAssert.AreEqual(new[] { chosen.ToString("D") }, merged.GetProperty("actors").EnumerateArray().Select(item => item.GetString()).ToArray());
        Assert.AreEqual("cancelled", await db.ExecuteScalarAsync<string>("SELECT StatusKey FROM fn_workflow_instance WHERE Id=@id", new { id = children[1] }));
        var inactiveVersion = await Publish(new { kindKey = "specified_user", userId = second });
        await db.ExecuteAsync("UPDATE fn_identity_user SET IsActive=0 WHERE Id=@second", new { second });
        var blocked = await Start(inactiveVersion); await Until(async () => await db.ExecuteScalarAsync<string>("SELECT StatusKey FROM fn_workflow_instance WHERE Id=@blocked", new { blocked }) == "suspended", "停用身份仍创建子根");
        Assert.AreEqual(0, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_subprocess_item WHERE InstanceId=@blocked AND ChildInstanceId IS NOT NULL", new { blocked }));
        await db.ExecuteAsync("UPDATE fn_identity_user SET IsActive=1 WHERE Id=@second", new { second });
        if (foreign is { } foreignId)
        {
            await Publish(new { kindKey = "specified_user", userId = foreignId }, denied: true);
            var foreignRoot = await Start(await Publish(new { kindKey = "field_user", fieldKey = "selectedUser" }), foreignId);
            await Until(async () => await db.ExecuteScalarAsync<string>("SELECT StatusKey FROM fn_workflow_instance WHERE Id=@foreignRoot", new { foreignRoot }) == "suspended", "非本租户成员仍创建子根");
            Assert.AreEqual(0, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_subprocess_item WHERE InstanceId=@foreignRoot AND ChildInstanceId IS NOT NULL", new { foreignRoot }));
        }
    }
}
