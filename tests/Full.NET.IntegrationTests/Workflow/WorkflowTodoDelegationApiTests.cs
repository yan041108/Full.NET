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

/// <summary>代理与收回在真实数据库中竞争同一席位，不能扩权、刷新SLA或伪造原办理人的业务决定。</summary>
[TestClass]
public sealed class WorkflowTodoDelegationApiTests
{
    [TestMethod]
    public async Task Todo_delegation_with_sql_server() => await VerifyAsync(DatabaseProvider.SqlServer, await SharedDatabaseFixture.CreateSqlServerDatabaseAsync());
    [TestMethod]
    public async Task Todo_delegation_with_mysql() => await VerifyAsync(DatabaseProvider.MySql, await SharedDatabaseFixture.CreateMySqlDatabaseAsync());

    private static readonly string[] ActorPermissions = [WorkflowPermissions.TodosRead, WorkflowPermissions.TodosApprove,
        WorkflowPermissions.TodosReject, WorkflowPermissions.TodosComplete, WorkflowPermissions.TodosVote, WorkflowPermissions.TodosTransfer,
        WorkflowPermissions.TodosDelegate, WorkflowPermissions.TodosRecallDelegation, WorkflowPermissions.TodosCountersign,
        WorkflowPermissions.TodosReturn, WorkflowPermissions.TodosRelease, WorkflowPermissions.TodosSign, WorkflowPermissions.ClaimPoolsClaim];

