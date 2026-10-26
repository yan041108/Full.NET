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
using Full.NET.Modules.Workflow.Execution;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using MySqlConnector;

namespace Full.NET.IntegrationTests.Workflow;

/// <summary>双库验证完整人工目标的耐久退回，不使用历史代表人伪造单人活动。</summary>
[TestClass]
public sealed class WorkflowHumanReturnApiTests
{
    [TestMethod]
    public async Task Human_return_with_sql_server() => await VerifyAsync(DatabaseProvider.SqlServer, await SharedDatabaseFixture.CreateSqlServerDatabaseAsync());
    [TestMethod]
    public async Task Human_return_with_mysql() => await VerifyAsync(DatabaseProvider.MySql, await SharedDatabaseFixture.CreateMySqlDatabaseAsync());
    private static async Task VerifyAsync(DatabaseProvider provider, string connection)
    {
        using var factory = new FullNetApiFactory(provider, connection); await factory.InitializeAsync(useSchemaTemplate: false);
        await using DbConnection db = provider == DatabaseProvider.SqlServer ? new SqlConnection(connection) : new MySqlConnection(Migration114To126RecoveryTestSupport.MySqlQueryConnectionString(connection));
        var owner = await factory.CreateHostIdentityAsync("return-owner-" + Guid.NewGuid().ToString("N"), [WorkflowPermissions.FormsCreate, WorkflowPermissions.FormsPublish,
            WorkflowPermissions.DefinitionsCreate, WorkflowPermissions.DefinitionsPublish, WorkflowPermissions.InstancesStart, WorkflowPermissions.InstancesRead, WorkflowPermissions.TodosRead, WorkflowPermissions.TodosReturn]);
        var permissions = new[] { WorkflowPermissions.TodosRead, WorkflowPermissions.TodosApprove, WorkflowPermissions.TodosComplete, WorkflowPermissions.TodosReturn };
        var first = await factory.CreateHostIdentityAsync("return-first-" + Guid.NewGuid().ToString("N"), permissions);
        var second = await factory.CreateHostIdentityAsync("return-second-" + Guid.NewGuid().ToString("N"), permissions);
        using var starter = Client(factory, owner); using var firstClient = Client(factory, first); using var secondClient = Client(factory, second);
        var form = await FormAsync(starter);
        foreach (var mode in new[] { "all", "sequential", "group", "task" })
            await CaseAsync(factory, db, starter, form, [firstClient, secondClient], [first.UserId, second.UserId], mode);
        await ParallelBoundaryAsync(factory, db, starter, form, [firstClient, secondClient], [first.UserId, second.UserId]);
        await VerifyTenantAsync(factory, db, starter);
    }

