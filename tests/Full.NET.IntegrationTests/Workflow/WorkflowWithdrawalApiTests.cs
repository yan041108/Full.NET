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

/// <summary>发起人按版本边界撤回，不能借管理员取消或旧回执绕过真实办理及副作用边界。</summary>
[TestClass]
public sealed class WorkflowWithdrawalApiTests
{
    [TestMethod]
    public async Task Withdrawal_with_sql_server() => await VerifyAsync(DatabaseProvider.SqlServer, await SharedDatabaseFixture.CreateSqlServerDatabaseAsync());
    [TestMethod]
    public async Task Withdrawal_with_mysql() => await VerifyAsync(DatabaseProvider.MySql, await SharedDatabaseFixture.CreateMySqlDatabaseAsync());
    private static async Task VerifyAsync(DatabaseProvider provider, string connectionString)
    {
        using var factory = new FullNetApiFactory(provider, connectionString); await factory.InitializeAsync(useSchemaTemplate: false);
        await using DbConnection db = provider == DatabaseProvider.SqlServer ? new SqlConnection(connectionString)
            : new MySqlConnection(Migration114To126RecoveryTestSupport.MySqlQueryConnectionString(connectionString));
        var starter = await factory.CreateHostIdentityAsync("withdraw-owner-" + Guid.NewGuid().ToString("N"),
            [WorkflowPermissions.FormsCreate, WorkflowPermissions.FormsPublish, WorkflowPermissions.DefinitionsCreate, WorkflowPermissions.DefinitionsPublish,
                WorkflowPermissions.InstancesStart, WorkflowPermissions.InstancesRead, WorkflowPermissions.InstancesWithdraw]);
        using var owner = Client(factory, starter);
        var people = new List<HostTestIdentity>();
        for (var index = 0; index < 2; index++) people.Add(await factory.CreateHostIdentityAsync("withdraw-actor-" + Guid.NewGuid().ToString("N"),
            [WorkflowPermissions.TodosRead, WorkflowPermissions.TodosComplete, WorkflowPermissions.ClaimPoolsClaim, WorkflowPermissions.InstancesRead,
                WorkflowPermissions.InstancesWithdraw, WorkflowPermissions.InstancesCancel, WorkflowPermissions.InstancesRecover]));
        var clients = people.Select(person => Client(factory, person)).ToArray();
        try
        {
            var form = await CreateFormAsync(owner);
            var disabled = await PublishAsync(owner, form, Graph(null, people.Select(person => person.UserId).ToArray()));
            var unavailable = await StartAsync(owner, disabled, "disabled");
            await PostAsync(owner, $"/api/v1/workflow/instances/{unavailable:D}/withdraw", new { expectedRevision = 1, idempotencyKey = "disabled" }, HttpStatusCode.Conflict);
            var version = await PublishAsync(owner, form, Graph(new { modeKey = "before_any_human_action" }, people.Select(person => person.UserId).ToArray()));
            var noWithdraw = await factory.CreateHostIdentityAsync("withdraw-no-permission-" + Guid.NewGuid().ToString("N"),
                [WorkflowPermissions.InstancesStart, WorkflowPermissions.InstancesCancel]);
            using var restricted = Client(factory, noWithdraw);
            var restrictedInstance = await StartAsync(restricted, version, "separate-permission");
            await PostAsync(restricted, $"/api/v1/workflow/instances/{restrictedInstance:D}/withdraw", new { expectedRevision = 1, idempotencyKey = "not-withdraw-authorized" }, HttpStatusCode.Forbidden);
            var instance = await StartAsync(owner, version, "before-first");
            var detail = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/workflow/instances/{instance:D}");
            Assert.IsTrue(detail.GetProperty("withdrawal").GetProperty("canWithdraw").GetBoolean());
            await PostAsync(owner, $"/api/v1/workflow/instances/{instance:D}/cancel", new { expectedRevision = 1, idempotencyKey = "not-admin", reason = "no cancel permission" }, HttpStatusCode.Forbidden);
            await PostAsync(clients[0], $"/api/v1/workflow/instances/{instance:D}/withdraw", new { expectedRevision = 1, idempotencyKey = "foreign" }, HttpStatusCode.Forbidden);
            var request = new { expectedRevision = 1, idempotencyKey = "withdraw-first", reason = "发起人主动撤回" };
            var receipts = await Task.WhenAll(PostAsync(owner, $"/api/v1/workflow/instances/{instance:D}/withdraw", request),
                PostAsync(owner, $"/api/v1/workflow/instances/{instance:D}/withdraw", request));
            Assert.AreEqual(receipts[0].GetRawText(), receipts[1].GetRawText());
            Assert.AreEqual("cancelled", receipts[0].GetProperty("statusKey").GetString());
            Assert.AreEqual(0, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_todo WHERE InstanceId=@instance AND StatusKey='active'", new { instance }));
            Assert.AreEqual(1, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_action_record WHERE InstanceId=@instance AND ActionKey='withdraw' AND ActorUserId=@actor AND TodoId IS NULL", new { instance, actor = starter.UserId }));
            Assert.AreEqual("cancelled", await db.ExecuteScalarAsync<string>("SELECT StatusKey FROM fn_workflow_task WHERE InstanceId=@instance", new { instance }));
            await PostAsync(owner, $"/api/v1/workflow/instances/{instance:D}/withdraw", new { expectedRevision = 1, idempotencyKey = "withdraw-first", reason = "different" }, HttpStatusCode.Conflict);

            var handled = await StartAsync(owner, version, "partly-handled");
            var todo = await TodoAsync(db, handled, people[0].UserId);
            await PostAsync(clients[0], $"/api/v1/workflow/todos/{todo:D}/complete", Vote(await RuntimeAsync(clients[0], todo), "handled-first", null));
            var active = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/workflow/instances/{handled:D}");
            Assert.IsFalse(active.GetProperty("withdrawal").GetProperty("canWithdraw").GetBoolean());
            await PostAsync(owner, $"/api/v1/workflow/instances/{handled:D}/withdraw", new { expectedRevision = active.GetProperty("revision").GetInt64(), idempotencyKey = "too-late" }, HttpStatusCode.Conflict);

            var stagedVersion = await PublishAsync(owner, form, [Node("start", "start", new { nextNodeKeys = new[] { "first" }, withdrawalPolicy = new { modeKey = "at_active_nodes", nodeKeys = new[] { "last" } } }),
                TaskNode("first", "last", [people[0].UserId]), TaskNode("last", "end", [people[1].UserId]), Node("end", "end", new { })]);
            var staged = await StartAsync(owner, stagedVersion, "selected-boundary");
            await PostAsync(owner, $"/api/v1/workflow/instances/{staged:D}/withdraw", new { expectedRevision = 1, idempotencyKey = "wrong-node" }, HttpStatusCode.Conflict);
            var first = await TodoAsync(db, staged, people[0].UserId);
            var completed = await PostAsync(clients[0], $"/api/v1/workflow/todos/{first:D}/complete", Vote(await RuntimeAsync(clients[0], first), "first-complete", null));
            await PostAsync(owner, $"/api/v1/workflow/instances/{staged:D}/withdraw", new { expectedRevision = completed.GetProperty("revision").GetInt64(), idempotencyKey = "selected-node" });

            var claimVersion = await PublishAsync(owner, form, [Node("start", "start", new { nextNodeKeys = new[] { "claim" }, withdrawalPolicy = new { modeKey = "before_any_human_action" } }),
                Node("claim", "human.claim", new { nextNodeKeys = new[] { "end" }, claimPolicy = new { allowRelease = true }, assigneePolicy = Sources([people[0].UserId]) }), Node("end", "end", new { })]);
            var unclaimed = await StartAsync(owner, claimVersion, "unclaimed");
            await PostAsync(owner, $"/api/v1/workflow/instances/{unclaimed:D}/withdraw", new { expectedRevision = 1, idempotencyKey = "withdraw-pool" });
            Assert.AreEqual("cancelled", await db.ExecuteScalarAsync<string>("SELECT StatusKey FROM fn_workflow_claim WHERE InstanceId=@unclaimed", new { unclaimed }));
            Assert.AreEqual(0, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_todo WHERE InstanceId=@unclaimed", new { unclaimed }));

            foreach (var both in new[] { false, true })
            {
                var parallelVersion = await PublishAsync(owner, form, [Node("start", "start", new { nextNodeKeys = new[] { "fork" },
                    withdrawalPolicy = new { modeKey = "at_active_nodes", nodeKeys = both ? new[] { "left", "right" } : new[] { "left" } } }),
                    Node("fork", "gateway.parallel", new { gatewayRoleKey = "fork", joinNodeKey = "join", branches = new[] {
                        new { branchKey = "left", nextNodeKey = "left" }, new { branchKey = "right", nextNodeKey = "right" } } }),
                    TaskNode("left", "join", [people[0].UserId]), TaskNode("right", "join", [people[1].UserId]),
                    Node("join", "gateway.parallel", new { gatewayRoleKey = "join", forkNodeKey = "fork", nextNodeKeys = new[] { "end" } }), Node("end", "end", new { })]);
                var parallel = await StartAsync(owner, parallelVersion, "parallel-" + both);
                await PostAsync(owner, $"/api/v1/workflow/instances/{parallel:D}/withdraw", new { expectedRevision = 1, idempotencyKey = "parallel-withdraw" },
                    both ? HttpStatusCode.OK : HttpStatusCode.Conflict);
                Assert.AreEqual(both ? 0 : 2, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_todo WHERE InstanceId=@parallel AND StatusKey='active'", new { parallel }));
            }

            var external = await StartAsync(owner, version, "effect-barrier");
            // 模拟未来外部活动已实际到达；不冒充已经验收连接器的执行或补偿协议。
            await db.ExecuteAsync("UPDATE fn_workflow_step SET NodeTypeKey='service.future' WHERE InstanceId=@external", new { external });
            await PostAsync(owner, $"/api/v1/workflow/instances/{external:D}/withdraw", new { expectedRevision = 1, idempotencyKey = "unsafe-effect" }, HttpStatusCode.Conflict);
        }
        finally { foreach (var client in clients) client.Dispose(); }
    }
    private static object[] Graph(object? withdrawal, Guid[] users) => [Node("start", "start", withdrawal is null
        ? (object)new { nextNodeKeys = new[] { "review" } } : new { nextNodeKeys = new[] { "review" }, withdrawalPolicy = withdrawal }),
        TaskNode("review", "end", users), Node("end", "end", new { })];
    private static object TaskNode(string key, string next, Guid[] users) => Node(key, "human.task", new { nextNodeKeys = new[] { next },
        taskPolicy = new { completionModeKey = "all" }, assigneePolicy = Sources(users) });
    private static async Task<Guid> CreateFormAsync(HttpClient client)
    {
        var form = await PostAsync(client, "/api/v1/workflow/forms", new { formKey = "withdraw." + Guid.NewGuid().ToString("N"), draft = new { schemaVersion = 1, adapterVersion = 1,
            sections = new[] { new { sectionKey = "main", fields = new[] { "title", "left", "right" }.Select(key => new { fieldKey = key, fieldTypeKey = "text", required = false, constraints = new { } }).ToArray() } } } }, HttpStatusCode.Created);
        return (await PostAsync(client, $"/api/v1/workflow/forms/{form.GetProperty("id").GetGuid():D}/publish", new { expectedRevision = 1 })).GetProperty("id").GetGuid();
    }
    private static async Task<Guid> PublishAsync(HttpClient client, Guid form, object[] nodes)
    {
        var definition = await PostAsync(client, "/api/v1/workflow/definitions", new { definitionKey = "withdraw." + Guid.NewGuid().ToString("N"), draft = new { schemaVersion = 1, workflowKindKey = "automation", nodes } }, HttpStatusCode.Created);
        return (await PostAsync(client, $"/api/v1/workflow/definitions/{definition.GetProperty("id").GetGuid():D}/publish", new { expectedRevision = 1, formVersionId = form })).GetProperty("id").GetGuid();
    }
    private static async Task<Guid> StartAsync(HttpClient client, Guid version, string key) => (await PostAsync(client, "/api/v1/workflow/instances", new {
        definitionVersionId = version, businessType = "withdraw", businessId = key, initialValues = new { title = "initial", left = "left-initial", right = "right-initial" }, idempotencyKey = key }, HttpStatusCode.Created)).GetProperty("id").GetGuid();
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