    private static async Task VerifyAsync(DatabaseProvider provider, string connection)
    {
        using var factory = new FullNetApiFactory(provider, connection); await factory.InitializeAsync(useSchemaTemplate: false);
        await using DbConnection db = provider == DatabaseProvider.SqlServer ? new SqlConnection(connection)
            : new MySqlConnection(Migration114To126RecoveryTestSupport.MySqlQueryConnectionString(connection));
        // 模拟新约束已存在、旧约束未移除且309尚未记账的中断点，须在新代理数据写入前恢复。
        await db.ExecuteAsync("ALTER TABLE fn_workflow_todo_transfer ADD CONSTRAINT ck_fn_workflow_todo_transfer_handoff CHECK (Depth BETWEEN 1 AND 32 AND FormRevision>0 AND FromAssigneeUserId<>ToAssigneeUserId AND (AuthorizationKey='workflow.instances.recover' OR (AuthorizationKey='workflow.todos.transfer' AND ActorUserId=FromAssigneeUserId)))");
        await db.ExecuteAsync(provider == DatabaseProvider.SqlServer ? "DELETE FROM dbo.SchemaVersions WHERE ScriptName LIKE '%309_WorkflowTodoDelegation.sql'" : "DELETE FROM schemaversions WHERE ScriptName LIKE '%309_WorkflowTodoDelegation.sql'");
        await WorkflowHumanMigrationRecoveryTests.MigrateAsync(provider, connection);
        var owner = await factory.CreateHostIdentityAsync("delegate-owner-" + Guid.NewGuid().ToString("N"),
            [WorkflowPermissions.FormsCreate, WorkflowPermissions.FormsPublish, WorkflowPermissions.DefinitionsCreate,
                WorkflowPermissions.DefinitionsPublish, WorkflowPermissions.InstancesStart, WorkflowPermissions.InstancesRecover]);
        var people = new List<HostTestIdentity>();
        for (var i = 0; i < 4; i++) people.Add(await factory.CreateHostIdentityAsync("delegate-person-" + Guid.NewGuid().ToString("N"), ActorPermissions));
        var denied = await factory.CreateHostIdentityAsync("delegate-denied-" + Guid.NewGuid().ToString("N"), [WorkflowPermissions.TodosRead, WorkflowPermissions.TodosApprove]);
        using var ownerClient = Client(factory, owner); using var deniedClient = Client(factory, denied);
        var clients = people.Select(person => Client(factory, person)).ToArray();
        try
        {
            var form = await FormAsync(ownerClient);
            foreach (var mode in new[] { "all", "sequential", "group", "task", "vote", "claim" })
            {
                var version = await PublishAsync(ownerClient, form, people.Take(2).Select(person => person.UserId).ToArray(), mode);
                var instance = await StartAsync(ownerClient, version);
                var original = await OriginalTodoAsync(clients[0], db, instance, people[0].UserId, mode);
                var runtime = await RuntimeAsync(clients[0], original);
                var due = DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds());
                await db.ExecuteAsync("UPDATE fn_workflow_todo SET DueAtUtc=@due, ReminderCount=2, MaxReminderCount=3 WHERE Id=@original", new { due, original });
                await PostAsync(deniedClient, $"/api/v1/workflow/todos/{original:D}/delegate", Delegate(people[2].UserId, runtime, "no-permission"), HttpStatusCode.Forbidden);
                await PostAsync(clients[1], $"/api/v1/workflow/todos/{original:D}/delegate", Delegate(people[2].UserId, runtime, "not-owner"), HttpStatusCode.Forbidden);
                if (mode != "claim") await PostAsync(clients[0], $"/api/v1/workflow/todos/{original:D}/delegate", Delegate(people[1].UserId, runtime, "occupied"), HttpStatusCode.BadRequest);
                var body = Delegate(people[2].UserId, runtime, "grant");
                var granted = await Task.WhenAll(PostAsync(clients[0], $"/api/v1/workflow/todos/{original:D}/delegate", body),
                    PostAsync(clients[0], $"/api/v1/workflow/todos/{original:D}/delegate", body));
                Assert.AreEqual(granted[0].GetRawText(), granted[1].GetRawText()); var proxy = granted[0].GetProperty("activeTodoId").GetGuid();
                Assert.AreEqual(2, await db.ExecuteScalarAsync<int>("SELECT ReminderCount FROM fn_workflow_todo WHERE Id=@proxy AND DueAtUtc=@due", new { proxy, due }));
                await AssertProxyAuthorityAsync(clients[2], proxy, people[3].UserId, mode);
                var grant = (await clients[0].GetFromJsonAsync<JsonElement>($"/api/v1/workflow/todos/{original:D}/delegation")).GetProperty("delegation");
                Assert.IsTrue(grant.GetProperty("canRecall").GetBoolean()); Assert.AreEqual(people[0].UserId, grant.GetProperty("originalAssigneeUserId").GetGuid());
                var recall = new { expectedRevision = grant.GetProperty("proxyRevision").GetInt64(), expectedFormRevision = grant.GetProperty("formRevision").GetInt64(), idempotencyKey = "recall" };
                var recalled = await Task.WhenAll(PostAsync(clients[0], $"/api/v1/workflow/todos/{original:D}/delegation/recall", recall),
                    PostAsync(clients[0], $"/api/v1/workflow/todos/{original:D}/delegation/recall", recall));
                Assert.AreEqual(recalled[0].GetRawText(), recalled[1].GetRawText()); var restored = recalled[0].GetProperty("activeTodoId").GetGuid();
                Assert.AreNotEqual(original, restored); Assert.AreNotEqual(proxy, restored);
                Assert.AreEqual("delegate_recall", await db.ExecuteScalarAsync<string>("SELECT ResultActionKey FROM fn_workflow_todo WHERE Id=@proxy", new { proxy }));
                Assert.AreEqual(0, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_action_record WHERE InstanceId=@instance AND ActionKey IN ('approve','reject','complete','vote')", new { instance }));
                await CompleteAsync(clients[0], restored, mode, "restored"); await FinishOtherAsync(clients[1], db, instance, people[1].UserId, mode);
                Assert.AreEqual("completed", await db.ExecuteScalarAsync<string>("SELECT StatusKey FROM fn_workflow_instance WHERE Id=@instance", new { instance }));
                Assert.AreEqual(recalled[0].GetRawText(), (await PostAsync(clients[0], $"/api/v1/workflow/todos/{original:D}/delegation/recall", recall)).GetRawText());
                Assert.AreEqual(granted[0].GetRawText(), (await PostAsync(clients[0], $"/api/v1/workflow/todos/{original:D}/delegate", body)).GetRawText());

                // 另一实例由实际代理办结；其动作作者是代理本人，原委托不能随后收回或重新授权。
                var doneInstance = await StartAsync(ownerClient, version); var doneOrigin = await OriginalTodoAsync(clients[0], db, doneInstance, people[0].UserId, mode);
                var doneRuntime = await RuntimeAsync(clients[0], doneOrigin);
                var delegated = await PostAsync(clients[0], $"/api/v1/workflow/todos/{doneOrigin:D}/delegate", Delegate(people[2].UserId, doneRuntime, "finish-grant"));
                var doneProxy = delegated.GetProperty("activeTodoId").GetGuid(); await CompleteAsync(clients[2], doneProxy, mode, "proxy-done");
                await FinishOtherAsync(clients[1], db, doneInstance, people[1].UserId, mode);
                var closed = (await clients[0].GetFromJsonAsync<JsonElement>($"/api/v1/workflow/todos/{doneOrigin:D}/delegation")).GetProperty("delegation");
                Assert.IsFalse(closed.GetProperty("canRecall").GetBoolean()); Assert.AreEqual("completed", closed.GetProperty("statusKey").GetString());
                await PostAsync(clients[0], $"/api/v1/workflow/todos/{doneOrigin:D}/delegation/recall", new { expectedRevision = 1, expectedFormRevision = 1, idempotencyKey = "too-late" }, HttpStatusCode.Conflict);
                Assert.AreEqual(1, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_action_record WHERE InstanceId=@doneInstance AND TodoId=@doneProxy AND ActorUserId=@actor AND ActionKey=@action", new {
                    doneInstance, doneProxy, actor = people[2].UserId, action = mode == "vote" ? "vote" : mode is "task" or "claim" ? "complete" : "approve" }));
                if (mode == "all")
                {
                    await VerifyRaceAsync(ownerClient, clients, people, db, version);
                    var reassignedInstance = await StartAsync(ownerClient, version); var source = await TodoAsync(db, reassignedInstance, people[0].UserId);
                    var assignment = await PostAsync(clients[0], $"/api/v1/workflow/todos/{source:D}/delegate", Delegate(people[2].UserId, await RuntimeAsync(clients[0], source), "admin-grant"));
                    await PostAsync(ownerClient, $"/api/v1/workflow/instances/{reassignedInstance:D}/reassign", new { assigneeUserId = people[3].UserId,
                        expectedRevision = assignment.GetProperty("revision").GetInt64(), reason = "纠正代理", idempotencyKey = "correct-proxy",
                        todoId = assignment.GetProperty("activeTodoId").GetGuid(), expectedTodoRevision = 1 });
                    var invalidated = (await clients[0].GetFromJsonAsync<JsonElement>($"/api/v1/workflow/todos/{source:D}/delegation")).GetProperty("delegation");
                    Assert.IsFalse(invalidated.GetProperty("canRecall").GetBoolean()); Assert.AreEqual("reassigned", invalidated.GetProperty("statusKey").GetString());
                    await PostAsync(clients[0], $"/api/v1/workflow/todos/{source:D}/delegation/recall", new { expectedRevision = 1, expectedFormRevision = 1, idempotencyKey = "old-grant" }, HttpStatusCode.Conflict);
                }
            }
            await VerifyTenantAsync(factory, db, clients[0]);
            await RecoverAsync(provider, connection, db);
        }
        finally { foreach (var client in clients) client.Dispose(); }
    }

    private static async Task AssertProxyAuthorityAsync(HttpClient proxyClient, Guid proxy, Guid target, string mode)
    {
        var runtime = await RuntimeAsync(proxyClient, proxy);
        Assert.IsTrue(runtime.GetProperty("allowedActions").EnumerateArray().All(action => action.GetString() is "approve" or "reject" or "complete" or "vote" or "remind"));
        await PostAsync(proxyClient, $"/api/v1/workflow/todos/{proxy:D}/delegate", Delegate(target, runtime, "nested"), HttpStatusCode.Forbidden);
        await PostAsync(proxyClient, $"/api/v1/workflow/todos/{proxy:D}/transfer", Delegate(target, runtime, "transfer"), HttpStatusCode.Forbidden);
        using var candidates = await proxyClient.GetAsync($"/api/v1/workflow/todos/{proxy:D}/delegation-candidates"); Assert.AreEqual(HttpStatusCode.Forbidden, candidates.StatusCode);
        if (mode is "all" or "sequential" or "group")
        {
            using var targets = await proxyClient.GetAsync($"/api/v1/workflow/todos/{proxy:D}/return-targets"); Assert.AreEqual(HttpStatusCode.Forbidden, targets.StatusCode);
            await PostAsync(proxyClient, $"/api/v1/workflow/todos/{proxy:D}/countersign", new { directionKey = "before", assigneeUserIds = new[] { target }, expectedRevision = 1, idempotencyKey = "nested-consult" }, HttpStatusCode.Forbidden);
        }
        if (mode == "claim") await PostAsync(proxyClient, $"/api/v1/workflow/todos/{proxy:D}/release", new {
            expectedRevision = runtime.GetProperty("revision").GetInt64(), expectedPoolRevision = runtime.GetProperty("claimProgress").GetProperty("poolRevision").GetInt64(),
            expectedFormRevision = runtime.GetProperty("submissionRevision").GetInt64(), idempotencyKey = "proxy-release" }, HttpStatusCode.Forbidden);
    }

    private static async Task VerifyRaceAsync(HttpClient owner, HttpClient[] clients, List<HostTestIdentity> people, DbConnection db, Guid version)
    {
        var instance = await StartAsync(owner, version); var origin = await TodoAsync(db, instance, people[0].UserId);
        var grant = await PostAsync(clients[0], $"/api/v1/workflow/todos/{origin:D}/delegate", Delegate(people[2].UserId, await RuntimeAsync(clients[0], origin), "race-grant"));
        var proxy = grant.GetProperty("activeTodoId").GetGuid();
        var responses = await Task.WhenAll(clients[0].PostAsJsonAsync($"/api/v1/workflow/todos/{origin:D}/delegation/recall", new { expectedRevision = 1, expectedFormRevision = 1, idempotencyKey = "race-recall" }),
            clients[2].PostAsJsonAsync($"/api/v1/workflow/todos/{proxy:D}/approve", new { expectedRevision = 1, expectedFormRevision = 1, fieldPatch = new { }, idempotencyKey = "race-approve" }));
        try { Assert.AreEqual(1, responses.Count(response => response.StatusCode == HttpStatusCode.OK)); Assert.AreEqual(1, responses.Count(response => response.StatusCode == HttpStatusCode.Conflict)); }
        finally { foreach (var response in responses) response.Dispose(); }
        Assert.AreEqual(2, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_approval_slot WHERE InstanceId=@instance", new { instance }));
    }

    private static async Task VerifyTenantAsync(FullNetApiFactory factory, DbConnection db, HttpClient host)
    {
        using var owner = factory.CreateClientForHost("localhost"); owner.DefaultRequestHeaders.Add("Origin", "http://localhost");
        var login = await PostAsync(owner, "/api/v1/auth/login", new LoginRequest("admin", FullNetApiFactory.TestPassword));
        owner.DefaultRequestHeaders.Authorization = new("Bearer", login.GetProperty("accessToken").GetString());
        var tenants = await owner.GetFromJsonAsync<TenantContextSummary[]>("/api/v1/tenancy/available"); var tenant = tenants!.Single(item => item.Identifier == "acme");
        owner.DefaultRequestHeaders.Authorization = new("Bearer", await IntegrationTestTenantContextHelper.SwitchToTenantAsync(owner, login.GetProperty("accessToken").GetString()!, tenant.Id));
        var people = new List<HostTestIdentity>(); var clients = new List<HttpClient>();
        try
        {
            for (var i = 0; i < 3; i++)
            {
                var name = "tenant-delegate-" + Guid.NewGuid().ToString("N"); var email = name + "@example.com";
                var identity = await factory.CreateHostIdentityAsync(name, [.. ActorPermissions, "tenancy.tenants.switch"], password: FullNetApiFactory.TestPassword);
                await factory.EnsureHostUserProfileEmailAsync(identity.UserId, email, name); var client = factory.CreateClientForHost("localhost"); clients.Add(client);
                client.DefaultRequestHeaders.Add("Origin", "http://localhost"); var token = await IntegrationTestAuthHelper.LoginAsHostUserAsync(client, name, FullNetApiFactory.TestPassword);
                client.DefaultRequestHeaders.Authorization = new("Bearer", token);
                var invitation = await PostAsync(owner, "/api/v1/identity/tenant-members/invitations", new CreateTenantInvitationRequest(email, TenantMemberRoles.Member));
                await PostAsync(client, $"/api/v1/me/tenant-invitations/{invitation.GetProperty("invitation").GetProperty("id").GetGuid():D}/accept", new { });
                client.DefaultRequestHeaders.Authorization = new("Bearer", await IntegrationTestTenantContextHelper.SwitchToTenantAsync(client, token, tenant.Id)); people.Add(identity);
            }
            var form = await FormAsync(owner); var version = await PublishAsync(owner, form, people.Take(2).Select(person => person.UserId).ToArray(), "group", sign: true);
            var instance = await StartAsync(owner, version); var origin = await TodoAsync(db, instance, people[0].UserId); var runtime = await RuntimeAsync(clients[0], origin);
            using (var wrongScope = await host.GetAsync($"/api/v1/workflow/todos/{origin:D}/delegation")) Assert.AreEqual(HttpStatusCode.NotFound, wrongScope.StatusCode);
            var other = await TodoAsync(db, instance, people[1].UserId); var foreignSignature = await UploadSignatureAsync(clients[1], other, 1);
            await PostAsync(clients[0], $"/api/v1/workflow/todos/{origin:D}/delegate", Delegate(people[2].UserId, runtime, "foreign-sign", foreignSignature, "委托"), HttpStatusCode.BadRequest);
            var signature = await UploadSignatureAsync(clients[0], origin, 1);
            var delegated = await PostAsync(clients[0], $"/api/v1/workflow/todos/{origin:D}/delegate", Delegate(people[2].UserId, runtime, "tenant-grant", signature, "委托"));
            var proxy = delegated.GetProperty("activeTodoId").GetGuid();
            await AssertProxyAuthorityAsync(clients[2], proxy, people[0].UserId, "group");
            var recalled = await PostAsync(clients[0], $"/api/v1/workflow/todos/{origin:D}/delegation/recall", new { expectedRevision = 1, expectedFormRevision = 1, comment = "收回", idempotencyKey = "tenant-recall" });
            await CompleteAsync(clients[0], recalled.GetProperty("activeTodoId").GetGuid(), "group", "tenant-done"); await FinishOtherAsync(clients[1], db, instance, people[1].UserId, "group");
            Assert.AreEqual("completed", await db.ExecuteScalarAsync<string>("SELECT StatusKey FROM fn_workflow_instance WHERE Id=@instance", new { instance }));
            var signedInstance = await StartAsync(owner, version); var signedOrigin = await TodoAsync(db, signedInstance, people[0].UserId);
            var signedGrant = await PostAsync(clients[0], $"/api/v1/workflow/todos/{signedOrigin:D}/delegate", Delegate(people[2].UserId,
                await RuntimeAsync(clients[0], signedOrigin), "signed-grant", await UploadSignatureAsync(clients[0], signedOrigin, 1), "委托"));
            var signedProxy = signedGrant.GetProperty("activeTodoId").GetGuid(); await CompleteAsync(clients[2], signedProxy, "group", "proxy-signed");
            Assert.AreEqual(1, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_action_evidence WHERE TodoId=@signedProxy AND ActorUserId=@actor AND OriginalAssigneeUserId=@original AND ActionKey='approve'",
                new { signedProxy, actor = people[2].UserId, original = people[0].UserId }));
            await FinishOtherAsync(clients[1], db, signedInstance, people[1].UserId, "group");
        }
        finally { foreach (var client in clients) client.Dispose(); }
    }

    private static async Task RecoverAsync(DatabaseProvider provider, string connection, DbConnection db)
    {
        // 在已有代理数据上模拟309约束缺失且未记账；同时验证重复执行和恢复过程清理。
        await db.ExecuteAsync($"ALTER TABLE fn_workflow_todo_transfer DROP {(provider == DatabaseProvider.SqlServer ? "CONSTRAINT" : "CHECK")} ck_fn_workflow_todo_transfer_authority");
        var journal = provider == DatabaseProvider.SqlServer ? "dbo.SchemaVersions" : "schemaversions";
        await db.ExecuteAsync($"DELETE FROM {journal} WHERE ScriptName LIKE '%309_WorkflowTodoDelegation.sql'");
        await WorkflowHumanMigrationRecoveryTests.MigrateAsync(provider, connection); await WorkflowHumanMigrationRecoveryTests.MigrateAsync(provider, connection);
        Assert.AreEqual(1, provider == DatabaseProvider.SqlServer
            ? await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID('fn_workflow_todo_transfer') AND name='ck_fn_workflow_todo_transfer_authority'")
            : await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM information_schema.TABLE_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_todo_transfer' AND CONSTRAINT_NAME='ck_fn_workflow_todo_transfer_authority'"));
        if (provider == DatabaseProvider.MySql) Assert.AreEqual(0, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM information_schema.ROUTINES WHERE ROUTINE_SCHEMA=DATABASE() AND ROUTINE_NAME='fn_workflow_ensure_todo_delegation'"));
    }

    private static async Task<Guid> FormAsync(HttpClient client)
    {
        var form = await PostAsync(client, "/api/v1/workflow/forms", new { formKey = "delegate." + Guid.NewGuid().ToString("N"), draft = new { schemaVersion = 1, adapterVersion = 1,
            sections = new[] { new { sectionKey = "main", fields = new object[] { new { fieldKey = "title", fieldTypeKey = "text", required = false, constraints = new { } },
                new { fieldKey = "accepted", fieldTypeKey = "switch", required = false, constraints = new { } } } } } } }, HttpStatusCode.Created);
        return (await PostAsync(client, $"/api/v1/workflow/forms/{form.GetProperty("id").GetGuid():D}/publish", new { expectedRevision = 1 })).GetProperty("id").GetGuid();
    }
    private static async Task<Guid> PublishAsync(HttpClient client, Guid form, Guid[] users, string mode, bool sign = false)
    {
        object Sources(Guid[] members) => new { sources = new[] { new { resolverKindKey = "specified_users", userIds = members } } };
        var config = new Dictionary<string, object> { ["nextNodeKeys"] = new[] { "end" } };
        if (sign) { config["signRequired"] = true; config["commentRequired"] = true; }
        var kind = mode switch { "group" => "human.group_approval", "task" => "human.task", "vote" => "human.vote", "claim" => "human.claim", _ => "human.approval" };
        if (mode == "group") { config["groupApprovalPolicy"] = new { modeKey = "all" }; config["groups"] = users.Select((user, index) => new { groupKey = "g" + index, assigneePolicy = Sources([user]) }).ToArray(); }
        else if (mode is "all" or "sequential") config["approvalPolicy"] = new { modeKey = mode, approverUserIds = users };
        else
        {
            config["assigneePolicy"] = Sources(users);
            if (mode == "task") config["taskPolicy"] = new { completionModeKey = "all" };
            if (mode == "claim") config["claimPolicy"] = new { allowRelease = true };
            if (mode == "vote") config["votePolicy"] = new { ballotFormVersionId = form, participationPercentage = 100, approvalPercentage = 100, allowAbstain = true, resultFieldKey = "accepted" };
        }
        var definition = await PostAsync(client, "/api/v1/workflow/definitions", new { definitionKey = "delegate." + Guid.NewGuid().ToString("N"), draft = new { schemaVersion = 1,
            workflowKindKey = mode is "task" or "claim" ? "automation" : "approval",
            nodes = new[] { Node("start", "start", new { nextNodeKeys = new[] { "work" } }), Node("work", kind, config), Node("end", "end", new { }) } } }, HttpStatusCode.Created);
        return (await PostAsync(client, $"/api/v1/workflow/definitions/{definition.GetProperty("id").GetGuid():D}/publish", new { expectedRevision = 1, formVersionId = form })).GetProperty("id").GetGuid();
    }
    private static async Task<Guid> OriginalTodoAsync(HttpClient actor, DbConnection db, Guid instance, Guid user, string mode)
    {
        if (mode != "claim") return await TodoAsync(db, instance, user);
        var pool = await db.ExecuteScalarAsync<Guid>("SELECT Id FROM fn_workflow_claim WHERE InstanceId=@instance", new { instance });
        return (await PostAsync(actor, $"/api/v1/workflow/claim-pools/{pool:D}/claim", new { expectedRevision = 1, idempotencyKey = "claim" })).GetProperty("activeTodoId").GetGuid();
    }
    private static async Task CompleteAsync(HttpClient actor, Guid todo, string mode, string key)
    {
        var runtime = await RuntimeAsync(actor, todo);
        Guid? signature = runtime.GetProperty("signRequired").GetBoolean() ? await UploadSignatureAsync(actor, todo, runtime.GetProperty("revision").GetInt64()) : null;
        if (mode == "vote") await PostAsync(actor, $"/api/v1/workflow/todos/{todo:D}/vote", new { expectedRevision = runtime.GetProperty("revision").GetInt64(),
            expectedBallotRevision = runtime.GetProperty("voteForm").GetProperty("revision").GetInt64(), choiceKey = "yes", values = new { title = key }, comment = key, signatureFileId = signature, idempotencyKey = key });
        else await PostAsync(actor, $"/api/v1/workflow/todos/{todo:D}/{(mode is "task" or "claim" ? "complete" : "approve")}", new {
            expectedRevision = runtime.GetProperty("revision").GetInt64(), expectedFormRevision = runtime.GetProperty("submissionRevision").GetInt64(), fieldPatch = new { }, comment = key, signatureFileId = signature, idempotencyKey = key });
    }
    private static async Task FinishOtherAsync(HttpClient other, DbConnection db, Guid instance, Guid user, string mode)
    { if (mode != "claim") await CompleteAsync(other, await TodoAsync(db, instance, user), mode, "other-" + Guid.NewGuid().ToString("N")); }
    private static Task<Guid> TodoAsync(DbConnection db, Guid instance, Guid actor) => db.ExecuteScalarAsync<Guid>("SELECT Id FROM fn_workflow_todo WHERE InstanceId=@instance AND AssigneeUserId=@actor AND StatusKey='active'", new { instance, actor });
    private static async Task<Guid> StartAsync(HttpClient client, Guid version) => (await PostAsync(client, "/api/v1/workflow/instances", new { definitionVersionId = version,
        businessType = "delegation", businessId = Guid.NewGuid().ToString("N"), initialValues = new { title = "initial", accepted = false }, idempotencyKey = Guid.NewGuid().ToString("N") }, HttpStatusCode.Created)).GetProperty("id").GetGuid();
    private static object Delegate(Guid target, JsonElement runtime, string key, Guid? signature = null, string? comment = null) => new { assigneeUserId = target, expectedRevision = runtime.GetProperty("revision").GetInt64(),
        expectedFormRevision = runtime.GetProperty("submissionRevision").GetInt64(), idempotencyKey = key, signatureFileId = signature, comment };
    private static async Task<Guid> UploadSignatureAsync(HttpClient client, Guid todo, long revision)
    {
        using var upload = new MultipartFormDataContent(); var bytes = new ByteArrayContent(Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+j2e8AAAAASUVORK5CYII="));
        bytes.Headers.ContentType = new MediaTypeHeaderValue("image/png"); upload.Add(bytes, "file", "signature.png");
        using var response = await client.PostAsync($"/api/v1/workflow/todos/{todo:D}/signature-uploads?expectedRevision={revision}", upload);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync()); return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("fileId").GetGuid();
    }
    private static Task<JsonElement> RuntimeAsync(HttpClient client, Guid todo) => client.GetFromJsonAsync<JsonElement>($"/api/v1/workflow/todos/{todo:D}/runtime");
    private static object Node(string key, string kind, object config) => new { nodeKey = key, nodeTypeKey = kind, nodeSchemaVersion = 1, config };
    private static HttpClient Client(FullNetApiFactory factory, HostTestIdentity identity) { var client = factory.CreateClientForHost("localhost"); client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", identity.AccessToken); return client; }
    private static async Task<JsonElement> PostAsync(HttpClient client, string path, object body, HttpStatusCode expected = HttpStatusCode.OK)
    {
        using var response = await client.PostAsJsonAsync(path, body); var text = await response.Content.ReadAsStringAsync(); Assert.AreEqual(expected, response.StatusCode, path + " " + text);
        using var json = JsonDocument.Parse(text); return json.RootElement.Clone();
    }
}