    private static async Task ParallelBoundaryAsync(FullNetApiFactory factory, DbConnection db, HttpClient starter, Guid form, HttpClient[] handlers, Guid[] users)
    {
        var version = await PublishAsync(starter, form, [Node("start", "start", new { nextNodeKeys = new[] { "target" } }), Human("target", "fork", users, "all"),
            Node("fork", "gateway.parallel", new { gatewayRoleKey = "fork", joinNodeKey = "join", branches = new[] { new { branchKey = "left", nextNodeKey = "left" }, new { branchKey = "right", nextNodeKey = "right" } } }),
            Human("left", "join", [users[0]], "all"), Human("right", "join", [users[1]], "all"),
            Node("join", "gateway.parallel", new { gatewayRoleKey = "join", forkNodeKey = "fork", nextNodeKeys = new[] { "end" } }), Node("end", "end", new { })]);
        var instance = (await PostAsync(starter, "/api/v1/workflow/instances", new { definitionVersionId = version, businessType = "human-return", businessId = "parallel", initialValues = new { }, idempotencyKey = "parallel" }, HttpStatusCode.Created)).GetProperty("id").GetGuid();
        var target = await StepAsync(db, instance, "target");
        for (var index = 0; index < 2; index++) await ActAsync(handlers[index], await TodoAsync(db, instance, users[index]), "approve", "parallel-target-" + index);
        var source = await TodoAsync(db, instance, users[0]); var runtime = await handlers[0].GetFromJsonAsync<JsonElement>($"/api/v1/workflow/todos/{source:D}/runtime");
        var candidates = await handlers[0].GetFromJsonAsync<JsonElement>($"/api/v1/workflow/todos/{source:D}/return-targets"); Assert.AreEqual(0, candidates.GetArrayLength());
        await PostAsync(handlers[0], $"/api/v1/workflow/todos/{source:D}/return", new { targetStepId = target, expectedRevision = runtime.GetProperty("revision").GetInt64(), expectedFormRevision = runtime.GetProperty("submissionRevision").GetInt64(), fieldPatch = new { }, comment = "不能跨越活动并行前沿", idempotencyKey = "parallel-return" }, HttpStatusCode.UnprocessableEntity);
        Assert.AreEqual(2, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_todo WHERE InstanceId=@instance AND StatusKey='active'", new { instance }));
        for (var index = 0; index < 2; index++) await ActAsync(handlers[index], await TodoAsync(db, instance, users[index]), "approve", "parallel-finish-" + index);
    }
    private static async Task CaseAsync(FullNetApiFactory factory, DbConnection db, HttpClient starter, Guid form, HttpClient[] handlers, Guid[] users, string mode, HttpClient? foreign = null)
    {
        var version = await PublishAsync(starter, form, [Node("start", "start", new { nextNodeKeys = new[] { "target" } }),
            Human("target", "current", users, mode), Human("current", "end", users, "group"), Node("end", "end", new { })]);
        var instance = (await PostAsync(starter, "/api/v1/workflow/instances", new { definitionVersionId = version, businessType = "human-return", businessId = mode + Guid.NewGuid().ToString("N"), initialValues = new { }, idempotencyKey = Guid.NewGuid().ToString("N") }, HttpStatusCode.Created)).GetProperty("id").GetGuid();
        var target = await StepAsync(db, instance, "target");
        for (var index = 0; index < users.Length; index++) await ActAsync(handlers[index], await TodoAsync(db, instance, users[index]), mode == "task" ? "complete" : "approve", "initial-" + index);
        var current = await StepAsync(db, instance, "current"); var source = await TodoAsync(db, instance, users[0]);
        if (foreign is not null) { using var denied = await foreign.GetAsync($"/api/v1/workflow/todos/{source:D}/return-targets"); Assert.AreEqual(HttpStatusCode.NotFound, denied.StatusCode); }
        var candidates = await handlers[0].GetFromJsonAsync<JsonElement>($"/api/v1/workflow/todos/{source:D}/return-targets");
        Assert.IsTrue(candidates.EnumerateArray().Any(item => item.GetProperty("stepId").GetGuid() == target));
        Assert.AreEqual(JsonValueKind.Null, candidates.EnumerateArray().Single(item => item.GetProperty("stepId").GetGuid() == target).GetProperty("assigneeUserId").ValueKind);
        var runtime = await handlers[0].GetFromJsonAsync<JsonElement>($"/api/v1/workflow/todos/{source:D}/runtime");
        var request = new { targetStepId = target, expectedRevision = runtime.GetProperty("revision").GetInt64(), expectedFormRevision = runtime.GetProperty("submissionRevision").GetInt64(), fieldPatch = new { }, comment = "补齐资料后重新办理", idempotencyKey = "return-" + mode };
        // 两个相同请求只落一条固定回执和一个耐久激活，不把后来产生的待办写回首次结果。
        var receipts = await Task.WhenAll(PostAsync(handlers[0], $"/api/v1/workflow/todos/{source:D}/return", request), PostAsync(handlers[0], $"/api/v1/workflow/todos/{source:D}/return", request));
        Assert.AreEqual(receipts[0].GetRawText(), receipts[1].GetRawText()); Assert.AreEqual(JsonValueKind.Null, receipts[0].GetProperty("activeTodoId").ValueKind);
        Assert.AreEqual("rolled_back", await db.ExecuteScalarAsync<string>("SELECT StatusKey FROM fn_workflow_step WHERE Id=@target", new { target }));
        Assert.AreEqual("returned", await db.ExecuteScalarAsync<string>("SELECT StatusKey FROM fn_workflow_step WHERE Id=@current", new { current }));
        Assert.AreEqual(0, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_approval_group WHERE StepId=@current AND StatusKey='active'", new { current }));
        Assert.AreEqual(0, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_todo WHERE InstanceId=@instance AND StatusKey='active'", new { instance }));
        Assert.AreEqual(1, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_activity_work WHERE InstanceId=@instance AND StatusKey='pending'", new { instance }));
        await using (var scope = factory.Services.CreateAsyncScope()) Assert.AreEqual(1, await scope.ServiceProvider.GetRequiredService<WorkflowActivityWorkProcessor>().ProcessPendingAsync(default));
        var reactivated = await StepAsync(db, instance, "target"); Assert.AreNotEqual(target, reactivated);
        Assert.IsNotNull(await db.ExecuteScalarAsync<string>("SELECT AssigneeSnapshotHash FROM fn_workflow_step WHERE Id=@reactivated", new { reactivated }));
        Assert.AreEqual(2, await db.ExecuteScalarAsync<int>(mode == "task" ? "SELECT COUNT(*) FROM fn_workflow_task_member WHERE TaskId=@reactivated" : "SELECT COUNT(*) FROM fn_workflow_approval_slot WHERE StepId=@reactivated", new { reactivated }));
        var replay = await PostAsync(handlers[0], $"/api/v1/workflow/todos/{source:D}/return", request); Assert.AreEqual(receipts[0].GetRawText(), replay.GetRawText());
        for (var index = 0; index < users.Length; index++) await ActAsync(handlers[index], await TodoAsync(db, instance, users[index]), mode == "task" ? "complete" : "approve", "again-" + index);
        for (var index = 0; index < users.Length; index++) await ActAsync(handlers[index], await TodoAsync(db, instance, users[index]), "approve", "finish-" + index);
        Assert.AreEqual("completed", await db.ExecuteScalarAsync<string>("SELECT StatusKey FROM fn_workflow_instance WHERE Id=@instance", new { instance }));
        replay = await PostAsync(handlers[0], $"/api/v1/workflow/todos/{source:D}/return", request); Assert.AreEqual(receipts[0].GetRawText(), replay.GetRawText());
    }
    private static async Task VerifyTenantAsync(FullNetApiFactory factory, DbConnection db, HttpClient host)
    {
        using var owner = factory.CreateClientForHost("localhost"); owner.DefaultRequestHeaders.Add("Origin", "http://localhost");
        var login = await PostAsync(owner, "/api/v1/auth/login", new LoginRequest("admin", FullNetApiFactory.TestPassword)); var token = login.GetProperty("accessToken").GetString()!;
        owner.DefaultRequestHeaders.Authorization = new("Bearer", token); var tenants = await owner.GetFromJsonAsync<TenantContextSummary[]>("/api/v1/tenancy/available"); var tenant = tenants!.Single(value => value.Identifier == "acme");
        owner.DefaultRequestHeaders.Authorization = new("Bearer", await IntegrationTestTenantContextHelper.SwitchToTenantAsync(owner, token, tenant.Id));
        var ownerId = (await owner.GetFromJsonAsync<JsonElement>("/api/v1/me")).GetProperty("id").GetGuid();
        var username = "return-tenant-" + Guid.NewGuid().ToString("N"); var email = username + "@example.com";
        var member = await factory.CreateHostIdentityAsync(username, [WorkflowPermissions.TodosRead, WorkflowPermissions.TodosApprove, "tenancy.tenants.switch"], password: FullNetApiFactory.TestPassword);
        await factory.EnsureHostUserProfileEmailAsync(member.UserId, email, username); using var handler = factory.CreateClientForHost("localhost"); handler.DefaultRequestHeaders.Add("Origin", "http://localhost");
        token = await IntegrationTestAuthHelper.LoginAsHostUserAsync(handler, username, FullNetApiFactory.TestPassword); handler.DefaultRequestHeaders.Authorization = new("Bearer", token);
        var invitation = await PostAsync(owner, "/api/v1/identity/tenant-members/invitations", new CreateTenantInvitationRequest(email, TenantMemberRoles.Member));
        await PostAsync(handler, $"/api/v1/me/tenant-invitations/{invitation.GetProperty("invitation").GetProperty("id").GetGuid():D}/accept", new { });
        handler.DefaultRequestHeaders.Authorization = new("Bearer", await IntegrationTestTenantContextHelper.SwitchToTenantAsync(handler, token, tenant.Id));
        await CaseAsync(factory, db, owner, await FormAsync(owner), [owner, handler], [ownerId, member.UserId], "group", host);
    }
    private static object Human(string key, string next, Guid[] users, string mode) => mode == "group"
        ? Node(key, "human.group_approval", new { nextNodeKeys = new[] { next }, groupApprovalPolicy = new { modeKey = "all" }, groups = users.Select((id, index) => new { groupKey = "g" + index, assigneePolicy = new { sources = new[] { new { resolverKindKey = "specified_users", userIds = new[] { id } } } } }).ToArray() })
        : mode == "task" ? Node(key, "human.task", new { nextNodeKeys = new[] { next }, taskPolicy = new { completionModeKey = "sequential" }, assigneePolicy = new { sources = new[] { new { resolverKindKey = "specified_users", userIds = users } } } })
        : users.Length == 1 ? Node(key, "human.approval", new { nextNodeKeys = new[] { next }, assigneePolicy = new { sources = new[] { new { resolverKindKey = "specified_users", userIds = users } } } })
        : Node(key, "human.approval", new { nextNodeKeys = new[] { next }, approvalPolicy = new { modeKey = mode, approverUserIds = users } });
    private static async Task ActAsync(HttpClient client, Guid todo, string action, string key)
    { var runtime = await client.GetFromJsonAsync<JsonElement>($"/api/v1/workflow/todos/{todo:D}/runtime"); await PostAsync(client, $"/api/v1/workflow/todos/{todo:D}/{action}", new { expectedRevision = runtime.GetProperty("revision").GetInt64(), expectedFormRevision = runtime.GetProperty("submissionRevision").GetInt64(), fieldPatch = new { }, idempotencyKey = key }); }
    private static Task<Guid> StepAsync(DbConnection db, Guid instance, string node) => db.ExecuteScalarAsync<Guid>("SELECT Id FROM fn_workflow_step WHERE InstanceId=@instance AND NodeKey=@node AND StatusKey='active'", new { instance, node });
    private static Task<Guid> TodoAsync(DbConnection db, Guid instance, Guid user) => db.ExecuteScalarAsync<Guid>("SELECT Id FROM fn_workflow_todo WHERE InstanceId=@instance AND AssigneeUserId=@user AND StatusKey='active'", new { instance, user });
    private static object Node(string key, string kind, object config) => new { nodeKey = key, nodeTypeKey = kind, nodeSchemaVersion = 1, config };
    private static async Task<Guid> FormAsync(HttpClient client)
    { var form = await PostAsync(client, "/api/v1/workflow/forms", new { formKey = "return." + Guid.NewGuid().ToString("N"), draft = new { schemaVersion = 1, adapterVersion = 1, sections = new[] { new { sectionKey = "main", fields = new[] { new { fieldKey = "title", fieldTypeKey = "text", required = false, constraints = new { } } } } } } }, HttpStatusCode.Created); return (await PostAsync(client, $"/api/v1/workflow/forms/{form.GetProperty("id").GetGuid():D}/publish", new { expectedRevision = 1 })).GetProperty("id").GetGuid(); }
    private static async Task<Guid> PublishAsync(HttpClient client, Guid form, object[] nodes)
    { var draft = await PostAsync(client, "/api/v1/workflow/definitions", new { definitionKey = "return." + Guid.NewGuid().ToString("N"), draft = new { schemaVersion = 1, workflowKindKey = "automation", nodes } }, HttpStatusCode.Created); return (await PostAsync(client, $"/api/v1/workflow/definitions/{draft.GetProperty("id").GetGuid():D}/publish", new { expectedRevision = 1, formVersionId = form })).GetProperty("id").GetGuid(); }
    private static HttpClient Client(FullNetApiFactory factory, HostTestIdentity identity) { var client = factory.CreateClientForHost("localhost"); client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", identity.AccessToken); return client; }
    private static async Task<JsonElement> PostAsync(HttpClient client, string path, object request, HttpStatusCode expected = HttpStatusCode.OK)
    { using var response = await client.PostAsJsonAsync(path, request); var text = await response.Content.ReadAsStringAsync(); Assert.AreEqual(expected, response.StatusCode, path + ": " + text); return JsonSerializer.Deserialize<JsonElement>(text); }
}
