using System.Data.Common;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Dapper;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.IntegrationTests.Migrations;
using Full.NET.Modules.Workflow.Contracts;
using Microsoft.Data.SqlClient;
using MySqlConnector;

namespace Full.NET.IntegrationTests.Workflow;

/// <summary>真实依次审批必须按冻结席位逐人激活，不预造未来待办、输入或通知。</summary>
[TestClass]
public sealed class WorkflowSequentialApprovalApiTests
{
    [TestMethod]
    public async Task Sequential_approval_with_sql_server() => await VerifyAsync(DatabaseProvider.SqlServer,
        await SharedDatabaseFixture.CreateSqlServerDatabaseAsync());

    [TestMethod]
    public async Task Sequential_approval_with_mysql() => await VerifyAsync(DatabaseProvider.MySql,
        await SharedDatabaseFixture.CreateMySqlDatabaseAsync());

    private static async Task VerifyAsync(DatabaseProvider provider, string connectionString)
    {
        // 先恢复到上一版本；SQL Server 再模拟已经创建新索引但修改可空性失败的真实残留。
        if (provider == DatabaseProvider.SqlServer)
        {
            var before = Migration114To126RecoveryTestSupport.MigrateSqlServer(connectionString, 296);
            Assert.IsTrue(before.Successful, before.Error?.Message);
            await using var partial = new SqlConnection(connectionString);
            await partial.ExecuteAsync("CREATE UNIQUE INDEX UX_fn_workflow_approval_slot_BoundTodo ON dbo.fn_workflow_approval_slot(TodoId) WHERE TodoId IS NOT NULL");
            await partial.ExecuteAsync("ALTER TABLE dbo.fn_workflow_approval_slot DROP CONSTRAINT UQ_fn_workflow_approval_slot_Todo");
        }
        using var factory = new FullNetApiFactory(provider, connectionString);
        await factory.InitializeAsync(useSchemaTemplate: false);
        await using DbConnection db = provider == DatabaseProvider.SqlServer ? new SqlConnection(connectionString)
            : new MySqlConnection(Migration114To126RecoveryTestSupport.MySqlQueryConnectionString(connectionString));
        var designer = await factory.CreateHostIdentityAsync("seq-designer-" + Guid.NewGuid().ToString("N"),
            [WorkflowPermissions.FormsCreate, WorkflowPermissions.FormsPublish, WorkflowPermissions.DefinitionsCreate,
                WorkflowPermissions.DefinitionsRead, WorkflowPermissions.DefinitionsPublish, WorkflowPermissions.InstancesStart,
                WorkflowPermissions.InstancesCancel]);
        var first = await CreateActorAsync(factory); var second = await CreateActorAsync(factory); var third = await CreateActorAsync(factory);
        // 选择顺序故意不同于 UUID 创建顺序，不能依赖数据库排序重新排列席位。
        var actors = new[] { third, first, second };
        using var designerClient = Client(factory, designer);
        using var firstClient = Client(factory, actors[0]); using var secondClient = Client(factory, actors[1]); using var thirdClient = Client(factory, actors[2]);
        var clients = new[] { firstClient, secondClient, thirdClient };
        var preview = await PostAsync(designerClient, "/api/v1/workflow/definitions/assignee-preview", new {
            assigneePolicy = new { sources = new[] { new { resolverKindKey = "specified_users", userIds = new[] { actors[0].UserId } } } },
            initiatorUserId = (Guid?)null,
            additionalUserIds = actors.Skip(1).Select(x => x.UserId).ToArray() });
        CollectionAssert.AreEqual(actors.Select(x => x.UserId).ToArray(), preview.GetProperty("users").EnumerateArray().Select(x => x.GetProperty("id").GetGuid()).ToArray());
        var form = await PostAsync(designerClient, "/api/v1/workflow/forms", new {
            formKey = "sequential." + Guid.NewGuid().ToString("N"), draft = new { schemaVersion = 1, adapterVersion = 1,
                sections = new[] { new { sectionKey = "main", fields = new[] { new {
                    fieldKey = "first", fieldTypeKey = "text", required = false, constraints = new { } } } } } } }, HttpStatusCode.Created);
        var publishedForm = await PostAsync(designerClient, $"/api/v1/workflow/forms/{form.GetProperty("id").GetGuid():D}/publish", new { expectedRevision = 1 });
        var definition = await PostAsync(designerClient, "/api/v1/workflow/definitions", new {
            definitionKey = "sequential." + Guid.NewGuid().ToString("N"), draft = new { schemaVersion = 1, workflowKindKey = "approval", nodes = new[] {
                Node("start", "start", new { nextNodeKeys = new[] { "review" } }),
                Node("review", "human.approval", new { nextNodeKeys = new[] { "end" },
                    approvalPolicy = new { modeKey = "sequential", approverUserIds = actors.Select(x => x.UserId).ToArray() } }),
                Node("end", "end", new { }) } } }, HttpStatusCode.Created);
        var published = await PostAsync(designerClient, $"/api/v1/workflow/definitions/{definition.GetProperty("id").GetGuid():D}/publish",
            new { expectedRevision = 1, formVersionId = publishedForm.GetProperty("id").GetGuid() });
        var version = published.GetProperty("id").GetGuid();
        object Start(string key) => new { definitionVersionId = version, businessType = "sequential", businessId = key,
            initialValues = new { first = "initial" }, idempotencyKey = "seq-start-" + key };
        var started = await PostAsync(designerClient, "/api/v1/workflow/instances", Start("complete"), HttpStatusCode.Created);
        var instance = started.GetProperty("id").GetGuid(); var todo = started.GetProperty("activeTodoId").GetGuid();
        await AssertCountsAsync(db, instance, 1, 2, 1);
        var frozen = await db.ExecuteScalarAsync<string>("SELECT AssigneeSnapshotJson FROM fn_workflow_step WHERE InstanceId=@instance", new { instance });
        using (var snapshot = JsonDocument.Parse(frozen!))
            CollectionAssert.AreEqual(actors.Select(x => x.UserId).ToArray(), snapshot.RootElement.GetProperty("memberUserIds").EnumerateArray().Select(x => x.GetGuid()).ToArray());
        await VerifyRecoveryAsync(provider, connectionString, db);
        await AssertCountsAsync(db, instance, 1, 2, 1);
        var originalTodo = todo;
        for (var index = 0; index < actors.Length; index++)
        {
            var detail = await clients[index].GetFromJsonAsync<JsonElement>($"/api/v1/workflow/todos/{todo:D}/runtime");
            Assert.AreEqual("sequential", detail.GetProperty("approvalModeKey").GetString());
            Assert.AreEqual(index+1, detail.GetProperty("submissionRevision").GetInt64());
            Assert.AreEqual(index == 0 ? "initial" : $"step-{index}", detail.GetProperty("submission").GetProperty("first").GetString());
            if (index < 2)
            {
                var future = await clients[index+1].GetFromJsonAsync<JsonElement>("/api/v1/workflow/todos/mine");
                Assert.AreEqual(0, future.GetProperty("total").GetInt64());
                using var forbidden = await clients[index+1].GetAsync($"/api/v1/workflow/todos/{todo:D}/runtime");
                Assert.AreEqual(HttpStatusCode.Forbidden, forbidden.StatusCode);
            }
            var request = new { expectedRevision = 1, expectedFormRevision = index+1, fieldPatch = new { first = $"step-{index+1}" }, idempotencyKey = $"seq-vote-{index}" };
            var duplicate = await Task.WhenAll(
                PostAsync(clients[index], $"/api/v1/workflow/todos/{todo:D}/approve", request),
                PostAsync(clients[index], $"/api/v1/workflow/todos/{todo:D}/approve", request));
            var result = duplicate[0]; var replay = duplicate[1];
            Assert.AreEqual(result.GetRawText(), replay.GetRawText());
            Assert.AreEqual(index == 2 ? "completed" : "active", result.GetProperty("statusKey").GetString());
            await AssertCountsAsync(db, instance, Math.Min(index+2, 3), Math.Max(1-index, 0), index == 2 ? 0 : 1);
            if (index < 2) todo = result.GetProperty("activeTodoId").GetGuid();
        }
        Assert.AreEqual(3, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_domain_audit WHERE InstanceId=@instance AND OperationKey='todo.approve'", new { instance }));
        Assert.AreEqual(frozen, await db.ExecuteScalarAsync<string>("SELECT AssigneeSnapshotJson FROM fn_workflow_step WHERE InstanceId=@instance", new { instance }));
        var history = await firstClient.GetFromJsonAsync<JsonElement>($"/api/v1/workflow/todos/{originalTodo:D}/runtime");
        Assert.AreEqual("step-1", history.GetProperty("submission").GetProperty("first").GetString());
        foreach (var terminal in new[] { "reject", "cancel" })
        {
            var active = await PostAsync(designerClient, "/api/v1/workflow/instances", Start(terminal), HttpStatusCode.Created);
            var terminalInstance = active.GetProperty("id").GetGuid(); var activeTodo = active.GetProperty("activeTodoId").GetGuid();
            if (terminal == "reject")
                await PostAsync(firstClient, $"/api/v1/workflow/todos/{activeTodo:D}/reject", new {
                    expectedRevision = 1, expectedFormRevision = 1, fieldPatch = new { }, idempotencyKey = "seq-reject" });
            else await PostAsync(designerClient, $"/api/v1/workflow/instances/{terminalInstance:D}/cancel", new {
                expectedRevision = 1, reason = "关闭未激活席位", idempotencyKey = "seq-cancel" });
            await AssertCountsAsync(db, terminalInstance, 1, 0, 0, undecidedOnly: true);
            Assert.AreEqual(2, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_approval_slot WHERE InstanceId=@Id AND TodoId IS NULL AND DecisionKey='cancelled'", new { Id = terminalInstance }));
        }
    }

    private static async Task AssertCountsAsync(DbConnection db, Guid id, int todos, int unbound, int active, bool undecidedOnly = false)
    {
        Assert.AreEqual(todos, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_todo WHERE InstanceId=@id", new { id }));
        Assert.AreEqual(todos, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_todo_view WHERE InstanceId=@id", new { id }));
        Assert.AreEqual(active, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_todo WHERE InstanceId=@id AND StatusKey='active'", new { id }));
        Assert.AreEqual(unbound, await db.ExecuteScalarAsync<int>(undecidedOnly
            ? "SELECT COUNT(*) FROM fn_workflow_approval_slot WHERE InstanceId=@id AND DecisionKey IS NULL"
            : "SELECT COUNT(*) FROM fn_workflow_approval_slot WHERE InstanceId=@id AND TodoId IS NULL", new { id }));
    }

    private static async Task VerifyRecoveryAsync(DatabaseProvider provider, string connectionString, DbConnection db)
    {
        // 有两条未绑定席位时重跑半完成 DDL，不能回填历史顺序、丢失事实或临时拆外键。
        await db.ExecuteAsync("DROP INDEX UX_fn_workflow_approval_slot_Sequence ON fn_workflow_approval_slot");
        await db.ExecuteAsync(provider == DatabaseProvider.SqlServer
            ? "ALTER TABLE fn_workflow_approval_slot DROP CONSTRAINT CK_fn_workflow_approval_slot_Activation"
            : "ALTER TABLE fn_workflow_approval_slot DROP CHECK CK_fn_workflow_approval_slot_Activation");
        await db.ExecuteAsync(provider == DatabaseProvider.SqlServer
            ? "DELETE FROM dbo.SchemaVersions WHERE ScriptName LIKE '%297_WorkflowSequentialApproval.sql'"
            : "DELETE FROM schemaversions WHERE ScriptName LIKE '%297_WorkflowSequentialApproval.sql'");
        var migrated = provider == DatabaseProvider.SqlServer
            ? Migration114To126RecoveryTestSupport.MigrateSqlServer(connectionString, 297)
            : Migration114To126RecoveryTestSupport.MigrateMySql(connectionString, 297);
        Assert.IsTrue(migrated.Successful, migrated.Error?.Message);
    }

    private static Task<HostTestIdentity> CreateActorAsync(FullNetApiFactory factory) => factory.CreateHostIdentityAsync(
        "seq-actor-" + Guid.NewGuid().ToString("N"), [WorkflowPermissions.TodosRead, WorkflowPermissions.TodosApprove, WorkflowPermissions.TodosReject]);
    private static HttpClient Client(FullNetApiFactory factory, HostTestIdentity identity)
    {
        var client = factory.CreateClientForHost("localhost"); client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", identity.AccessToken); return client;
    }
    private static object Node(string key, string kind, object config) => new { nodeKey = key, nodeTypeKey = kind, nodeSchemaVersion = 1, config };
    private static async Task<JsonElement> PostAsync(HttpClient client, string path, object body, HttpStatusCode status = HttpStatusCode.OK)
    {
        using var response = await client.PostAsJsonAsync(path, body); var json = await response.Content.ReadAsStringAsync();
        Assert.AreEqual(status, response.StatusCode, path + " " + json); using var document = JsonDocument.Parse(json); return document.RootElement.Clone();
    }
}
