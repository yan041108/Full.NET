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

/// <summary>真实办理提交、两类交接、签名及并行分支均保持独立成员事实，不伪造同意票。</summary>
[TestClass]
public sealed class WorkflowTaskActivityApiTests
{
    [TestMethod]
    public async Task Task_activity_with_sql_server() => await VerifyAsync(DatabaseProvider.SqlServer, await SharedDatabaseFixture.CreateSqlServerDatabaseAsync());
    [TestMethod]
    public async Task Task_activity_with_mysql() => await VerifyAsync(DatabaseProvider.MySql, await SharedDatabaseFixture.CreateMySqlDatabaseAsync());

    private static async Task VerifyAsync(DatabaseProvider provider, string connectionString)
    {
        using var factory = new FullNetApiFactory(provider, connectionString); await factory.InitializeAsync(useSchemaTemplate: false);
        await using DbConnection db = provider == DatabaseProvider.SqlServer ? new SqlConnection(connectionString)
            : new MySqlConnection(Migration114To126RecoveryTestSupport.MySqlQueryConnectionString(connectionString));
        var designer = await factory.CreateHostIdentityAsync("task-designer-" + Guid.NewGuid().ToString("N"),
            [WorkflowPermissions.FormsCreate, WorkflowPermissions.FormsPublish, WorkflowPermissions.DefinitionsCreate, WorkflowPermissions.DefinitionsPublish,
                WorkflowPermissions.InstancesStart, WorkflowPermissions.InstancesRecover, WorkflowPermissions.TodosRead]);
        using var owner = Client(factory, designer);
        var people = new List<HostTestIdentity>();
        for (var i = 0; i < 4; i++) people.Add(await factory.CreateHostIdentityAsync("task-member-" + Guid.NewGuid().ToString("N"),
            [WorkflowPermissions.TodosRead, WorkflowPermissions.TodosComplete, WorkflowPermissions.TodosTransfer, WorkflowPermissions.TodosSign]));
        var clients = people.Select(person => Client(factory, person)).ToArray();
        try
        {
            var formVersion = await CreateFormAsync(owner);
            foreach (var mode in new[] { "all", "any", "sequential" })
            {
                var version = await PublishAsync(owner, formVersion, [Node("start", "start", new { nextNodeKeys = new[] { "task" } }),
                    Node("task", "human.task", new { nextNodeKeys = new[] { "end" }, taskPolicy = new { completionModeKey = mode },
                        assigneePolicy = Sources(people.Take(3).Select(person => person.UserId).ToArray()),
                        operationPermissions = new { complete = true, transfer = mode != "sequential", @return = false, remind = false } }), Node("end", "end", new { })]);
                var instance = await StartAsync(owner, version, mode);
                Assert.AreEqual(0, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_approval_slot WHERE InstanceId=@instance", new { instance }));
                Assert.AreEqual(3, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_task_member WHERE InstanceId=@instance", new { instance }));
                Assert.AreEqual(mode == "sequential" ? 1 : 3, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_todo WHERE InstanceId=@instance", new { instance }));
                var first = await TodoAsync(db, instance, people[0].UserId); var initial = await RuntimeAsync(clients[0], first);
                Assert.AreEqual("human.task", initial.GetProperty("activityKindKey").GetString()); Assert.AreEqual("none", initial.GetProperty("approvalModeKey").GetString());
                Assert.AreEqual(0, initial.GetProperty("requiredApprovalCount").GetInt32());
                using (var approve = await clients[0].PostAsJsonAsync($"/api/v1/workflow/todos/{first:D}/approve", Vote(initial, "forbidden-approve", null))) Assert.AreEqual(HttpStatusCode.Forbidden, approve.StatusCode);
                await PostAsync(clients[1], $"/api/v1/workflow/todos/{first:D}/complete", Vote(initial, "foreign", null), HttpStatusCode.Forbidden);
                var snapshot = await db.ExecuteScalarAsync<string>("SELECT AssigneeSnapshotJson FROM fn_workflow_step WHERE InstanceId=@instance", new { instance });
                var firstRequest = Vote(initial, "first", "after-first");
                var duplicate = await Task.WhenAll(PostAsync(clients[0], $"/api/v1/workflow/todos/{first:D}/complete", firstRequest),
                    PostAsync(clients[0], $"/api/v1/workflow/todos/{first:D}/complete", firstRequest));
                Assert.AreEqual(duplicate[0].GetRawText(), duplicate[1].GetRawText());
                JsonElement? reassignment = null; object? reassignmentRequest = null;
                if (mode != "any")
                {
                    var second = await TodoAsync(db, instance, people[1].UserId); var runtime = await RuntimeAsync(clients[1], second);
                    Assert.AreEqual("after-first", runtime.GetProperty("submission").GetProperty("title").GetString());
                    Assert.AreEqual(1, runtime.GetProperty("taskProgress").GetProperty("completedCount").GetInt32());
                    Guid handed;
                    if (mode == "sequential")
                    {
                        Assert.AreEqual(2, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_todo WHERE InstanceId=@instance", new { instance }));
                        await PostAsync(clients[1], $"/api/v1/workflow/todos/{second:D}/transfer", new { assigneeUserId = people[3].UserId,
                            expectedRevision = 1, expectedFormRevision = 2, idempotencyKey = "node-disabled" }, HttpStatusCode.Forbidden);
                        reassignmentRequest = new { assigneeUserId = people[3].UserId, expectedRevision = 2, expectedTodoRevision = 1,
                            todoId = second, reason = "明确改派当前顺序成员", idempotencyKey = "admin-handoff" };
                        await PostAsync(clients[1], $"/api/v1/workflow/instances/{instance:D}/reassign", reassignmentRequest, HttpStatusCode.Forbidden);
                        reassignment = await PostAsync(owner, $"/api/v1/workflow/instances/{instance:D}/reassign", reassignmentRequest);
                        handed = reassignment.Value.GetProperty("activeTodoId").GetGuid();
                        Assert.AreEqual(1, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_todo_transfer WHERE InstanceId=@instance AND FromTodoId=@source AND ToTodoId=@target AND ActorUserId=@actor AND AuthorizationKey='workflow.instances.recover' AND TaskMemberId IS NOT NULL AND SlotId IS NULL",
                            new { instance, source = second, target = handed, actor = designer.UserId }));
                    }
                    else
                    {
                        await PostAsync(clients[1], $"/api/v1/workflow/todos/{second:D}/transfer", new { assigneeUserId = people[0].UserId,
                            expectedRevision = 1, expectedFormRevision = 2, idempotencyKey = "occupied" }, HttpStatusCode.BadRequest);
                        var result = await PostAsync(clients[1], $"/api/v1/workflow/todos/{second:D}/transfer", new { assigneeUserId = people[3].UserId,
                            expectedRevision = 1, expectedFormRevision = 2, idempotencyKey = "personal-handoff" });
                        handed = result.GetProperty("activeTodoId").GetGuid();
                    }
                    Assert.AreNotEqual(second, handed);
                    using (var foreign = await clients[1].GetAsync($"/api/v1/workflow/todos/{handed:D}/runtime")) Assert.AreEqual(HttpStatusCode.Forbidden, foreign.StatusCode);
                    var old = await RuntimeAsync(clients[1], second);
                    Assert.AreEqual(mode == "sequential" ? "after-first" : "initial", old.GetProperty("submission").GetProperty("title").GetString());
                    var current = await RuntimeAsync(clients[3], handed);
                    await PostAsync(clients[3], $"/api/v1/workflow/todos/{handed:D}/complete", Vote(current, "new-owner-complete", "after-handoff"));
                    var last = await TodoAsync(db, instance, people[2].UserId); var lastRuntime = await RuntimeAsync(clients[2], last);
                    Assert.AreEqual("after-handoff", lastRuntime.GetProperty("submission").GetProperty("title").GetString());
                    await PostAsync(clients[2], $"/api/v1/workflow/todos/{last:D}/complete", Vote(lastRuntime, "last", null));
                    if (reassignment is { } originalResponse)
                        Assert.AreEqual(originalResponse.GetRawText(), (await PostAsync(owner, $"/api/v1/workflow/instances/{instance:D}/reassign", reassignmentRequest!)).GetRawText());
                }
                Assert.AreEqual("completed", await db.ExecuteScalarAsync<string>("SELECT StatusKey FROM fn_workflow_instance WHERE Id=@instance", new { instance }));
                Assert.AreEqual(0, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_task_member WHERE InstanceId=@instance AND StatusKey='pending'", new { instance }));
                Assert.AreEqual(0, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_approval_slot WHERE InstanceId=@instance", new { instance }));
                Assert.AreEqual(snapshot, await db.ExecuteScalarAsync<string>("SELECT AssigneeSnapshotJson FROM fn_workflow_step WHERE InstanceId=@instance", new { instance }));
                var history = await RuntimeAsync(clients[0], first);
                Assert.AreEqual("after-first", history.GetProperty("submission").GetProperty("title").GetString());
                using (var input = JsonDocument.Parse((await db.ExecuteScalarAsync<string>("SELECT InputJson FROM fn_workflow_todo_view WHERE Id=@first", new { first }))!))
                    Assert.AreEqual("initial", input.RootElement.GetProperty("title").GetString(), "历史输出不得覆盖激活输入或借用后来办理人的结果。");
                if (mode == "any") Assert.AreEqual(2, history.GetProperty("taskProgress").GetProperty("cancelledCount").GetInt32());
            }
            await VerifyParallelAsync(owner, clients, people, db, formVersion);
            await VerifyTenantAsync(factory, db, owner);
            await RecoverAsync(provider, connectionString, db);
        }
        finally { foreach (var client in clients) client.Dispose(); }
    }

    private static async Task VerifyParallelAsync(HttpClient owner, HttpClient[] clients, List<HostTestIdentity> people, DbConnection db, Guid form)
    {
        var version = await PublishAsync(owner, form, [Node("start", "start", new { nextNodeKeys = new[] { "fork" } }),
            Node("fork", "gateway.parallel", new { gatewayRoleKey = "fork", joinNodeKey = "join", branches = new[] {
                new { branchKey = "left", nextNodeKey = "left" }, new { branchKey = "right", nextNodeKey = "right" } } }),
            Node("left", "human.task", new { nextNodeKeys = new[] { "join" }, taskPolicy = new { completionModeKey = "all" },
                assigneePolicy = Sources([people[0].UserId]), fieldPolicies = new { title = "readOnly", left = "editable", right = "readOnly" } }),
            Node("right", "human.task", new { nextNodeKeys = new[] { "join" }, taskPolicy = new { completionModeKey = "all" },
                assigneePolicy = Sources([people[1].UserId]), fieldPolicies = new { title = "readOnly", left = "readOnly", right = "editable" } }),
            Node("join", "gateway.parallel", new { gatewayRoleKey = "join", forkNodeKey = "fork", nextNodeKeys = new[] { "end" } }), Node("end", "end", new { })]);
        var instance = await StartAsync(owner, version, "parallel");
        var first = await TodoAsync(db, instance, people[0].UserId); var second = await TodoAsync(db, instance, people[1].UserId);
        await PostAsync(clients[0], $"/api/v1/workflow/todos/{first:D}/complete", new { expectedRevision = 1, expectedFormRevision = 1,
            fieldPatch = new { left = "left-output" }, idempotencyKey = "left" });
        await PostAsync(clients[1], $"/api/v1/workflow/todos/{second:D}/complete", new { expectedRevision = 1, expectedFormRevision = 1,
            fieldPatch = new { right = "right-output" }, idempotencyKey = "right" });
        var result = await db.ExecuteScalarAsync<string>("SELECT SubmissionJson FROM fn_workflow_form_submission WHERE InstanceId=@instance", new { instance });
        using var values = JsonDocument.Parse(result!); Assert.AreEqual("left-output", values.RootElement.GetProperty("left").GetString());
        Assert.AreEqual("right-output", values.RootElement.GetProperty("right").GetString());
        Assert.AreEqual("completed", await db.ExecuteScalarAsync<string>("SELECT StatusKey FROM fn_workflow_instance WHERE Id=@instance", new { instance }));
        var history = await RuntimeAsync(clients[0], first); Assert.AreEqual("right-initial", history.GetProperty("submission").GetProperty("right").GetString());
    }

    private static async Task VerifyTenantAsync(FullNetApiFactory factory, DbConnection db, HttpClient hostReader)
    {
        using var owner = factory.CreateClientForHost("localhost"); owner.DefaultRequestHeaders.Add("Origin", "http://localhost");
        var login = await PostAsync(owner, "/api/v1/auth/login", new LoginRequest("admin", FullNetApiFactory.TestPassword));
        var token = login.GetProperty("accessToken").GetString()!; owner.DefaultRequestHeaders.Authorization = new("Bearer", token);
        var tenants = await owner.GetFromJsonAsync<TenantContextSummary[]>("/api/v1/tenancy/available"); var tenant = tenants!.Single(value => value.Identifier == "acme");
        owner.DefaultRequestHeaders.Authorization = new("Bearer", await IntegrationTestTenantContextHelper.SwitchToTenantAsync(owner, token, tenant.Id));
        var members = new List<HostTestIdentity>(); var clients = new List<HttpClient>();
        try
        {
            for (var index = 0; index < 2; index++)
            {
                var name = "task-tenant-" + Guid.NewGuid().ToString("N"); var email = name + "@example.com";
                var user = await factory.CreateHostIdentityAsync(name, [WorkflowPermissions.TodosRead, WorkflowPermissions.TodosComplete, WorkflowPermissions.TodosSign, "tenancy.tenants.switch"], password: FullNetApiFactory.TestPassword);
                await factory.EnsureHostUserProfileEmailAsync(user.UserId, email, name); members.Add(user);
                var client = factory.CreateClientForHost("localhost"); clients.Add(client); client.DefaultRequestHeaders.Add("Origin", "http://localhost");
                var hostToken = await IntegrationTestAuthHelper.LoginAsHostUserAsync(client, name, FullNetApiFactory.TestPassword); client.DefaultRequestHeaders.Authorization = new("Bearer", hostToken);
                var invitation = await PostAsync(owner, "/api/v1/identity/tenant-members/invitations", new CreateTenantInvitationRequest(email, TenantMemberRoles.Member));
                await PostAsync(client, $"/api/v1/me/tenant-invitations/{invitation.GetProperty("invitation").GetProperty("id").GetGuid():D}/accept", new { });
                client.DefaultRequestHeaders.Authorization = new("Bearer", await IntegrationTestTenantContextHelper.SwitchToTenantAsync(client, hostToken, tenant.Id));
            }
            var form = await CreateFormAsync(owner); var version = await PublishAsync(owner, form,
                [Node("start", "start", new { nextNodeKeys = new[] { "task" } }), Node("task", "human.task", new { nextNodeKeys = new[] { "end" },
                    taskPolicy = new { completionModeKey = "all" }, assigneePolicy = Sources(members.Select(member => member.UserId).ToArray()), signRequired = true, commentRequired = true }), Node("end", "end", new { })]);
            var instance = await StartAsync(owner, version, "tenant"); var todo = await TodoAsync(db, instance, members[0].UserId);
            using (var foreign = await hostReader.GetAsync($"/api/v1/workflow/todos/{todo:D}/runtime")) Assert.AreEqual(HttpStatusCode.NotFound, foreign.StatusCode);
            for (var index = 0; index < 2; index++)
            {
                var current = await TodoAsync(db, instance, members[index].UserId); var runtime = await RuntimeAsync(clients[index], current);
                var file = await SignatureAsync(clients[index], current); var request = new { expectedRevision = 1, expectedFormRevision = runtime.GetProperty("submissionRevision").GetInt64(),
                    fieldPatch = new { }, comment = "提交确认", signatureFileId = file, idempotencyKey = "tenant-" + index };
                await PostAsync(clients[index], $"/api/v1/workflow/todos/{current:D}/complete", request);
            }
            Assert.AreEqual(2, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_action_evidence WHERE InstanceId=@instance AND ActionKey='complete' AND TenantScopeKey=@scope", new { instance, scope = $"tenant:{tenant.Id:N}" }));
            Assert.AreEqual("completed", await db.ExecuteScalarAsync<string>("SELECT StatusKey FROM fn_workflow_instance WHERE Id=@instance", new { instance }));
        }
        finally { foreach (var client in clients) client.Dispose(); }
    }

    private static async Task RecoverAsync(DatabaseProvider provider, string connection, DbConnection db)
    {
        await db.ExecuteAsync("DROP INDEX uq_fn_workflow_task_member_sequence ON fn_workflow_task_member");
        await db.ExecuteAsync(provider == DatabaseProvider.SqlServer ? "ALTER TABLE fn_workflow_task_member DROP CONSTRAINT ck_fn_workflow_task_member_state" : "ALTER TABLE fn_workflow_task_member DROP CHECK ck_fn_workflow_task_member_state");
        await db.ExecuteAsync(provider == DatabaseProvider.SqlServer ? "DELETE FROM dbo.SchemaVersions WHERE ScriptName LIKE '%300_WorkflowHumanTasks.sql'" : "DELETE FROM schemaversions WHERE ScriptName LIKE '%300_WorkflowHumanTasks.sql'");
        var result = provider == DatabaseProvider.SqlServer ? Migration114To126RecoveryTestSupport.MigrateSqlServer(connection, 300) : Migration114To126RecoveryTestSupport.MigrateMySql(connection, 300);
        Assert.IsTrue(result.Successful, result.Error?.Message);
        try
        {
            await db.ExecuteAsync("UPDATE fn_workflow_todo_transfer SET AuthorizationKey='workflow.todos.transfer' WHERE AuthorizationKey='workflow.instances.recover' AND ActorUserId<>FromAssigneeUserId");
            Assert.Fail("管理员改派不能伪装为本人转办授权。");
        }
        catch (DbException) { }
    }
    private static async Task<Guid> SignatureAsync(HttpClient client, Guid todo)
    {
        using var body = new MultipartFormDataContent(); var png = new ByteArrayContent(Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+j2e8AAAAASUVORK5CYII="));
        png.Headers.ContentType = new("image/png"); body.Add(png, "file", "signature.png");
        using var response = await client.PostAsync($"/api/v1/workflow/todos/{todo:D}/signature-uploads?expectedRevision=1", body);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("fileId").GetGuid();
    }
    private static async Task<Guid> CreateFormAsync(HttpClient client)
    {
        var form = await PostAsync(client, "/api/v1/workflow/forms", new { formKey = "task." + Guid.NewGuid().ToString("N"), draft = new { schemaVersion = 1, adapterVersion = 1,
            sections = new[] { new { sectionKey = "main", fields = new[] { "title", "left", "right" }.Select(key => new { fieldKey = key, fieldTypeKey = "text", required = false, constraints = new { } }).ToArray() } } } }, HttpStatusCode.Created);
        return (await PostAsync(client, $"/api/v1/workflow/forms/{form.GetProperty("id").GetGuid():D}/publish", new { expectedRevision = 1 })).GetProperty("id").GetGuid();
    }
    private static async Task<Guid> PublishAsync(HttpClient client, Guid form, object[] nodes)
    {
        var definition = await PostAsync(client, "/api/v1/workflow/definitions", new { definitionKey = "task." + Guid.NewGuid().ToString("N"), draft = new { schemaVersion = 1, workflowKindKey = "automation", nodes } }, HttpStatusCode.Created);
        return (await PostAsync(client, $"/api/v1/workflow/definitions/{definition.GetProperty("id").GetGuid():D}/publish", new { expectedRevision = 1, formVersionId = form })).GetProperty("id").GetGuid();
    }
    private static async Task<Guid> StartAsync(HttpClient client, Guid version, string key) => (await PostAsync(client, "/api/v1/workflow/instances", new {
        definitionVersionId = version, businessType = "task", businessId = key, initialValues = new { title = "initial", left = "left-initial", right = "right-initial" }, idempotencyKey = key }, HttpStatusCode.Created)).GetProperty("id").GetGuid();
    private static object Sources(Guid[] ids) => new { sources = new[] { new { resolverKindKey = "specified_users", userIds = ids } } };
    private static object Vote(JsonElement runtime, string key, string? title) => new { expectedRevision = runtime.GetProperty("revision").GetInt64(), expectedFormRevision = runtime.GetProperty("submissionRevision").GetInt64(),
        fieldPatch = title is null ? new Dictionary<string, string>() : new Dictionary<string, string> { ["title"] = title }, idempotencyKey = key };
    private static object Node(string key, string kind, object config) => new { nodeKey = key, nodeTypeKey = kind, nodeSchemaVersion = 1, config };
    private static Task<Guid> TodoAsync(DbConnection db, Guid instance, Guid actor) => db.ExecuteScalarAsync<Guid>("SELECT Id FROM fn_workflow_todo WHERE InstanceId=@instance AND AssigneeUserId=@actor AND StatusKey='active'", new { instance, actor });
    private static Task<JsonElement> RuntimeAsync(HttpClient client, Guid todo) => client.GetFromJsonAsync<JsonElement>($"/api/v1/workflow/todos/{todo:D}/runtime");
    private static HttpClient Client(FullNetApiFactory factory, HostTestIdentity user) { var client = factory.CreateClientForHost("localhost"); client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", user.AccessToken); return client; }
    private static async Task<JsonElement> PostAsync(HttpClient client, string path, object body, HttpStatusCode expected = HttpStatusCode.OK)
    { using var response = await client.PostAsJsonAsync(path, body); var text = await response.Content.ReadAsStringAsync(); Assert.AreEqual(expected, response.StatusCode, path + ": " + text); return JsonSerializer.Deserialize<JsonElement>(text); }
}
