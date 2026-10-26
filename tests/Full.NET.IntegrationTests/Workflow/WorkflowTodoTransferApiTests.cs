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

/// <summary>转办在真实双库中交接同一席位；旧待办和表单输入不可覆写，并发不能增票。</summary>
[TestClass]
public sealed class WorkflowTodoTransferApiTests
{
    [TestMethod]
    public async Task Todo_transfer_with_sql_server() => await VerifyAsync(DatabaseProvider.SqlServer,
        await SharedDatabaseFixture.CreateSqlServerDatabaseAsync());
    [TestMethod]
    public async Task Todo_transfer_with_mysql() => await VerifyAsync(DatabaseProvider.MySql,
        await SharedDatabaseFixture.CreateMySqlDatabaseAsync());

    private static async Task VerifyAsync(DatabaseProvider provider, string connectionString)
    {
        using var factory = new FullNetApiFactory(provider, connectionString);
        await factory.InitializeAsync(useSchemaTemplate: false);
        await using DbConnection db = provider == DatabaseProvider.SqlServer ? new SqlConnection(connectionString)
            : new MySqlConnection(Migration114To126RecoveryTestSupport.MySqlQueryConnectionString(connectionString));
        var designer = await factory.CreateHostIdentityAsync("transfer-designer-" + Guid.NewGuid().ToString("N"),
            [WorkflowPermissions.FormsCreate, WorkflowPermissions.FormsPublish, WorkflowPermissions.DefinitionsCreate,
                WorkflowPermissions.DefinitionsPublish, WorkflowPermissions.InstancesStart]);
        var people = new List<HostTestIdentity>();
        for (var i = 0; i < 10; i++) people.Add(await factory.CreateHostIdentityAsync("transfer-actor-" + Guid.NewGuid().ToString("N"),
            [WorkflowPermissions.TodosRead, WorkflowPermissions.TodosApprove, WorkflowPermissions.TodosTransfer]));
        var denied = await factory.CreateHostIdentityAsync("transfer-denied-" + Guid.NewGuid().ToString("N"), [WorkflowPermissions.TodosRead]);
        using var designerClient = Client(factory, designer);
        using var deniedClient = Client(factory, denied);
        var clients = people.Select(person => Client(factory, person)).ToArray();
        try
        {
            var form = await PostAsync(designerClient, "/api/v1/workflow/forms", new { formKey = "transfer." + Guid.NewGuid().ToString("N"),
                draft = new { schemaVersion = 1, adapterVersion = 1, sections = new[] { new { sectionKey = "main", fields = new[] {
                    new { fieldKey = "title", fieldTypeKey = "text", required = false, constraints = new { } } } } } } }, HttpStatusCode.Created);
            var formVersion = await PostAsync(designerClient, $"/api/v1/workflow/forms/{form.GetProperty("id").GetGuid():D}/publish", new { expectedRevision = 1 });
            foreach (var mode in new[] { "all", "sequential" })
            {
                var definition = await PostAsync(designerClient, "/api/v1/workflow/definitions", new {
                    definitionKey = "transfer." + Guid.NewGuid().ToString("N"), draft = new { schemaVersion = 1, workflowKindKey = "approval", nodes = new[] {
                        Node("start", "start", new { nextNodeKeys = new[] { "review" } }),
                        Node("review", "human.approval", new { nextNodeKeys = new[] { "end" }, approvalPolicy = new {
                            modeKey = mode, approverUserIds = people.Take(2).Select(x => x.UserId).ToArray() } }),
                        Node("end", "end", new { }) } } }, HttpStatusCode.Created);
                var published = await PostAsync(designerClient, $"/api/v1/workflow/definitions/{definition.GetProperty("id").GetGuid():D}/publish",
                    new { expectedRevision = 1, formVersionId = formVersion.GetProperty("id").GetGuid() });
                async Task<JsonElement> StartAsync(string key) => await PostAsync(designerClient, "/api/v1/workflow/instances", new {
                    definitionVersionId = published.GetProperty("id").GetGuid(), businessType = "transfer", businessId = mode + key,
                    initialValues = new { title = "initial" }, idempotencyKey = mode + key }, HttpStatusCode.Created);
                var started = await StartAsync("-normal"); var instance = started.GetProperty("id").GetGuid();
                var original = await db.ExecuteScalarAsync<Guid>("SELECT Id FROM fn_workflow_todo WHERE InstanceId=@instance AND AssigneeUserId=@actor", new { instance, actor = people[0].UserId });
                var slot = await db.ExecuteScalarAsync<Guid>("SELECT Id FROM fn_workflow_approval_slot WHERE TodoId=@original", new { original });
                var frozen = await db.ExecuteScalarAsync<string>("SELECT AssigneeSnapshotJson FROM fn_workflow_step WHERE InstanceId=@instance", new { instance });
                using (var candidates = await clients[0].GetAsync($"/api/v1/workflow/todos/{original:D}/transfer-candidates")) Assert.AreEqual(HttpStatusCode.OK, candidates.StatusCode);
                using (var definitions = await clients[0].GetAsync("/api/v1/workflow/definitions")) Assert.AreEqual(HttpStatusCode.Forbidden, definitions.StatusCode);
                await PostAsync(deniedClient, $"/api/v1/workflow/todos/{original:D}/transfer", Transfer(people[2].UserId, "no-permission"), HttpStatusCode.Forbidden);
                await PostAsync(clients[1], $"/api/v1/workflow/todos/{original:D}/transfer", Transfer(people[2].UserId, "foreign"), HttpStatusCode.Forbidden);
                await PostAsync(clients[0], $"/api/v1/workflow/todos/{original:D}/transfer", Transfer(people[1].UserId, "occupied"), HttpStatusCode.BadRequest);
                // 已发生的催办和绝对截止属于原席位，转办不能刷新SLA或重复升级。
                var due = DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds());
                await db.ExecuteAsync("UPDATE fn_workflow_todo SET DueAtUtc=@due, MaxReminderCount=3, ReminderCount=2 WHERE Id=@original", new { due, original });
                var duplicate = await Task.WhenAll(PostAsync(clients[0], $"/api/v1/workflow/todos/{original:D}/transfer", Transfer(people[2].UserId, "handoff")),
                    PostAsync(clients[0], $"/api/v1/workflow/todos/{original:D}/transfer", Transfer(people[2].UserId, "handoff")));
                Assert.AreEqual(duplicate[0].GetRawText(), duplicate[1].GetRawText());
                var next = duplicate[0].GetProperty("activeTodoId").GetGuid();
                Assert.AreNotEqual(original, next);
                Assert.AreEqual(2, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_approval_slot WHERE InstanceId=@instance", new { instance }));
                Assert.AreEqual(next, await db.ExecuteScalarAsync<Guid>("SELECT TodoId FROM fn_workflow_approval_slot WHERE Id=@slot AND AssigneeUserId=@target AND DecisionKey IS NULL", new { slot, target = people[2].UserId }));
                Assert.AreEqual(2, await db.ExecuteScalarAsync<int>("SELECT ReminderCount FROM fn_workflow_todo WHERE Id=@next AND DueAtUtc=@due", new { next, due }));
                Assert.AreEqual(1, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_todo_transfer WHERE InstanceId=@instance AND FromTodoId=@original AND ToTodoId=@next AND OriginalAssigneeUserId=@actor AND ActorUserId=@actor AND Depth=1", new { instance, original, next, actor = people[0].UserId }));
                await AssertHistoryAsync(clients[0], original, "initial");
                using (var foreignOld = await clients[2].GetAsync($"/api/v1/workflow/todos/{original:D}/runtime")) Assert.AreEqual(HttpStatusCode.Forbidden, foreignOld.StatusCode);
                using (var foreignNew = await clients[0].GetAsync($"/api/v1/workflow/todos/{next:D}/runtime")) Assert.AreEqual(HttpStatusCode.Forbidden, foreignNew.StatusCode);
                await RecoverAsync(provider, connectionString, db);
                if (mode == "all")
                {
                    var other = await db.ExecuteScalarAsync<Guid>("SELECT Id FROM fn_workflow_todo WHERE InstanceId=@instance AND AssigneeUserId=@actor AND StatusKey='active'", new { instance, actor = people[1].UserId });
                    await PostAsync(clients[1], $"/api/v1/workflow/todos/{other:D}/approve", Vote(1, "other-done"));
                }
                var formRevision = mode == "all" ? 2 : 1;
                var onward = await PostAsync(clients[2], $"/api/v1/workflow/todos/{next:D}/transfer", Transfer(people[3].UserId, "onward", formRevision));
                var finalTodo = onward.GetProperty("activeTodoId").GetGuid();
                var detail = await clients[3].GetFromJsonAsync<JsonElement>($"/api/v1/workflow/todos/{finalTodo:D}/runtime");
                Assert.AreEqual(formRevision, detail.GetProperty("submissionRevision").GetInt64());
                Assert.AreEqual(mode == "all" ? "other-done" : "initial", detail.GetProperty("submission").GetProperty("title").GetString());
                await AssertHistoryAsync(clients[2], next, "initial");
                await PostAsync(clients[3], $"/api/v1/workflow/todos/{finalTodo:D}/transfer", Transfer(people[0].UserId, "cycle", formRevision), HttpStatusCode.BadRequest);
                var completed = await PostAsync(clients[3], $"/api/v1/workflow/todos/{finalTodo:D}/approve", Vote(formRevision, "final-done"));
                if (mode == "sequential")
                {
                    var last = completed.GetProperty("activeTodoId").GetGuid();
                    var lastDetail = await clients[1].GetFromJsonAsync<JsonElement>($"/api/v1/workflow/todos/{last:D}/runtime");
                    Assert.AreEqual("final-done", lastDetail.GetProperty("submission").GetProperty("title").GetString());
                    completed = await PostAsync(clients[1], $"/api/v1/workflow/todos/{last:D}/approve", Vote(2, "end"));
                }
                Assert.AreEqual("completed", completed.GetProperty("statusKey").GetString());
                Assert.AreEqual(2, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_approval_slot WHERE InstanceId=@instance AND DecisionKey='approve'", new { instance }));
                Assert.AreEqual(frozen, await db.ExecuteScalarAsync<string>("SELECT AssigneeSnapshotJson FROM fn_workflow_step WHERE InstanceId=@instance", new { instance }));
                var replay = await PostAsync(clients[0], $"/api/v1/workflow/todos/{original:D}/transfer", Transfer(people[2].UserId, "handoff"));
                Assert.AreEqual(duplicate[0].GetRawText(), replay.GetRawText());
                await VerifyCompetingKeysAsync(clients, people, db, await StartAsync("-race"));
                var chain = await StartAsync("-depth"); var chainInstance = chain.GetProperty("id").GetGuid();
                var chainTodo = await db.ExecuteScalarAsync<Guid>("SELECT Id FROM fn_workflow_todo WHERE InstanceId=@chainInstance AND AssigneeUserId=@actor", new { chainInstance, actor = people[0].UserId });
                var ownerIndex = 0;
                for (var depth = 1; depth <= 8; depth++)
                {
                    var transferred = await PostAsync(clients[ownerIndex], $"/api/v1/workflow/todos/{chainTodo:D}/transfer", Transfer(people[depth+1].UserId, "depth-" + depth));
                    chainTodo = transferred.GetProperty("activeTodoId").GetGuid(); ownerIndex = depth + 1;
                }
                await PostAsync(clients[ownerIndex], $"/api/v1/workflow/todos/{chainTodo:D}/transfer", Transfer(denied.UserId, "depth-nine"), HttpStatusCode.Conflict);
                Assert.AreEqual(8, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_todo_transfer WHERE InstanceId=@chainInstance", new { chainInstance }));
            }
            await VerifyTenantAsync(factory, db, deniedClient);
        }
        finally { foreach (var client in clients) client.Dispose(); }
    }

    private static async Task VerifyTenantAsync(FullNetApiFactory factory, DbConnection db, HttpClient hostReader)
    {
        using var owner = factory.CreateClientForHost("localhost");
        owner.DefaultRequestHeaders.Add("Origin", "http://localhost");
        var login = await PostAsync(owner, "/api/v1/auth/login", new LoginRequest("admin", FullNetApiFactory.TestPassword));
        owner.DefaultRequestHeaders.Authorization = new("Bearer", login.GetProperty("accessToken").GetString());
        var tenants = await owner.GetFromJsonAsync<TenantContextSummary[]>("/api/v1/tenancy/available");
        var tenant = tenants!.Single(x => x.Identifier == "acme");
        var ownerToken = await IntegrationTestTenantContextHelper.SwitchToTenantAsync(owner,
            login.GetProperty("accessToken").GetString()!, tenant.Id);
        owner.DefaultRequestHeaders.Authorization = new("Bearer", ownerToken);
        var users = new List<HostTestIdentity>(); var clients = new List<HttpClient>();
        try
        {
            // 通过Identity正式邀请/接受建立当前成员，不以Workflow夹具直接写其他模块表或旧角色关系。
            for (var i = 0; i < 3; i++)
            {
                var username = "tenant-transfer-" + Guid.NewGuid().ToString("N");
                var email = username + "@example.com";
                var identity = await factory.CreateHostIdentityAsync(username, [WorkflowPermissions.TodosRead,
                    WorkflowPermissions.TodosApprove, WorkflowPermissions.TodosTransfer, WorkflowPermissions.TodosSign, "tenancy.tenants.switch"],
                    password: FullNetApiFactory.TestPassword);
                await factory.EnsureHostUserProfileEmailAsync(identity.UserId, email, username);
                var client = factory.CreateClientForHost("localhost"); clients.Add(client);
                client.DefaultRequestHeaders.Add("Origin", "http://localhost");
                var hostToken = await IntegrationTestAuthHelper.LoginAsHostUserAsync(client, username, FullNetApiFactory.TestPassword);
                client.DefaultRequestHeaders.Authorization = new("Bearer", hostToken);
                var invitation = await PostAsync(owner, "/api/v1/identity/tenant-members/invitations",
                    new CreateTenantInvitationRequest(email, TenantMemberRoles.Member));
                var invitationId = invitation.GetProperty("invitation").GetProperty("id").GetGuid();
                await PostAsync(client, $"/api/v1/me/tenant-invitations/{invitationId:D}/accept", new { });
                var token = await IntegrationTestTenantContextHelper.SwitchToTenantAsync(client, hostToken, tenant.Id);
                client.DefaultRequestHeaders.Authorization = new("Bearer", token);
                users.Add(identity);
            }
            var form = await PostAsync(owner, "/api/v1/workflow/forms", new { formKey = "tenant.transfer." + Guid.NewGuid().ToString("N"),
                draft = new { schemaVersion = 1, adapterVersion = 1, sections = new[] { new { sectionKey = "main", fields = new[] {
                    new { fieldKey = "title", fieldTypeKey = "text", required = false, constraints = new { } } } } } } }, HttpStatusCode.Created);
            var publishedForm = await PostAsync(owner, $"/api/v1/workflow/forms/{form.GetProperty("id").GetGuid():D}/publish", new { expectedRevision = 1 });
            var definition = await PostAsync(owner, "/api/v1/workflow/definitions", new { definitionKey = "tenant.transfer." + Guid.NewGuid().ToString("N"),
                draft = new { schemaVersion = 1, workflowKindKey = "approval", nodes = new[] {
                    Node("start", "start", new { nextNodeKeys = new[] { "review" } }),
                    Node("review", "human.approval", new { nextNodeKeys = new[] { "end" }, signRequired = true, commentRequired = true,
                        approvalPolicy = new { modeKey = "all", approverUserIds = users.Take(2).Select(x => x.UserId).ToArray() } }),
                    Node("end", "end", new { }) } } }, HttpStatusCode.Created);
            var version = await PostAsync(owner, $"/api/v1/workflow/definitions/{definition.GetProperty("id").GetGuid():D}/publish",
                new { expectedRevision = 1, formVersionId = publishedForm.GetProperty("id").GetGuid() });
            var started = await PostAsync(owner, "/api/v1/workflow/instances", new { definitionVersionId = version.GetProperty("id").GetGuid(),
                businessType = "transfer", businessId = "tenant-signed", initialValues = new { title = "tenant initial" }, idempotencyKey = "tenant-signed" }, HttpStatusCode.Created);
            var instance = started.GetProperty("id").GetGuid();
            var todo = await db.ExecuteScalarAsync<Guid>("SELECT Id FROM fn_workflow_todo WHERE InstanceId=@instance AND AssigneeUserId=@actor", new { instance, actor = users[0].UserId });
            var otherTodo = await db.ExecuteScalarAsync<Guid>("SELECT Id FROM fn_workflow_todo WHERE InstanceId=@instance AND AssigneeUserId=@actor", new { instance, actor = users[1].UserId });
            var candidates = await clients[0].GetFromJsonAsync<JsonElement>($"/api/v1/workflow/todos/{todo:D}/transfer-candidates");
            Assert.IsTrue(candidates.GetProperty("items").EnumerateArray().Any(x => x.GetProperty("id").GetGuid() == users[2].UserId));
            using (var crossScope = await hostReader.GetAsync($"/api/v1/workflow/todos/{todo:D}/runtime")) Assert.AreEqual(HttpStatusCode.NotFound, crossScope.StatusCode);
            var otherSignature = await UploadSignatureAsync(clients[1], otherTodo);
            await PostAsync(clients[0], $"/api/v1/workflow/todos/{todo:D}/transfer", new { assigneeUserId = users[2].UserId,
                expectedRevision = 1, expectedFormRevision = 1, comment = "交接", idempotencyKey = "foreign-signature", signatureFileId = otherSignature }, HttpStatusCode.BadRequest);
            var signature = await UploadSignatureAsync(clients[0], todo);
            var request = new { assigneeUserId = users[2].UserId, expectedRevision = 1, expectedFormRevision = 1,
                comment = "交接", idempotencyKey = "tenant-transfer", signatureFileId = signature };
            var result = await PostAsync(clients[0], $"/api/v1/workflow/todos/{todo:D}/transfer", request);
            var next = result.GetProperty("activeTodoId").GetGuid();
            Assert.AreEqual(1, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_todo_transfer WHERE InstanceId=@instance AND TenantScopeKey=@scope AND OriginalAssigneeUserId=@actor AND ToAssigneeUserId=@target", new { instance, scope = $"tenant:{tenant.Id:N}", actor = users[0].UserId, target = users[2].UserId }));
            await AssertHistoryAsync(clients[0], todo, "tenant initial");
            var evidence = await clients[0].GetFromJsonAsync<JsonElement>($"/api/v1/workflow/todos/{todo:D}/signature-evidence");
            var evidenceId = evidence.EnumerateArray().Single().GetProperty("id").GetGuid();
            using (var content = await clients[0].GetAsync($"/api/v1/workflow/todos/{todo:D}/signature-evidence/{evidenceId:D}/content")) Assert.AreEqual(HttpStatusCode.OK, content.StatusCode);
            using (var foreignEvidence = await clients[2].GetAsync($"/api/v1/workflow/todos/{todo:D}/signature-evidence/{evidenceId:D}/content")) Assert.AreEqual(HttpStatusCode.Forbidden, foreignEvidence.StatusCode);
            var nextSignature = await UploadSignatureAsync(clients[2], next);
            await PostAsync(clients[2], $"/api/v1/workflow/todos/{next:D}/approve", new { expectedRevision = 1, expectedFormRevision = 1,
                fieldPatch = new { title = "tenant next" }, comment = "确认", signatureFileId = nextSignature, idempotencyKey = "tenant-next" });
            await PostAsync(clients[1], $"/api/v1/workflow/todos/{otherTodo:D}/approve", new { expectedRevision = 1, expectedFormRevision = 2,
                fieldPatch = new { }, comment = "确认", signatureFileId = otherSignature, idempotencyKey = "tenant-last" });
            var replay = await PostAsync(clients[0], $"/api/v1/workflow/todos/{todo:D}/transfer", request);
            Assert.AreEqual(result.GetRawText(), replay.GetRawText());
        }
        finally { foreach (var client in clients) client.Dispose(); }
    }

    private static async Task<Guid> UploadSignatureAsync(HttpClient client, Guid todo)
    {
        var png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+j2e8AAAAASUVORK5CYII=");
        using var upload = new MultipartFormDataContent();
        var bytes = new ByteArrayContent(png); bytes.Headers.ContentType = new MediaTypeHeaderValue("image/png"); upload.Add(bytes, "file", "signature.png");
        using var response = await client.PostAsync($"/api/v1/workflow/todos/{todo:D}/signature-uploads?expectedRevision=1", upload);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("fileId").GetGuid();
    }

    private static async Task VerifyCompetingKeysAsync(HttpClient[] clients, List<HostTestIdentity> people, DbConnection db, JsonElement started)
    {
        var instance = started.GetProperty("id").GetGuid();
        var todo = await db.ExecuteScalarAsync<Guid>("SELECT Id FROM fn_workflow_todo WHERE InstanceId=@instance AND AssigneeUserId=@actor", new { instance, actor = people[0].UserId });
        var results = await Task.WhenAll(clients[0].PostAsJsonAsync($"/api/v1/workflow/todos/{todo:D}/transfer", Transfer(people[2].UserId, "race-a")),
            clients[0].PostAsJsonAsync($"/api/v1/workflow/todos/{todo:D}/transfer", Transfer(people[3].UserId, "race-b")));
        try
        {
            CollectionAssert.AreEquivalent(new[] { HttpStatusCode.OK, HttpStatusCode.Conflict }, results.Select(x => x.StatusCode).ToArray());
            Assert.AreEqual(1, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_todo_transfer WHERE InstanceId=@instance", new { instance }));
            Assert.AreEqual(2, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_approval_slot WHERE InstanceId=@instance", new { instance }));
        }
        finally { foreach (var response in results) response.Dispose(); }
    }

    private static async Task RecoverAsync(DatabaseProvider provider, string connectionString, DbConnection db)
    {
        // MySQL外键必须始终有支撑索引；先补夹具支撑，再模拟最后一个唯一/检查步骤未记账。
        await db.ExecuteAsync("CREATE INDEX IX_fn_workflow_todo_transfer_RecoveryOrigin ON fn_workflow_todo_transfer(OriginTodoId)");
        await db.ExecuteAsync("DROP INDEX uq_fn_workflow_todo_transfer_origin_depth ON fn_workflow_todo_transfer");
        await db.ExecuteAsync(provider == DatabaseProvider.SqlServer
            ? "ALTER TABLE fn_workflow_todo_transfer DROP CONSTRAINT ck_fn_workflow_todo_transfer_authority"
            : "ALTER TABLE fn_workflow_todo_transfer DROP CHECK ck_fn_workflow_todo_transfer_authority");
        await db.ExecuteAsync(provider == DatabaseProvider.SqlServer ? "DELETE FROM dbo.SchemaVersions WHERE ScriptName LIKE '%298_WorkflowTodoTransfer.sql' OR ScriptName LIKE '%300_WorkflowHumanTasks.sql' OR ScriptName LIKE '%309_WorkflowTodoDelegation.sql'"
            : "DELETE FROM schemaversions WHERE ScriptName LIKE '%298_WorkflowTodoTransfer.sql' OR ScriptName LIKE '%300_WorkflowHumanTasks.sql' OR ScriptName LIKE '%309_WorkflowTodoDelegation.sql'");
        // 交接约束已经直接演进至309，恢复须走当前正式迁移，不能把300旧约束重新留在现行库。
        await WorkflowHumanMigrationRecoveryTests.MigrateAsync(provider, connectionString);
        await db.ExecuteAsync("DROP INDEX IX_fn_workflow_todo_transfer_RecoveryOrigin ON fn_workflow_todo_transfer");
    }
    private static async Task AssertHistoryAsync(HttpClient client, Guid todo, string title)
    {
        var history = await client.GetFromJsonAsync<JsonElement>($"/api/v1/workflow/todos/{todo:D}/runtime");
        Assert.AreEqual("completed", history.GetProperty("statusKey").GetString());
        Assert.AreEqual(title, history.GetProperty("submission").GetProperty("title").GetString());
        var list = await client.GetFromJsonAsync<JsonElement>("/api/v1/workflow/todos/mine/history?resultActionKey=transfer&pageSize=100");
        Assert.IsTrue(list.GetProperty("items").EnumerateArray().Any(item => item.GetProperty("id").GetGuid() == todo && item.GetProperty("resultActionKey").GetString() == "transfer"));
    }
    private static object Transfer(Guid target, string key, int formRevision = 1) => new { assigneeUserId = target,
        expectedRevision = 1, expectedFormRevision = formRevision, comment = (string?)null, idempotencyKey = key };
    private static object Vote(int revision, string value) => new { expectedRevision = 1, expectedFormRevision = revision,
        fieldPatch = new { title = value }, idempotencyKey = "vote-" + value };
    private static object Node(string key, string kind, object config) => new { nodeKey = key, nodeTypeKey = kind, nodeSchemaVersion = 1, config };
    private static HttpClient Client(FullNetApiFactory factory, HostTestIdentity identity)
    { var client = factory.CreateClientForHost("localhost"); client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", identity.AccessToken); return client; }
    private static async Task<JsonElement> PostAsync(HttpClient client, string path, object body, HttpStatusCode status = HttpStatusCode.OK)
    {
        using var response = await client.PostAsJsonAsync(path, body); var text = await response.Content.ReadAsStringAsync();
        Assert.AreEqual(status, response.StatusCode, path + " " + text); using var json = JsonDocument.Parse(text); return json.RootElement.Clone();
    }
}
