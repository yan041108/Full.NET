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

/// <summary>双库通过真实多人/组内顺序和或签验证加签只约束原席，附属动作不扩大固定分母。</summary>
[TestClass]
public sealed class WorkflowMultiCountersignApiTests
{
    [TestMethod]
    public async Task Multi_countersign_with_sql_server() => await VerifyAsync(DatabaseProvider.SqlServer, await SharedDatabaseFixture.CreateSqlServerDatabaseAsync());
    [TestMethod]
    public async Task Multi_countersign_with_mysql() => await VerifyAsync(DatabaseProvider.MySql, await SharedDatabaseFixture.CreateMySqlDatabaseAsync());
    private static async Task VerifyAsync(DatabaseProvider provider, string connection)
    {
        using var factory = new FullNetApiFactory(provider, connection); await factory.InitializeAsync(useSchemaTemplate: false);
        await using DbConnection db = provider == DatabaseProvider.SqlServer ? new SqlConnection(connection) : new MySqlConnection(Migration114To126RecoveryTestSupport.MySqlQueryConnectionString(connection));
        var owner = await factory.CreateHostIdentityAsync("consult-owner-" + Guid.NewGuid().ToString("N"), [WorkflowPermissions.FormsCreate, WorkflowPermissions.FormsPublish,
            WorkflowPermissions.DefinitionsCreate, WorkflowPermissions.DefinitionsPublish, WorkflowPermissions.InstancesStart, WorkflowPermissions.InstancesRead, WorkflowPermissions.TodosRead, WorkflowPermissions.TodosCountersign]);
        using var starter = Client(factory, owner); var identities = new List<HostTestIdentity>(); var clients = new List<HttpClient>();
        try
        {
            for (var index = 0; index < 4; index++)
            {
                var actor = await factory.CreateHostIdentityAsync("consult-" + index + "-" + Guid.NewGuid().ToString("N"), [WorkflowPermissions.TodosRead, WorkflowPermissions.TodosApprove, WorkflowPermissions.TodosReject, WorkflowPermissions.TodosCountersign, WorkflowPermissions.TodosReturn]);
                identities.Add(actor); clients.Add(Client(factory, actor));
            }
            var users = identities.Select(actor => actor.UserId).ToArray(); var handlers = clients.ToArray(); var form = await FormAsync(starter);
            foreach (var mode in new[] { "all", "sequential", "groupSeq" }) await BeforeAsync(db, starter, form, handlers, users, mode);
            await AfterAsync(db, starter, form, handlers, users);
            foreach (var direction in new[] { "before", "after" }) await RejectAsync(db, starter, form, handlers, users, direction, "any");
            await RejectAsync(db, starter, form, handlers, users, "after", "groupAny");
            await EarlyAnyAsync(db, starter, form, handlers, users);
            await OwnRejectAsync(db, starter, form, handlers, users);
            await CancelAsync(db, starter, form, handlers, users, "before");
            await CancelAsync(db, starter, form, handlers, users, "after");
            await TenantAsync(factory, db, starter, users[3]);
        }
        finally { foreach (var client in clients) client.Dispose(); }
    }
    private static async Task BeforeAsync(DbConnection db, HttpClient starter, Guid form, HttpClient[] clients, Guid[] users, string mode,
        HttpClient? foreign = null, Guid? invalidRecipient = null)
    {
        var instance = await StartAsync(starter, form, users, mode); var original = await TodoAsync(db, instance, users[0]);
        if (foreign is not null) { using var probe = await foreign.GetAsync($"/api/v1/workflow/todos/{original:D}/countersign-candidates?page=1&pageSize=20"); Assert.AreEqual(HttpStatusCode.NotFound, probe.StatusCode); }
        if (invalidRecipient is { } invalid) await ConfigureAsync(clients[0], original, "before", invalid, "foreign", HttpStatusCode.BadRequest);
        var candidates = await clients[0].GetFromJsonAsync<JsonElement>($"/api/v1/workflow/todos/{original:D}/countersign-candidates?page=1&pageSize=20");
        Assert.AreEqual(20, candidates.GetProperty("pageSize").GetInt32());
        await ConfigureAsync(clients[0], original, "before", users[3], "before");
        Assert.AreEqual("before_countersign", await db.ExecuteScalarAsync<string>("SELECT StatusKey FROM fn_workflow_todo WHERE Id=@original", new { original }));
        if (mode == "all" || mode == "group") await ActAsync(clients[1], await TodoAsync(db, instance, users[1]), "approve", "other");
        if (mode == "groupSeq") await ActAsync(clients[2], await TodoAsync(db, instance, users[2]), "approve", "other-group");
        var supplemental = await TodoAsync(db, instance, users[3]);
        if (foreign is null) await SupplementalAuthorityAsync(db, clients[3], supplemental, users[1]);
        await ActAsync(clients[3], supplemental, "approve", "consult");
        var resumed = await TodoAsync(db, instance, users[0]); Assert.AreNotEqual(original, resumed);
        if (mode.StartsWith("group", StringComparison.Ordinal))
        {
            var previous = await clients[0].GetFromJsonAsync<JsonElement>($"/api/v1/workflow/todos/{original:D}/runtime");
            Assert.AreEqual("cancelled", previous.GetProperty("statusKey").GetString());
            Assert.AreEqual(0, previous.GetProperty("approvalGroups").EnumerateArray().Count(group => group.GetProperty("isCurrentGroup").GetBoolean()));
        }
        await AssertSlotsAsync(db, instance, mode == "groupSeq" ? 3 : 2);
        await ActAsync(clients[0], resumed, "approve", "origin");
        if (mode is "sequential" or "groupSeq") await ActAsync(clients[1], await TodoAsync(db, instance, users[1]), "approve", "next-original");
        Assert.AreEqual("completed", await StatusAsync(db, instance));
    }
    private static async Task AfterAsync(DbConnection db, HttpClient starter, Guid form, HttpClient[] clients, Guid[] users)
    {
        var instance = await StartAsync(starter, form, users, "all"); var original = await TodoAsync(db, instance, users[0]);
        await ConfigureAsync(clients[0], original, "after", users[3], "after");
        await ActAsync(clients[0], original, "approve", "origin-approved");
        Assert.AreEqual(0, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_approval_slot WHERE InstanceId=@instance AND AssigneeUserId=@actor AND DecisionKey IS NOT NULL", new { instance, actor = users[0] }));
        await ActAsync(clients[3], await TodoAsync(db, instance, users[3]), "approve", "consult-approved");
        Assert.AreEqual("active", await StatusAsync(db, instance)); await AssertSlotsAsync(db, instance, 2);
        await ActAsync(clients[1], await TodoAsync(db, instance, users[1]), "approve", "other-approved");
        Assert.AreEqual("completed", await StatusAsync(db, instance));
    }
    private static async Task RejectAsync(DbConnection db, HttpClient starter, Guid form, HttpClient[] clients, Guid[] users, string direction, string mode)
    {
        var instance = await StartAsync(starter, form, users, mode); var original = await TodoAsync(db, instance, users[0]);
        await ConfigureAsync(clients[0], original, direction, users[3], "reject-chain");
        if (direction == "after") await ActAsync(clients[0], original, "approve", "approve-before-consult");
        if (mode == "groupAny") await ActAsync(clients[2], await TodoAsync(db, instance, users[2]), "approve", "other-group");
        await ActAsync(clients[3], await TodoAsync(db, instance, users[3]), "reject", "consult-rejected");
        Assert.AreEqual("active", await StatusAsync(db, instance));
        Assert.AreEqual(1, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_action_record WHERE InstanceId=@instance AND ActorUserId=@signer AND ActionKey='reject'", new { instance, signer = users[3] }));
        Assert.AreEqual(0, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_action_record WHERE InstanceId=@instance AND ActorUserId=@origin AND ActionKey='reject'", new { instance, origin = users[0] }));
        Assert.AreEqual(1, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_approval_slot WHERE InstanceId=@instance AND AssigneeUserId=@actor AND DecisionKey='reject'", new { instance, actor = users[0] }));
        await AssertSlotsAsync(db, instance, mode == "groupAny" ? 3 : 2);
        await ActAsync(clients[1], await TodoAsync(db, instance, users[1]), "approve", "remaining-approved");
        Assert.AreEqual("completed", await StatusAsync(db, instance));
    }
    private static async Task EarlyAnyAsync(DbConnection db, HttpClient starter, Guid form, HttpClient[] clients, Guid[] users)
    {
        var instance = await StartAsync(starter, form, users, "any"); var original = await TodoAsync(db, instance, users[0]);
        await ConfigureAsync(clients[0], original, "before", users[3], "early-any"); var supplemental = await TodoAsync(db, instance, users[3]);
        await ActAsync(clients[1], await TodoAsync(db, instance, users[1]), "approve", "winner");
        Assert.AreEqual("completed", await StatusAsync(db, instance));
        Assert.AreEqual(0, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_todo WHERE InstanceId=@instance AND StatusKey IN ('active','before_countersign')", new { instance }));
        Assert.AreEqual(0, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_countersign_chain WHERE InstanceId=@instance AND StatusKey='active'", new { instance }));
        await PostAsync(clients[3], $"/api/v1/workflow/todos/{supplemental:D}/approve", new { expectedRevision = 1, expectedFormRevision = 1, fieldPatch = new { }, idempotencyKey = "late-consult" }, HttpStatusCode.Conflict);
    }
    private static async Task OwnRejectAsync(DbConnection db, HttpClient starter, Guid form, HttpClient[] clients, Guid[] users)
    {
        var instance = await StartAsync(starter, form, users, "any"); var original = await TodoAsync(db, instance, users[0]);
        await ConfigureAsync(clients[0], original, "after", users[3], "own-reject");
        await ActAsync(clients[0], original, "reject", "original-reject"); Assert.AreEqual("active", await StatusAsync(db, instance));
        Assert.AreEqual(0, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_countersign_chain WHERE InstanceId=@instance AND StatusKey='active'", new { instance }));
        await ActAsync(clients[1], await TodoAsync(db, instance, users[1]), "approve", "other-vote"); Assert.AreEqual("completed", await StatusAsync(db, instance));
    }
    private static async Task CancelAsync(DbConnection db, HttpClient starter, Guid form, HttpClient[] clients, Guid[] users, string direction)
    {
        var instance = await StartAsync(starter, form, users, "all"); var original = await TodoAsync(db, instance, users[0]);
        await ConfigureAsync(clients[0], original, direction, users[3], "cancel-chain");
        if (direction == "after") await ActAsync(clients[0], original, "approve", "own-vote");
        await ActAsync(clients[1], await TodoAsync(db, instance, users[1]), "approve", "other-vote");
        var runtime = await clients[0].GetFromJsonAsync<JsonElement>($"/api/v1/workflow/todos/{original:D}/runtime");
        var request = new { expectedRevision = runtime.GetProperty("revision").GetInt64(), comment = "撤销剩余加签", idempotencyKey = "cancel-consult" };
        var result = await PostAsync(clients[0], $"/api/v1/workflow/todos/{original:D}/countersign/cancel", request);
        if (direction == "before") await ActAsync(clients[0], result.GetProperty("activeTodoId").GetGuid(), "approve", "resumed-vote");
        else { Assert.AreEqual("completed", result.GetProperty("statusKey").GetString()); Assert.AreEqual(JsonValueKind.Null, result.GetProperty("activeTodoId").ValueKind); }
        Assert.AreEqual("completed", await StatusAsync(db, instance));
        var replay = await PostAsync(clients[0], $"/api/v1/workflow/todos/{original:D}/countersign/cancel", request); Assert.AreEqual(result.GetRawText(), replay.GetRawText());
        await AssertSlotsAsync(db, instance, 2);
    }
    private static async Task TenantAsync(FullNetApiFactory factory, DbConnection db, HttpClient host, Guid foreignRecipient)
    {
        using var owner = factory.CreateClientForHost("localhost"); owner.DefaultRequestHeaders.Add("Origin", "http://localhost");
        var login = await PostAsync(owner, "/api/v1/auth/login", new LoginRequest("admin", FullNetApiFactory.TestPassword)); var token = login.GetProperty("accessToken").GetString()!;
        owner.DefaultRequestHeaders.Authorization = new("Bearer", token); var tenants = await owner.GetFromJsonAsync<TenantContextSummary[]>("/api/v1/tenancy/available"); var tenant = tenants!.Single(value => value.Identifier == "acme");
        owner.DefaultRequestHeaders.Authorization = new("Bearer", await IntegrationTestTenantContextHelper.SwitchToTenantAsync(owner, token, tenant.Id));
        var ownerId = (await owner.GetFromJsonAsync<JsonElement>("/api/v1/me")).GetProperty("id").GetGuid();
        var clients = new List<HttpClient>(); var users = new List<Guid>();
        try
        {
            for (var index = 0; index < 2; index++)
            {
                var username = "consult-tenant-" + Guid.NewGuid().ToString("N"); var email = username + "@example.com";
                var member = await factory.CreateHostIdentityAsync(username, [WorkflowPermissions.TodosRead, WorkflowPermissions.TodosApprove, "tenancy.tenants.switch"], password: FullNetApiFactory.TestPassword);
                await factory.EnsureHostUserProfileEmailAsync(member.UserId, email, username); var client = factory.CreateClientForHost("localhost"); clients.Add(client); users.Add(member.UserId); client.DefaultRequestHeaders.Add("Origin", "http://localhost");
                token = await IntegrationTestAuthHelper.LoginAsHostUserAsync(client, username, FullNetApiFactory.TestPassword); client.DefaultRequestHeaders.Authorization = new("Bearer", token);
                var invitation = await PostAsync(owner, "/api/v1/identity/tenant-members/invitations", new CreateTenantInvitationRequest(email, TenantMemberRoles.Member));
                await PostAsync(client, $"/api/v1/me/tenant-invitations/{invitation.GetProperty("invitation").GetProperty("id").GetGuid():D}/accept", new { });
                client.DefaultRequestHeaders.Authorization = new("Bearer", await IntegrationTestTenantContextHelper.SwitchToTenantAsync(client, token, tenant.Id));
            }
            await BeforeAsync(db, owner, await FormAsync(owner), [owner, clients[0], owner, clients[1]], [ownerId, users[0], ownerId, users[1]], "group", host, foreignRecipient);
        }
        finally { foreach (var client in clients) client.Dispose(); }
    }
    private static async Task ConfigureAsync(HttpClient client, Guid todo, string direction, Guid recipient, string key, HttpStatusCode expected = HttpStatusCode.OK)
    { var runtime = await client.GetFromJsonAsync<JsonElement>($"/api/v1/workflow/todos/{todo:D}/runtime"); await PostAsync(client, $"/api/v1/workflow/todos/{todo:D}/countersign", new { directionKey = direction, assigneeUserIds = new[] { recipient }, expectedRevision = runtime.GetProperty("revision").GetInt64(), comment = "补充审批意见", idempotencyKey = key }, expected); }
    private static async Task SupplementalAuthorityAsync(DbConnection db, HttpClient client, Guid todo, Guid recipient)
    {
        var runtime = await client.GetFromJsonAsync<JsonElement>($"/api/v1/workflow/todos/{todo:D}/runtime");
        var actions = runtime.GetProperty("allowedActions").EnumerateArray().Select(action => action.GetString()).ToArray();
        Assert.DoesNotContain("return", actions); Assert.DoesNotContain("countersign", actions); Assert.DoesNotContain("transfer", actions);
        if (runtime.GetProperty("approvalModeKey").GetString() == "group")
        {
            var expectedGroup = await db.ExecuteScalarAsync<Guid>("SELECT slot.GroupId FROM fn_workflow_approval_slot AS slot INNER JOIN fn_workflow_countersign_chain AS chain ON chain.OriginTodoId=slot.TodoId INNER JOIN fn_workflow_countersign_item AS item ON item.ChainId=chain.Id WHERE item.TodoId=@todo", new { todo });
            var current = runtime.GetProperty("approvalGroups").EnumerateArray().Where(group => group.GetProperty("isCurrentGroup").GetBoolean()).ToArray();
            Assert.HasCount(1, current); Assert.AreEqual(expectedGroup, current[0].GetProperty("id").GetGuid());
        }
        using var candidates = await client.GetAsync($"/api/v1/workflow/todos/{todo:D}/return-targets"); Assert.AreEqual(HttpStatusCode.Forbidden, candidates.StatusCode);
        await PostAsync(client, $"/api/v1/workflow/todos/{todo:D}/return", new { targetStepId = Guid.CreateVersion7(), expectedRevision = runtime.GetProperty("revision").GetInt64(), expectedFormRevision = runtime.GetProperty("submissionRevision").GetInt64(), fieldPatch = new { }, comment = "不能代原席退回", idempotencyKey = "forbidden-route" }, HttpStatusCode.Forbidden);
        await ConfigureAsync(client, todo, "before", recipient, "nested-consult", HttpStatusCode.Forbidden);
    }
    private static async Task ActAsync(HttpClient client, Guid todo, string action, string key)
    { var runtime = await client.GetFromJsonAsync<JsonElement>($"/api/v1/workflow/todos/{todo:D}/runtime"); await PostAsync(client, $"/api/v1/workflow/todos/{todo:D}/{action}", new { expectedRevision = runtime.GetProperty("revision").GetInt64(), expectedFormRevision = runtime.GetProperty("submissionRevision").GetInt64(), fieldPatch = new { }, idempotencyKey = key }); }
    private static async Task<Guid> StartAsync(HttpClient client, Guid form, Guid[] users, string mode)
    {
        object Group(string key, Guid[] members, string? inner = null) => inner is null ? new { groupKey = key, assigneePolicy = new { sources = new[] { new { resolverKindKey = "specified_users", userIds = members } } } }
            : new { groupKey = key, assigneePolicy = new { sources = new[] { new { resolverKindKey = "specified_users", userIds = members } } }, approvalPolicy = new { modeKey = inner, approverUserIds = Array.Empty<Guid>() } };
        var human = mode.StartsWith("group", StringComparison.Ordinal) ? Node("review", "human.group_approval", new { nextNodeKeys = new[] { "end" }, groupApprovalPolicy = new { modeKey = "all" }, groups = mode == "group" ? new[] { Group("a", [users[0]]), Group("b", [users[1]]) } : new[] { Group("a", [users[0], users[1]], mode == "groupSeq" ? "sequential" : "any"), Group("b", [users[2]]) } })
            : Node("review", "human.approval", new { nextNodeKeys = new[] { "end" }, approvalPolicy = new { modeKey = mode, approverUserIds = new[] { users[0], users[1] } } });
        var definition = await PostAsync(client, "/api/v1/workflow/definitions", new { definitionKey = "consult." + Guid.NewGuid().ToString("N"), draft = new { schemaVersion = 1, workflowKindKey = "approval", nodes = new[] { Node("start", "start", new { nextNodeKeys = new[] { "review" } }), human, Node("end", "end", new { }) } } }, HttpStatusCode.Created);
        var version = await PostAsync(client, $"/api/v1/workflow/definitions/{definition.GetProperty("id").GetGuid():D}/publish", new { expectedRevision = 1, formVersionId = form });
        return (await PostAsync(client, "/api/v1/workflow/instances", new { definitionVersionId = version.GetProperty("id").GetGuid(), businessType = "multi-consult", businessId = Guid.NewGuid().ToString("N"), initialValues = new { }, idempotencyKey = Guid.NewGuid().ToString("N") }, HttpStatusCode.Created)).GetProperty("id").GetGuid();
    }
    private static async Task<Guid> FormAsync(HttpClient client)
    { var form = await PostAsync(client, "/api/v1/workflow/forms", new { formKey = "consult." + Guid.NewGuid().ToString("N"), draft = new { schemaVersion = 1, adapterVersion = 1, sections = new[] { new { sectionKey = "main", fields = new[] { new { fieldKey = "title", fieldTypeKey = "text", required = false, constraints = new { } } } } } } }, HttpStatusCode.Created); return (await PostAsync(client, $"/api/v1/workflow/forms/{form.GetProperty("id").GetGuid():D}/publish", new { expectedRevision = 1 })).GetProperty("id").GetGuid(); }
    private static object Node(string key, string kind, object config) => new { nodeKey = key, nodeTypeKey = kind, nodeSchemaVersion = 1, config };
    private static async Task AssertSlotsAsync(DbConnection db, Guid instance, int count) => Assert.AreEqual(count, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_approval_slot WHERE InstanceId=@instance", new { instance }));
    private static Task<Guid> TodoAsync(DbConnection db, Guid instance, Guid user) => db.ExecuteScalarAsync<Guid>("SELECT Id FROM fn_workflow_todo WHERE InstanceId=@instance AND AssigneeUserId=@user AND StatusKey='active'", new { instance, user });
    private static Task<string?> StatusAsync(DbConnection db, Guid instance) => db.ExecuteScalarAsync<string>("SELECT StatusKey FROM fn_workflow_instance WHERE Id=@instance", new { instance });
    private static HttpClient Client(FullNetApiFactory factory, HostTestIdentity identity) { var client = factory.CreateClientForHost("localhost"); client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", identity.AccessToken); return client; }
    private static async Task<JsonElement> PostAsync(HttpClient client, string path, object request, HttpStatusCode expected = HttpStatusCode.OK)
    { using var response = await client.PostAsJsonAsync(path, request); var text = await response.Content.ReadAsStringAsync(); Assert.AreEqual(expected, response.StatusCode, path + ": " + text); return JsonSerializer.Deserialize<JsonElement>(text); }
}
