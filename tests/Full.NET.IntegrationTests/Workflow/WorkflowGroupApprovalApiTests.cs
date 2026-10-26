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
using Full.NET.Modules.Tenancy.Contracts;
using Full.NET.Modules.Workflow.Contracts;
using Microsoft.Data.SqlClient;
using MySqlConnector;

namespace Full.NET.IntegrationTests.Workflow;

/// <summary>真实双库验证组内收敛、组间门槛、依次激活、历史与取消，个人票不能直接计为组票。</summary>
[TestClass]
public sealed class WorkflowGroupApprovalApiTests
{
    [TestMethod]
    public async Task Group_approval_with_sql_server() => await VerifyAsync(DatabaseProvider.SqlServer, await SharedDatabaseFixture.CreateSqlServerDatabaseAsync());
    [TestMethod]
    public async Task Group_approval_with_mysql() => await VerifyAsync(DatabaseProvider.MySql, await SharedDatabaseFixture.CreateMySqlDatabaseAsync());

    private static async Task VerifyAsync(DatabaseProvider provider, string connectionString)
    {
        using var factory = new FullNetApiFactory(provider, connectionString);
        await factory.InitializeAsync(useSchemaTemplate: false);
        await using DbConnection db = provider == DatabaseProvider.SqlServer ? new SqlConnection(connectionString)
            : new MySqlConnection(Migration114To126RecoveryTestSupport.MySqlQueryConnectionString(connectionString));
        var designer = await factory.CreateHostIdentityAsync("group-designer-" + Guid.NewGuid().ToString("N"),
            [WorkflowPermissions.FormsCreate, WorkflowPermissions.FormsPublish, WorkflowPermissions.DefinitionsCreate,
                WorkflowPermissions.DefinitionsPublish, WorkflowPermissions.InstancesStart, WorkflowPermissions.InstancesCancel,
                WorkflowPermissions.InstancesRecover, WorkflowPermissions.TodosRead]);
        using var owner = Client(factory, designer);
        var people = new List<HostTestIdentity>();
        for (var index = 0; index < 6; index++) people.Add(await factory.CreateHostIdentityAsync("group-member-" + Guid.NewGuid().ToString("N"),
            [WorkflowPermissions.TodosRead, WorkflowPermissions.TodosApprove, WorkflowPermissions.TodosReject, WorkflowPermissions.TodosTransfer]));
        var clients = people.Select(person => Client(factory, person)).ToArray();
        try
        {
            var formVersion = await CreateFormAsync(owner);
            foreach (var mode in new[] { "all", "any", "nOfM", "percent" })
            {
                var version = await PublishAsync(owner, formVersion, people.Select(person => person.UserId).ToArray(), mode);
                async Task<Guid> StartAsync(string suffix) => (await PostAsync(owner, "/api/v1/workflow/instances", new {
                    definitionVersionId = version, businessType = "group", businessId = mode + suffix,
                    initialValues = new { title = "initial" }, idempotencyKey = mode + suffix }, HttpStatusCode.Created)).GetProperty("id").GetGuid();
                var instance = await StartAsync("-normal");
                Assert.AreEqual(3, await CountAsync(db, "fn_workflow_approval_group", instance));
                Assert.AreEqual(6, await CountAsync(db, "fn_workflow_approval_slot", instance));
                Assert.AreEqual(5, await CountAsync(db, "fn_workflow_todo", instance));
                Assert.AreEqual(mode == "any" ? 1 : mode == "nOfM" ? 2 : 3,
                    await db.ExecuteScalarAsync<int>("SELECT RequiredApprovalCount FROM fn_workflow_step WHERE InstanceId=@instance", new { instance }));
                Assert.AreEqual(0, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_todo WHERE InstanceId=@instance AND AssigneeUserId=@actor", new { instance, actor = people[1].UserId }));
                var first = await TodoAsync(db, instance, people[0].UserId);
                using (var foreign = await clients[2].GetAsync($"/api/v1/workflow/todos/{first:D}/runtime")) Assert.AreEqual(HttpStatusCode.Forbidden, foreign.StatusCode);
                var frozen = await db.ExecuteScalarAsync<string>("SELECT AssigneeSnapshotJson FROM fn_workflow_step WHERE InstanceId=@instance", new { instance });
                using (var snapshot = JsonDocument.Parse(frozen!)) Assert.AreEqual(2, snapshot.RootElement.GetProperty("schemaVersion").GetInt32());

                async Task<JsonElement> ActAsync(int actor, string action = "approve", string? title = null)
                {
                    var todo = await TodoAsync(db, instance, people[actor].UserId);
                    var detail = await clients[actor].GetFromJsonAsync<JsonElement>($"/api/v1/workflow/todos/{todo:D}/runtime");
                    var request = Vote(detail.GetProperty("revision").GetInt64(), detail.GetProperty("submissionRevision").GetInt64(), action + actor, title);
                    if (mode == "all" && actor == 0 && action == "approve")
                    {
                        var results = await Task.WhenAll(PostAsync(clients[actor], $"/api/v1/workflow/todos/{todo:D}/{action}", request),
                            PostAsync(clients[actor], $"/api/v1/workflow/todos/{todo:D}/{action}", request));
                        Assert.AreEqual(results[0].GetRawText(), results[1].GetRawText());
                        return results[0];
                    }
                    return await PostAsync(clients[actor], $"/api/v1/workflow/todos/{todo:D}/{action}", request);
                }
                if (mode == "any")
                {
                    await ActAsync(2);
                    Assert.AreEqual("completed", await StatusAsync(db, instance));
                    Assert.AreEqual(2, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_approval_group WHERE InstanceId=@instance AND StatusKey='cancelled'", new { instance }));
                    Assert.AreEqual(0, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_todo WHERE InstanceId=@instance AND AssigneeUserId=@actor", new { instance, actor = people[1].UserId }));
                }
                else
                {
                    await ActAsync(0, mode == "nOfM" ? "reject" : "approve", "after-first");
                    Assert.AreEqual("active", await StatusAsync(db, instance));
                    if (mode != "nOfM")
                    {
                        var next = await TodoAsync(db, instance, people[1].UserId);
                        var runtime = await clients[1].GetFromJsonAsync<JsonElement>($"/api/v1/workflow/todos/{next:D}/runtime");
                        Assert.AreEqual("after-first", runtime.GetProperty("submission").GetProperty("title").GetString());
                        Assert.AreEqual(0, runtime.GetProperty("approvedCount").GetInt32());
                        var group = runtime.GetProperty("approvalGroups").EnumerateArray().Single(value => value.GetProperty("isCurrentGroup").GetBoolean());
                        Assert.AreEqual(1, group.GetProperty("approvedCount").GetInt32());
                        await ActAsync(1);
                    }
                    await ActAsync(2);
                    Assert.AreEqual("active", await StatusAsync(db, instance));
                    await ActAsync(4);
                    Assert.AreEqual("active", await StatusAsync(db, instance));
                    await ActAsync(5, title: "after-last");
                    Assert.AreEqual("completed", await StatusAsync(db, instance));
                }
                Assert.AreEqual(0, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_todo WHERE InstanceId=@instance AND StatusKey='active'", new { instance }));
                Assert.AreEqual(0, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_approval_slot WHERE InstanceId=@instance AND DecisionKey IS NULL", new { instance }));
                Assert.AreEqual(frozen, await db.ExecuteScalarAsync<string>("SELECT AssigneeSnapshotJson FROM fn_workflow_step WHERE InstanceId=@instance", new { instance }));
                var history = await clients[0].GetFromJsonAsync<JsonElement>($"/api/v1/workflow/todos/{first:D}/runtime");
                Assert.AreEqual(mode == "any" ? "initial" : "after-first", history.GetProperty("submission").GetProperty("title").GetString());
                using (var input = JsonDocument.Parse((await db.ExecuteScalarAsync<string>("SELECT InputJson FROM fn_workflow_todo_view WHERE Id=@first", new { first }))!))
                    Assert.AreEqual("initial", input.RootElement.GetProperty("title").GetString(), "本人输出不覆盖该次激活的输入事实。");
                Assert.AreEqual(3, history.GetProperty("approvalGroups").GetArrayLength());

                if (mode == "all")
                {
                    var rejected = await StartAsync("-reject");
                    var rejectTodo = await TodoAsync(db, rejected, people[0].UserId);
                    await PostAsync(clients[0], $"/api/v1/workflow/todos/{rejectTodo:D}/reject", Vote(1, 1, "early-reject", null));
                    Assert.AreEqual("rejected", await StatusAsync(db, rejected));
                    using var rejectedHistory = await clients[0].GetAsync($"/api/v1/workflow/todos/{rejectTodo:D}/runtime");
                    Assert.AreEqual(HttpStatusCode.OK, rejectedHistory.StatusCode);
                    var cancelled = await StartAsync("-cancel");
                    await PostAsync(owner, $"/api/v1/workflow/instances/{cancelled:D}/cancel", new { expectedRevision = 1, reason = "测试取消", idempotencyKey = "cancel-group" });
                    Assert.AreEqual(3, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_approval_group WHERE InstanceId=@instance AND StatusKey='cancelled'", new { instance = cancelled }));
                    await RecoverAsync(provider, connectionString, db);
                }
            }
            await VerifyHandoffAsync(factory, db, owner, designer.UserId, people, clients, formVersion);
            await VerifySystemGroupsAsync(db, owner, clients[0], designer.UserId, people[0].UserId, formVersion);
            await VerifyTenantAsync(factory, db, owner);
        }
        finally { foreach (var client in clients) client.Dispose(); }
    }

    /// <summary>有效空角色产生零席位系统组，固定两组门槛仍等待一次真实人审；本人跳过满足任一组时不创建虚假待办。</summary>
    private static async Task VerifySystemGroupsAsync(DbConnection db, HttpClient owner, HttpClient member, Guid ownerId, Guid memberId, Guid form)
    {
        var empty = Guid.CreateVersion7(); var source = await db.QueryFirstAsync<Guid>("SELECT RoleId FROM fn_identity_user_role WHERE UserId=@ownerId", new { ownerId });
        await db.ExecuteAsync("""
            INSERT INTO fn_identity_role(Id,TenantId,ScopeKey,Code,Name,IsSystem,IsActive,IsSuperAdministrator,DataScopeKind,CreatedAtUtc,Version)
            SELECT @empty,TenantId,ScopeKey,@code,Name,0,1,0,DataScopeKind,CreatedAtUtc,Version FROM fn_identity_role WHERE Id=@source
            """, new { empty, source, code = "group-empty-" + empty.ToString("N") });
        foreach (var mode in new[] { "all", "any" })
        {
            object automatic = mode == "all"
                ? new { sources = new[] { new { resolverKindKey = "role_members", roleIds = new[] { empty } } }, handling = new { whenEmpty = new { actionKey = "auto_pass" } } }
                : new { sources = new[] { new { resolverKindKey = "initiator" } }, handling = new { whenInitiator = new { actionKey = "skip" } } };
            var definition = await PostAsync(owner, "/api/v1/workflow/definitions", new { definitionKey = "group-system." + Guid.NewGuid().ToString("N"), draft = new {
                schemaVersion = 1, workflowKindKey = mode == "all" ? "approval" : "automation", nodes = new[] {
                    Node("start", "start", new { nextNodeKeys = new[] { "groups" } }),
                    Node("groups", "human.group_approval", new { nextNodeKeys = new[] { "end" }, groupApprovalPolicy = new { modeKey = mode }, groups = new object[] {
                        new { groupKey = "automatic", assigneePolicy = automatic }, new { groupKey = "human", assigneePolicy = new { sources = new[] { new { resolverKindKey = "specified_users", userIds = new[] { memberId } } } } }
                    } }), Node("end", "end", new { }) } } }, HttpStatusCode.Created);
            var version = (await PostAsync(owner, $"/api/v1/workflow/definitions/{definition.GetProperty("id").GetGuid():D}/publish", new { expectedRevision = 1, formVersionId = form })).GetProperty("id").GetGuid();
            var request = new { definitionVersionId = version, businessType = "system-group", businessId = mode, initialValues = new { title = "initial" }, idempotencyKey = "system-group-" + mode };
            var started = await PostAsync(owner, "/api/v1/workflow/instances", request, HttpStatusCode.Created); var instance = started.GetProperty("id").GetGuid();
            if (mode == "any")
            {
                Assert.AreEqual("completed", started.GetProperty("statusKey").GetString());
                Assert.AreEqual(0, await CountAsync(db, "fn_workflow_todo", instance));
                Assert.AreEqual(0, await CountAsync(db, "fn_workflow_approval_slot", instance));
                Assert.AreEqual(started.GetRawText(), (await PostAsync(owner, "/api/v1/workflow/instances", request, HttpStatusCode.Created)).GetRawText());
                continue;
            }
            Assert.AreEqual("active", started.GetProperty("statusKey").GetString());
            Assert.AreEqual(2, await CountAsync(db, "fn_workflow_approval_group", instance));
            Assert.AreEqual(1, await CountAsync(db, "fn_workflow_approval_slot", instance)); Assert.AreEqual(1, await CountAsync(db, "fn_workflow_todo", instance));
            var todo = await TodoAsync(db, instance, memberId); var runtime = await member.GetFromJsonAsync<JsonElement>($"/api/v1/workflow/todos/{todo:D}/runtime");
            Assert.AreEqual(2, runtime.GetProperty("requiredApprovalCount").GetInt32()); Assert.AreEqual(1, runtime.GetProperty("approvedCount").GetInt32());
            var system = runtime.GetProperty("approvalGroups").EnumerateArray().Single(group => group.GetProperty("groupKey").GetString() == "automatic");
            Assert.AreEqual("system", system.GetProperty("approvalModeKey").GetString()); Assert.AreEqual("approved", system.GetProperty("statusKey").GetString());
            foreach (var count in new[] { "memberCount", "requiredApprovalCount", "approvedCount", "pendingCount" }) Assert.AreEqual(0, system.GetProperty(count).GetInt32());
            await PostAsync(member, $"/api/v1/workflow/todos/{todo:D}/approve", Vote(runtime.GetProperty("revision").GetInt64(), runtime.GetProperty("submissionRevision").GetInt64(), "human-after-system", null));
            Assert.AreEqual("completed", await StatusAsync(db, instance));
            Assert.AreEqual(1, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_action_record WHERE InstanceId=@instance AND ActionKey='approve'", new { instance }));
            Assert.AreEqual(1, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_domain_audit WHERE InstanceId=@instance AND OperationKey='assignee.handle' AND ActorUserId IS NULL", new { instance }));
            var history = await member.GetFromJsonAsync<JsonElement>($"/api/v1/workflow/todos/{todo:D}/runtime");
            Assert.AreEqual(2, history.GetProperty("approvedCount").GetInt32());
        }
    }

    /// <summary>组内个人转办与管理员改派重绑原席位，不能改变组归属或泄露新待办。</summary>
    private static async Task VerifyHandoffAsync(FullNetApiFactory factory, DbConnection db, HttpClient owner, Guid administrator,
        List<HostTestIdentity> people, HttpClient[] clients, Guid formVersion)
    {
        var spares = new List<HostTestIdentity>();
        for (var index = 0; index < 2; index++) spares.Add(await factory.CreateHostIdentityAsync("group-handoff-" + Guid.NewGuid().ToString("N"),
            [WorkflowPermissions.TodosRead, WorkflowPermissions.TodosApprove]));
        using var personalTarget = Client(factory, spares[0]); using var adminTarget = Client(factory, spares[1]);
        var version = await PublishAsync(owner, formVersion, people.Select(person => person.UserId).ToArray(), "all");
        var instance = (await PostAsync(owner, "/api/v1/workflow/instances", new { definitionVersionId = version, businessType = "group",
            businessId = "handoff", initialValues = new { title = "initial" }, idempotencyKey = "group-handoff" }, HttpStatusCode.Created)).GetProperty("id").GetGuid();
        var frozen = await db.ExecuteScalarAsync<string>("SELECT AssigneeSnapshotJson FROM fn_workflow_step WHERE InstanceId=@instance", new { instance });
        var first = await TodoAsync(db, instance, people[0].UserId);
        var transferred = (await PostAsync(clients[0], $"/api/v1/workflow/todos/{first:D}/transfer", new {
            assigneeUserId = spares[0].UserId, expectedRevision = 1, expectedFormRevision = 1, idempotencyKey = "group-personal" })).GetProperty("activeTodoId").GetGuid();
        Assert.AreNotEqual(first, transferred);
        using (var denied = await clients[0].GetAsync($"/api/v1/workflow/todos/{transferred:D}/runtime")) Assert.AreEqual(HttpStatusCode.Forbidden, denied.StatusCode);
        var firstOld = await clients[0].GetFromJsonAsync<JsonElement>($"/api/v1/workflow/todos/{first:D}/runtime");
        Assert.AreEqual("first", firstOld.GetProperty("approvalGroups").EnumerateArray().Single(group => group.GetProperty("isCurrentGroup").GetBoolean()).GetProperty("groupKey").GetString());
        await PostAsync(personalTarget, $"/api/v1/workflow/todos/{transferred:D}/approve", Vote(1, 1, "group-transferred-approve", "after-transfer"));
        var second = await TodoAsync(db, instance, people[1].UserId);
        var reassignRequest = new { assigneeUserId = spares[1].UserId, expectedRevision = 3, expectedTodoRevision = 1,
            todoId = second, reason = "改派组内当前顺序席位", idempotencyKey = "group-admin" };
        var reassignment = await PostAsync(owner, $"/api/v1/workflow/instances/{instance:D}/reassign", reassignRequest);
        var reassigned = reassignment.GetProperty("activeTodoId").GetGuid(); Assert.AreNotEqual(second, reassigned);
        var old = await clients[1].GetFromJsonAsync<JsonElement>($"/api/v1/workflow/todos/{second:D}/runtime");
        Assert.AreEqual("after-transfer", old.GetProperty("submission").GetProperty("title").GetString());
        Assert.AreEqual("first", old.GetProperty("approvalGroups").EnumerateArray().Single(group => group.GetProperty("isCurrentGroup").GetBoolean()).GetProperty("groupKey").GetString());
        using (var denied = await clients[1].GetAsync($"/api/v1/workflow/todos/{reassigned:D}/runtime")) Assert.AreEqual(HttpStatusCode.Forbidden, denied.StatusCode);
        var current = await adminTarget.GetFromJsonAsync<JsonElement>($"/api/v1/workflow/todos/{reassigned:D}/runtime");
        Assert.AreEqual(1, current.GetProperty("approvalGroups").EnumerateArray().Single(group => group.GetProperty("isCurrentGroup").GetBoolean()).GetProperty("approvedCount").GetInt32());
        Assert.AreEqual(1, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_todo_transfer WHERE InstanceId=@instance AND FromTodoId=@second AND ToTodoId=@reassigned AND ActorUserId=@administrator AND AuthorizationKey='workflow.instances.recover' AND SlotId IS NOT NULL AND TaskMemberId IS NULL", new { instance, second, reassigned, administrator }));
        await PostAsync(adminTarget, $"/api/v1/workflow/todos/{reassigned:D}/approve", Vote(1, 2, "group-admin-approve", "after-admin"));
        foreach (var actor in new[] { 2, 4, 5 })
        {
            var todo = await TodoAsync(db, instance, people[actor].UserId);
            var runtime = await clients[actor].GetFromJsonAsync<JsonElement>($"/api/v1/workflow/todos/{todo:D}/runtime");
            await PostAsync(clients[actor], $"/api/v1/workflow/todos/{todo:D}/approve", Vote(1, runtime.GetProperty("submissionRevision").GetInt64(), "group-remaining-" + actor, null));
        }
        Assert.AreEqual("completed", await StatusAsync(db, instance));
        Assert.AreEqual(frozen, await db.ExecuteScalarAsync<string>("SELECT AssigneeSnapshotJson FROM fn_workflow_step WHERE InstanceId=@instance", new { instance }));
        Assert.AreEqual(reassignment.GetRawText(), (await PostAsync(owner, $"/api/v1/workflow/instances/{instance:D}/reassign", reassignRequest)).GetRawText());
        var history = await clients[1].GetFromJsonAsync<JsonElement>($"/api/v1/workflow/todos/{second:D}/runtime");
        Assert.AreEqual("after-transfer", history.GetProperty("submission").GetProperty("title").GetString());
    }

    private static async Task VerifyTenantAsync(FullNetApiFactory factory, DbConnection db, HttpClient hostReader)
    {
        using var owner = factory.CreateClientForHost("localhost"); owner.DefaultRequestHeaders.Add("Origin", "http://localhost");
        var login = await PostAsync(owner, "/api/v1/auth/login", new LoginRequest("admin", FullNetApiFactory.TestPassword));
        var hostToken = login.GetProperty("accessToken").GetString()!; owner.DefaultRequestHeaders.Authorization = new("Bearer", hostToken);
        var tenants = await owner.GetFromJsonAsync<TenantContextSummary[]>("/api/v1/tenancy/available"); var tenant = tenants!.Single(value => value.Identifier == "acme");
        owner.DefaultRequestHeaders.Authorization = new("Bearer", await IntegrationTestTenantContextHelper.SwitchToTenantAsync(owner, hostToken, tenant.Id));
        var members = new List<HostTestIdentity>(); var clients = new List<HttpClient>();
        try
        {
            for (var index = 0; index < 2; index++)
            {
                var username = "group-tenant-" + Guid.NewGuid().ToString("N"); var email = username + "@example.com";
                var member = await factory.CreateHostIdentityAsync(username, [WorkflowPermissions.TodosRead, WorkflowPermissions.TodosApprove, "tenancy.tenants.switch"], password: FullNetApiFactory.TestPassword);
                await factory.EnsureHostUserProfileEmailAsync(member.UserId, email, username); members.Add(member);
                var client = factory.CreateClientForHost("localhost"); clients.Add(client); client.DefaultRequestHeaders.Add("Origin", "http://localhost");
                var token = await IntegrationTestAuthHelper.LoginAsHostUserAsync(client, username, FullNetApiFactory.TestPassword); client.DefaultRequestHeaders.Authorization = new("Bearer", token);
                var invitation = await PostAsync(owner, "/api/v1/identity/tenant-members/invitations", new CreateTenantInvitationRequest(email, TenantMemberRoles.Member));
                await PostAsync(client, $"/api/v1/me/tenant-invitations/{invitation.GetProperty("invitation").GetProperty("id").GetGuid():D}/accept", new { });
                client.DefaultRequestHeaders.Authorization = new("Bearer", await IntegrationTestTenantContextHelper.SwitchToTenantAsync(client, token, tenant.Id));
            }
            var version = await PublishAsync(owner, await CreateFormAsync(owner), members.Select(value => value.UserId).ToArray(), "all");
            var started = await PostAsync(owner, "/api/v1/workflow/instances", new { definitionVersionId = version, businessType = "group", businessId = "tenant", initialValues = new { title = "tenant" }, idempotencyKey = "group-tenant" }, HttpStatusCode.Created);
            var instance = started.GetProperty("id").GetGuid();
            for (var index = 0; index < 2; index++)
            {
                var todo = await TodoAsync(db, instance, members[index].UserId);
                using (var foreign = await hostReader.GetAsync($"/api/v1/workflow/todos/{todo:D}/runtime")) Assert.AreEqual(HttpStatusCode.NotFound, foreign.StatusCode);
                var runtime = await clients[index].GetFromJsonAsync<JsonElement>($"/api/v1/workflow/todos/{todo:D}/runtime");
                Assert.AreEqual(2, runtime.GetProperty("approvalGroups").GetArrayLength());
                await PostAsync(clients[index], $"/api/v1/workflow/todos/{todo:D}/approve", Vote(1, index + 1, "tenant-" + index, null));
            }
            Assert.AreEqual("completed", await StatusAsync(db, instance));
        }
        finally { foreach (var client in clients) client.Dispose(); }
    }

    private static async Task<Guid> CreateFormAsync(HttpClient owner)
    {
        var form = await PostAsync(owner, "/api/v1/workflow/forms", new { formKey = "group." + Guid.NewGuid().ToString("N"), draft = new { schemaVersion = 1, adapterVersion = 1,
            sections = new[] { new { sectionKey = "main", fields = new[] { new { fieldKey = "title", fieldTypeKey = "text", required = false, constraints = new { } } } } } } }, HttpStatusCode.Created);
        return (await PostAsync(owner, $"/api/v1/workflow/forms/{form.GetProperty("id").GetGuid():D}/publish", new { expectedRevision = 1 })).GetProperty("id").GetGuid();
    }
    private static async Task<Guid> PublishAsync(HttpClient owner, Guid formVersion, Guid[] people, string mode)
    {
        object[] groups = people.Length == 2 ? people.Select((id, index) => (object)new { groupKey = "group_" + index,
            assigneePolicy = new { sources = new[] { new { resolverKindKey = "specified_users", userIds = new[] { id } } } } }).ToArray()
            : new[] { ("first", "sequential"), ("second", "any"), ("third", "all") }.Select((group, index) => (object)new {
                groupKey = group.Item1, groupName = group.Item1, approvalPolicy = new { modeKey = group.Item2, approverUserIds = people.Skip(index * 2).Take(2).ToArray() } }).ToArray();
        var gate = new Dictionary<string, object> { ["modeKey"] = mode };
        if (mode == "nOfM") gate["requiredApprovals"] = 2;
        if (mode == "percent") gate["requiredPercentage"] = 67;
        var draft = await PostAsync(owner, "/api/v1/workflow/definitions", new { definitionKey = "group." + Guid.NewGuid().ToString("N"), draft = new { schemaVersion = 1, workflowKindKey = "approval", nodes = new[] {
            Node("start", "start", new { nextNodeKeys = new[] { "review" } }), Node("review", "human.group_approval", new { nextNodeKeys = new[] { "end" }, groups, groupApprovalPolicy = gate }), Node("end", "end", new { }) } } }, HttpStatusCode.Created);
        return (await PostAsync(owner, $"/api/v1/workflow/definitions/{draft.GetProperty("id").GetGuid():D}/publish", new { expectedRevision = 1, formVersionId = formVersion })).GetProperty("id").GetGuid();
    }
    private static async Task RecoverAsync(DatabaseProvider provider, string connectionString, DbConnection db)
    {
        await db.ExecuteAsync("DROP INDEX uq_fn_workflow_approval_group_sequence ON fn_workflow_approval_group");
        await db.ExecuteAsync(provider == DatabaseProvider.SqlServer ? "ALTER TABLE fn_workflow_approval_group DROP CONSTRAINT ck_fn_workflow_approval_group_state_306" : "ALTER TABLE fn_workflow_approval_group DROP CHECK ck_fn_workflow_approval_group_state_306");
        var journal = provider == DatabaseProvider.SqlServer ? "dbo.SchemaVersions" : "schemaversions";
        foreach (var script in new[] { "%299_WorkflowApprovalGroups.sql", "%306_WorkflowSystemApprovalGroups.sql" })
            await db.ExecuteAsync($"DELETE FROM {journal} WHERE ScriptName LIKE @script", new { script });
        await WorkflowHumanMigrationRecoveryTests.MigrateAsync(provider, connectionString);
    }
    private static Task<int> CountAsync(DbConnection db, string table, Guid instance) => table switch
    {
        "fn_workflow_approval_group" => db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_approval_group WHERE InstanceId=@instance", new { instance }),
        "fn_workflow_approval_slot" => db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_approval_slot WHERE InstanceId=@instance", new { instance }),
        "fn_workflow_todo" => db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_todo WHERE InstanceId=@instance", new { instance }),
        _ => throw new ArgumentOutOfRangeException(nameof(table))
    };
    private static Task<Guid> TodoAsync(DbConnection db, Guid instance, Guid actor) => db.ExecuteScalarAsync<Guid>("SELECT Id FROM fn_workflow_todo WHERE InstanceId=@instance AND AssigneeUserId=@actor AND StatusKey='active'", new { instance, actor });
    private static Task<string?> StatusAsync(DbConnection db, Guid instance) => db.ExecuteScalarAsync<string?>("SELECT StatusKey FROM fn_workflow_instance WHERE Id=@instance", new { instance });
    private static object Vote(long revision, long formRevision, string key, string? title) => new { expectedRevision = revision, expectedFormRevision = formRevision,
        fieldPatch = title is null ? new Dictionary<string, string>() : new Dictionary<string, string> { ["title"] = title }, idempotencyKey = key };
    private static object Node(string key, string kind, object config) => new { nodeKey = key, nodeTypeKey = kind, nodeSchemaVersion = 1, config };
    private static HttpClient Client(FullNetApiFactory factory, HostTestIdentity identity)
    { var client = factory.CreateClientForHost("localhost"); client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", identity.AccessToken); return client; }
    private static async Task<JsonElement> PostAsync(HttpClient client, string path, object body, HttpStatusCode expected = HttpStatusCode.OK)
    {
        using var response = await client.PostAsJsonAsync(path, body); var json = await response.Content.ReadAsStringAsync();
        Assert.AreEqual(expected, response.StatusCode, path + ": " + json);
        return JsonSerializer.Deserialize<JsonElement>(json);
    }
}
