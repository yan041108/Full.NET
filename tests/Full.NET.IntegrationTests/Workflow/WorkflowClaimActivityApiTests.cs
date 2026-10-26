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
using Microsoft.Extensions.DependencyInjection;
using Full.NET.Modules.Workflow.Execution;
using MySqlConnector;

namespace Full.NET.IntegrationTests.Workflow;

/// <summary>真实候选竞争、释放、再领取与交接都绑定同一个候选池，并保留固定回执和SLA。</summary>
[TestClass]
public sealed class WorkflowClaimActivityApiTests
{
    [TestMethod]
    public async Task Claim_activity_with_sql_server() => await VerifyAsync(DatabaseProvider.SqlServer, await SharedDatabaseFixture.CreateSqlServerDatabaseAsync());
    [TestMethod]
    public async Task Claim_activity_with_mysql() => await VerifyAsync(DatabaseProvider.MySql, await SharedDatabaseFixture.CreateMySqlDatabaseAsync());
    private static async Task VerifyAsync(DatabaseProvider provider, string connectionString)
    {
        using var factory = new FullNetApiFactory(provider, connectionString); await factory.InitializeAsync(useSchemaTemplate: false);
        await using DbConnection db = provider == DatabaseProvider.SqlServer ? new SqlConnection(connectionString)
            : new MySqlConnection(Migration114To126RecoveryTestSupport.MySqlQueryConnectionString(connectionString));
        var designer = await factory.CreateHostIdentityAsync("claim-designer-" + Guid.NewGuid().ToString("N"),
            [WorkflowPermissions.FormsCreate, WorkflowPermissions.FormsPublish, WorkflowPermissions.DefinitionsCreate, WorkflowPermissions.DefinitionsPublish,
                WorkflowPermissions.InstancesStart, WorkflowPermissions.InstancesCancel, WorkflowPermissions.InstancesRecover, WorkflowPermissions.ClaimPoolsAssign, WorkflowPermissions.ClaimPoolsRead]);
        using var owner = Client(factory, designer);
        var people = new List<HostTestIdentity>();
        for (var index = 0; index < 3; index++) people.Add(await factory.CreateHostIdentityAsync("claim-person-" + Guid.NewGuid().ToString("N"),
            [WorkflowPermissions.ClaimPoolsRead, WorkflowPermissions.ClaimPoolsClaim, WorkflowPermissions.TodosRead, WorkflowPermissions.TodosComplete,
                WorkflowPermissions.TodosTransfer, WorkflowPermissions.TodosRelease, WorkflowPermissions.TodosSign]));
        var clients = people.Select(person => Client(factory, person)).ToArray();
        try
        {
            var form = await CreateFormAsync(owner);
            var version = await PublishAsync(owner, form, [Node("start", "start", new { nextNodeKeys = new[] { "claim" } }),
                Node("claim", "human.claim", new { nextNodeKeys = new[] { "end" }, claimPolicy = new { allowRelease = true },
                    assigneePolicy = Sources(people.Take(2).Select(person => person.UserId).ToArray()),
                    timeoutPolicy = new { dueAfterMinutes = 60, reminderIntervalMinutes = 10, maxReminderCount = 3 } }), Node("end", "end", new { })]);
            var instance = await StartAsync(owner, version, "claim-main");
            var pool = await db.ExecuteScalarAsync<Guid>("SELECT Id FROM fn_workflow_claim WHERE InstanceId=@instance", new { instance });
            var due = await db.ExecuteScalarAsync<DateTimeOffset>("SELECT DueAtUtc FROM fn_workflow_claim WHERE Id=@pool", new { pool });
            Assert.AreEqual(0, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_todo WHERE InstanceId=@instance", new { instance }));
            Assert.AreEqual(0, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_approval_slot WHERE InstanceId=@instance", new { instance }));
            var list = await clients[0].GetFromJsonAsync<JsonElement>("/api/v1/workflow/claim-pools");
            Assert.IsTrue(list.GetProperty("items").EnumerateArray().Any(item => item.GetProperty("id").GetGuid() == pool));
            var hidden = await owner.GetFromJsonAsync<JsonElement>("/api/v1/workflow/claim-pools");
            Assert.IsFalse(hidden.GetProperty("items").EnumerateArray().Any(item => item.GetProperty("id").GetGuid() == pool));
            await PostAsync(clients[2], $"/api/v1/workflow/claim-pools/{pool:D}/claim", new { expectedRevision = 1, idempotencyKey = "foreign-candidate" }, HttpStatusCode.Forbidden);
            var requests = new[] { new { expectedRevision = 1, idempotencyKey = "claim-one" }, new { expectedRevision = 1, idempotencyKey = "claim-two" } };
            var attempts = await Task.WhenAll(clients[0].PostAsJsonAsync($"/api/v1/workflow/claim-pools/{pool:D}/claim", requests[0]),
                clients[1].PostAsJsonAsync($"/api/v1/workflow/claim-pools/{pool:D}/claim", requests[1]));
            try
            {
                Assert.AreEqual(1, attempts.Count(response => response.StatusCode == HttpStatusCode.OK));
                Assert.AreEqual(1, attempts.Count(response => response.StatusCode == HttpStatusCode.Conflict));
                var winner = attempts[0].IsSuccessStatusCode ? 0 : 1; var other = 1 - winner;
                var first = await attempts[winner].Content.ReadFromJsonAsync<JsonElement>();
                var todo = first.GetProperty("activeTodoId").GetGuid();
                var replay = await PostAsync(clients[winner], $"/api/v1/workflow/claim-pools/{pool:D}/claim", requests[winner]);
                Assert.AreEqual(first.GetRawText(), replay.GetRawText());
                var runtime = await RuntimeAsync(clients[winner], todo);
                Assert.AreEqual("none", runtime.GetProperty("approvalModeKey").GetString());
                Assert.AreEqual(0, runtime.GetProperty("requiredApprovalCount").GetInt32());
                var releaseRequest = new { expectedRevision = 1, expectedPoolRevision = 2, expectedFormRevision = 1, idempotencyKey = "release-first" };
                await PostAsync(clients[other], $"/api/v1/workflow/todos/{todo:D}/release", releaseRequest, HttpStatusCode.Forbidden);
                var released = await PostAsync(clients[winner], $"/api/v1/workflow/todos/{todo:D}/release", releaseRequest);
                Assert.AreEqual(JsonValueKind.Null, released.GetProperty("activeTodoId").ValueKind);
                Assert.AreEqual("available", await db.ExecuteScalarAsync<string>("SELECT StatusKey FROM fn_workflow_claim WHERE Id=@pool", new { pool }));
                Assert.AreEqual(due, await db.ExecuteScalarAsync<DateTimeOffset>("SELECT DueAtUtc FROM fn_workflow_claim WHERE Id=@pool", new { pool }));
                var second = await PostAsync(clients[other], $"/api/v1/workflow/claim-pools/{pool:D}/claim", new { expectedRevision = 3, idempotencyKey = "claim-again" });
                var secondTodo = second.GetProperty("activeTodoId").GetGuid(); Assert.AreNotEqual(todo, secondTodo);
                var history = await RuntimeAsync(clients[winner], todo);
                Assert.AreEqual(1, history.GetProperty("claimProgress").GetProperty("claimCycle").GetInt32());
                Assert.IsFalse(history.GetProperty("claimProgress").GetProperty("allowRelease").GetBoolean());
                Assert.AreEqual(released.GetRawText(), (await PostAsync(clients[winner], $"/api/v1/workflow/todos/{todo:D}/release", releaseRequest)).GetRawText());
                Assert.AreEqual(first.GetRawText(), (await PostAsync(clients[winner], $"/api/v1/workflow/claim-pools/{pool:D}/claim", requests[winner])).GetRawText());
                var transfer = await PostAsync(clients[other], $"/api/v1/workflow/todos/{secondTodo:D}/transfer", new { assigneeUserId = people[2].UserId,
                    expectedRevision = 1, expectedFormRevision = 1, idempotencyKey = "claim-transfer" });
                var transferred = transfer.GetProperty("activeTodoId").GetGuid();
                Assert.AreEqual(1, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_todo_transfer WHERE InstanceId=@instance AND ClaimAttemptId IS NOT NULL AND SlotId IS NULL AND TaskMemberId IS NULL AND VoteBallotId IS NULL", new { instance }));
                var reassigned = await PostAsync(owner, $"/api/v1/workflow/instances/{instance:D}/reassign", new { todoId = transferred,
                    assigneeUserId = people[winner].UserId, expectedRevision = transfer.GetProperty("revision").GetInt64(), expectedTodoRevision = 1,
                    reason = "纠正领取后交接", idempotencyKey = "claim-reassign" });
                var last = reassigned.GetProperty("activeTodoId").GetGuid();
                var completed = await PostAsync(clients[winner], $"/api/v1/workflow/todos/{last:D}/complete", Vote(await RuntimeAsync(clients[winner], last), "claim-complete", "done"));
                Assert.AreEqual("completed", completed.GetProperty("statusKey").GetString());
                Assert.AreEqual("completed", await db.ExecuteScalarAsync<string>("SELECT StatusKey FROM fn_workflow_claim WHERE Id=@pool", new { pool }));
                Assert.AreEqual(1, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_claim_attempt WHERE PoolId=@pool AND StatusKey='released'", new { pool }));
                Assert.AreEqual(1, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_claim_attempt WHERE PoolId=@pool AND StatusKey='completed'", new { pool }));
                Assert.AreEqual(0, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_approval_slot WHERE InstanceId=@instance", new { instance }));
            }
            finally { foreach (var response in attempts) response.Dispose(); }
            await VerifyParallelAsync(factory, owner, db, people, clients, form);
            await VerifyCurrentRoleAndAdministrativeAssignmentAsync(factory, owner, db, people[0], clients[0], form);
            await VerifyTenantAsync(factory, db, clients[0]);
            await RecoverAsync(provider, connectionString, db);
        }
        finally { foreach (var client in clients) client.Dispose(); }
    }
    private static async Task VerifyParallelAsync(FullNetApiFactory factory, HttpClient owner, DbConnection db,
        IReadOnlyList<HostTestIdentity> people, IReadOnlyList<HttpClient> clients, Guid form)
    {
        var source = Sources(people.Take(2).Select(person => person.UserId).ToArray());
        var version = await PublishAsync(owner, form, [Node("start", "start", new { nextNodeKeys = new[] { "fork" } }),
            Node("fork", "gateway.parallel", new { gatewayRoleKey = "fork", joinNodeKey = "join", branches = new[] {
                new { branchKey = "left", nextNodeKey = "left" }, new { branchKey = "right", nextNodeKey = "right" } } }),
            Node("left", "human.claim", new { nextNodeKeys = new[] { "join" }, assigneePolicy = source, claimPolicy = new { allowRelease = true }, fieldPolicies = new { left = "editable" } }),
            Node("right", "human.claim", new { nextNodeKeys = new[] { "join" }, assigneePolicy = source, claimPolicy = new { allowRelease = true }, fieldPolicies = new { right = "editable" } }),
            Node("join", "gateway.parallel", new { gatewayRoleKey = "join", forkNodeKey = "fork", nextNodeKeys = new[] { "end" } }), Node("end", "end", new { })]);
        var instance = await StartAsync(owner, version, "parallel-claim");
        var pools = (await db.QueryAsync<(Guid Id, string NodeKey, Guid? JoinId, string Branch)>("""
            SELECT pool.Id,step.NodeKey,step.ParallelJoinId AS JoinId,step.ParallelBranchKey AS Branch
            FROM fn_workflow_claim AS pool INNER JOIN fn_workflow_step AS step ON step.Id=pool.Id WHERE pool.InstanceId=@instance
            """, new { instance })).ToDictionary(item => item.NodeKey);
        Assert.AreEqual(2, pools.Count); Assert.IsNotNull(pools["left"].JoinId); Assert.AreEqual(pools["left"].JoinId, pools["right"].JoinId);
        Assert.AreEqual("left", pools["left"].Branch); Assert.AreEqual("right", pools["right"].Branch);
        var claimed = await Task.WhenAll(PostAsync(clients[0], $"/api/v1/workflow/claim-pools/{pools["left"].Id:D}/claim", new { expectedRevision = 1, idempotencyKey = "left-claim" }),
            PostAsync(clients[1], $"/api/v1/workflow/claim-pools/{pools["right"].Id:D}/claim", new { expectedRevision = 1, idempotencyKey = "right-claim" }));
        var leftTodo = claimed[0].GetProperty("activeTodoId").GetGuid(); var rightTodo = claimed[1].GetProperty("activeTodoId").GetGuid();
        var left = await RuntimeAsync(clients[0], leftTodo);
        await PostAsync(clients[0], $"/api/v1/workflow/todos/{leftTodo:D}/complete", new { expectedRevision = left.GetProperty("revision").GetInt64(),
            expectedFormRevision = left.GetProperty("submissionRevision").GetInt64(), fieldPatch = new { left = "left-output" }, idempotencyKey = "left-complete" });
        Assert.AreEqual("active", await db.ExecuteScalarAsync<string>("SELECT StatusKey FROM fn_workflow_instance WHERE Id=@instance", new { instance }));
        Assert.AreEqual("claimed", await db.ExecuteScalarAsync<string>("SELECT StatusKey FROM fn_workflow_claim WHERE Id=@id", new { id = pools["right"].Id }));
        var right = await RuntimeAsync(clients[1], rightTodo);
        Assert.AreEqual("right-initial", right.GetProperty("submission").GetProperty("right").GetString());
        await PostAsync(clients[1], $"/api/v1/workflow/todos/{rightTodo:D}/release", new { expectedRevision = right.GetProperty("revision").GetInt64(),
            expectedFormRevision = right.GetProperty("submissionRevision").GetInt64(),
            expectedPoolRevision = await db.ExecuteScalarAsync<long>("SELECT Revision FROM fn_workflow_claim WHERE Id=@id", new { id = pools["right"].Id }), idempotencyKey = "parallel-release" });
        var next = await PostAsync(clients[0], $"/api/v1/workflow/claim-pools/{pools["right"].Id:D}/claim", new {
            expectedRevision = await db.ExecuteScalarAsync<long>("SELECT Revision FROM fn_workflow_claim WHERE Id=@id", new { id = pools["right"].Id }), idempotencyKey = "parallel-reclaim" });
        var nextTodo = next.GetProperty("activeTodoId").GetGuid(); var current = await RuntimeAsync(clients[0], nextTodo);
        Assert.AreEqual("right-initial", current.GetProperty("submission").GetProperty("right").GetString());
        await PostAsync(clients[0], $"/api/v1/workflow/todos/{nextTodo:D}/complete", new { expectedRevision = current.GetProperty("revision").GetInt64(),
            expectedFormRevision = current.GetProperty("submissionRevision").GetInt64(), fieldPatch = new { right = "right-output" }, idempotencyKey = "right-complete" });
        for (var attempt = 0; attempt < 4; attempt++) { await using var scope = factory.Services.CreateAsyncScope(); if (await scope.ServiceProvider.GetRequiredService<WorkflowActivityWorkProcessor>().ProcessPendingAsync(default) == 0) break; }
        Assert.AreEqual("completed", await db.ExecuteScalarAsync<string>("SELECT StatusKey FROM fn_workflow_instance WHERE Id=@instance", new { instance }));
        using var values = JsonDocument.Parse((await db.ExecuteScalarAsync<string>("SELECT SubmissionJson FROM fn_workflow_form_submission WHERE InstanceId=@instance", new { instance }))!);
        Assert.AreEqual("left-output", values.RootElement.GetProperty("left").GetString()); Assert.AreEqual("right-output", values.RootElement.GetProperty("right").GetString());
        Assert.AreEqual(0, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_approval_slot WHERE InstanceId=@instance", new { instance }));

        var cancelled = await StartAsync(owner, version, "parallel-cancel");
        var available = (await db.QueryAsync<(Guid Id, string NodeKey)>("SELECT step.Id,step.NodeKey FROM fn_workflow_step AS step WHERE InstanceId=@cancelled AND NodeTypeKey='human.claim'", new { cancelled }))
            .ToDictionary(item => item.NodeKey, item => item.Id);
        var active = await PostAsync(clients[0], $"/api/v1/workflow/claim-pools/{available["left"]:D}/claim", new { expectedRevision = 1, idempotencyKey = "before-cancel" });
        await PostAsync(owner, $"/api/v1/workflow/instances/{cancelled:D}/cancel", new { expectedRevision = await db.ExecuteScalarAsync<long>("SELECT Revision FROM fn_workflow_instance WHERE Id=@cancelled", new { cancelled }), reason = "并行领取关闭验收", idempotencyKey = "parallel-cancel-action" });
        Assert.AreEqual(2, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_claim WHERE InstanceId=@cancelled AND StatusKey='cancelled'", new { cancelled }));
        Assert.AreEqual(0, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_todo WHERE InstanceId=@cancelled AND StatusKey='active'", new { cancelled }));
        await PostAsync(clients[1], $"/api/v1/workflow/claim-pools/{available["right"]:D}/claim", new { expectedRevision = 1, idempotencyKey = "late-claim" }, HttpStatusCode.Conflict);
        Assert.AreNotEqual(Guid.Empty, active.GetProperty("activeTodoId").GetGuid());
    }
    private static async Task VerifyCurrentRoleAndAdministrativeAssignmentAsync(FullNetApiFactory factory, HttpClient owner,
        DbConnection db, HostTestIdentity candidate, HttpClient candidateClient, Guid form)
    {
        var role = Guid.CreateVersion7();
        await db.ExecuteAsync("""
            INSERT INTO fn_identity_role (Id, TenantId, ScopeKey, Code, Name, IsSystem, IsActive, IsSuperAdministrator, DataScopeKind, CreatedAtUtc, Version)
            SELECT @role, TenantId, ScopeKey, @code, Name, IsSystem, IsActive, IsSuperAdministrator, DataScopeKind, CreatedAtUtc, Version
            FROM fn_identity_role WHERE Id=(SELECT RoleId FROM fn_identity_user_role WHERE UserId=@user)
            """, new { role, code = "claim-source-" + role.ToString("N"), user = candidate.UserId });
        await db.ExecuteAsync("INSERT INTO fn_identity_user_role(UserId, RoleId) VALUES (@user, @role)", new { user = candidate.UserId, role });
        var version = await PublishAsync(owner, form, [Node("start", "start", new { nextNodeKeys = new[] { "claim" } }),
            Node("claim", "human.claim", new { nextNodeKeys = new[] { "end" }, claimPolicy = new { allowRelease = false },
                assigneePolicy = new { sources = new[] { new { resolverKindKey = "role_members", roleIds = new[] { role } } } } }), Node("end", "end", new { })]);
        var instance = await StartAsync(owner, version, "claim-role");
        var pool = await db.ExecuteScalarAsync<Guid>("SELECT Id FROM fn_workflow_claim WHERE InstanceId=@instance", new { instance });
        await db.ExecuteAsync("DELETE FROM fn_identity_user_role WHERE UserId=@user AND RoleId=@role", new { user = candidate.UserId, role });
        await PostAsync(candidateClient, $"/api/v1/workflow/claim-pools/{pool:D}/claim", new { expectedRevision = 1, idempotencyKey = "removed-source" }, HttpStatusCode.Forbidden);
        Assert.AreEqual(0, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_todo WHERE InstanceId=@instance", new { instance }));
        var noSign = await factory.CreateHostIdentityAsync("claim-no-sign-" + Guid.NewGuid().ToString("N"),
            [WorkflowPermissions.TodosRead, WorkflowPermissions.TodosComplete, WorkflowPermissions.TodosRelease]);
        using var client = Client(factory, noSign);
        var assigned = await PostAsync(owner, $"/api/v1/workflow/claim-pools/{pool:D}/assign",
            new { expectedRevision = 1, assigneeUserId = noSign.UserId, idempotencyKey = "admin-assign" });
        var todo = assigned.GetProperty("activeTodoId").GetGuid(); var runtime = await RuntimeAsync(client, todo);
        Assert.IsFalse(runtime.GetProperty("allowedActions").EnumerateArray().Any(value => value.GetString() == "release"));
        await PostAsync(client, $"/api/v1/workflow/todos/{todo:D}/release",
            new { expectedRevision = 1, expectedPoolRevision = 2, expectedFormRevision = 1, idempotencyKey = "release-disabled" }, HttpStatusCode.Conflict);
        await PostAsync(client, $"/api/v1/workflow/todos/{todo:D}/complete", new { expectedRevision = 1, expectedFormRevision = 1,
            fieldPatch = new { }, signatureFileId = Guid.CreateVersion7(), idempotencyKey = "signature-denied" }, HttpStatusCode.Forbidden);
        Assert.AreEqual(0, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_action_evidence WHERE InstanceId=@instance", new { instance }));
        Assert.AreEqual("workflow.claim_pools.assign", await db.ExecuteScalarAsync<string>("SELECT AuthorizationKey FROM fn_workflow_claim_attempt WHERE PoolId=@pool", new { pool }));
        await PostAsync(client, $"/api/v1/workflow/todos/{todo:D}/complete", Vote(runtime, "admin-assigned-complete", null));
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
                var name = "claim-tenant-" + Guid.NewGuid().ToString("N"); var email = name + "@example.com";
                var user = await factory.CreateHostIdentityAsync(name, [WorkflowPermissions.TodosRead, WorkflowPermissions.TodosComplete, WorkflowPermissions.TodosSign, WorkflowPermissions.ClaimPoolsClaim, WorkflowPermissions.ClaimPoolsRead, "tenancy.tenants.switch"], password: FullNetApiFactory.TestPassword);
                await factory.EnsureHostUserProfileEmailAsync(user.UserId, email, name); members.Add(user);
                var client = factory.CreateClientForHost("localhost"); clients.Add(client); client.DefaultRequestHeaders.Add("Origin", "http://localhost");
                var hostToken = await IntegrationTestAuthHelper.LoginAsHostUserAsync(client, name, FullNetApiFactory.TestPassword); client.DefaultRequestHeaders.Authorization = new("Bearer", hostToken);
                var invitation = await PostAsync(owner, "/api/v1/identity/tenant-members/invitations", new CreateTenantInvitationRequest(email, TenantMemberRoles.Member));
                await PostAsync(client, $"/api/v1/me/tenant-invitations/{invitation.GetProperty("invitation").GetProperty("id").GetGuid():D}/accept", new { });
                client.DefaultRequestHeaders.Authorization = new("Bearer", await IntegrationTestTenantContextHelper.SwitchToTenantAsync(client, hostToken, tenant.Id));
            }
            var form = await CreateFormAsync(owner); var version = await PublishAsync(owner, form,
                [Node("start", "start", new { nextNodeKeys = new[] { "claim" } }), Node("claim", "human.claim", new { nextNodeKeys = new[] { "end" },
                    claimPolicy = new { allowRelease = true }, assigneePolicy = Sources(members.Select(member => member.UserId).ToArray()), signRequired = true, commentRequired = true }), Node("end", "end", new { })]);
            var instance = await StartAsync(owner, version, "tenant");
            var pool = await db.ExecuteScalarAsync<Guid>("SELECT Id FROM fn_workflow_claim WHERE InstanceId=@instance", new { instance });
            await PostAsync(hostReader, $"/api/v1/workflow/claim-pools/{pool:D}/claim", new { expectedRevision = 1, idempotencyKey = "wrong-scope" }, HttpStatusCode.NotFound);
            var claimed = await PostAsync(clients[0], $"/api/v1/workflow/claim-pools/{pool:D}/claim", new { expectedRevision = 1, idempotencyKey = "tenant-claim" });
            var todo = claimed.GetProperty("activeTodoId").GetGuid(); var runtime = await RuntimeAsync(clients[0], todo);
            var file = await SignatureAsync(clients[0], todo);
            var request = new { expectedRevision = 1, expectedFormRevision = runtime.GetProperty("submissionRevision").GetInt64(),
                fieldPatch = new { }, comment = "确认办理", signatureFileId = file, idempotencyKey = "tenant-complete" };
            var receipts = await Task.WhenAll(PostAsync(clients[0], $"/api/v1/workflow/todos/{todo:D}/complete", request),
                PostAsync(clients[0], $"/api/v1/workflow/todos/{todo:D}/complete", request));
            Assert.AreEqual(receipts[0].GetRawText(), receipts[1].GetRawText());
            Assert.AreEqual(1, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_action_evidence WHERE InstanceId=@instance AND ActionKey='complete' AND TenantScopeKey=@scope", new { instance, scope = $"tenant:{tenant.Id:N}" }));
            Assert.AreEqual("completed", await db.ExecuteScalarAsync<string>("SELECT StatusKey FROM fn_workflow_instance WHERE Id=@instance", new { instance }));
        }
        finally { foreach (var client in clients) client.Dispose(); }
    }

    private static async Task<Guid> SignatureAsync(HttpClient client, Guid todo)
    {
        using var body = new MultipartFormDataContent(); var png = new ByteArrayContent(Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+j2e8AAAAASUVORK5CYII="));
        png.Headers.ContentType = new("image/png"); body.Add(png, "file", "signature.png");
        using var response = await client.PostAsync($"/api/v1/workflow/todos/{todo:D}/signature-uploads?expectedRevision=1", body);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("fileId").GetGuid();
    }
    private static async Task RecoverAsync(DatabaseProvider provider, string connection, DbConnection db)
    {
        // MySQL会复用PoolId前导唯一索引承载外键；恢复夹具只移除独立调度索引，不破坏外键依赖。
        await db.ExecuteAsync("DROP INDEX ix_fn_workflow_claim_due ON fn_workflow_claim");
        await db.ExecuteAsync(provider == DatabaseProvider.SqlServer ? "ALTER TABLE fn_workflow_claim DROP CONSTRAINT ck_fn_workflow_claim_state" : "ALTER TABLE fn_workflow_claim DROP CHECK ck_fn_workflow_claim_state");
        await db.ExecuteAsync(provider == DatabaseProvider.SqlServer ? "DELETE FROM dbo.SchemaVersions WHERE ScriptName LIKE '%302_WorkflowClaimPools.sql'" : "DELETE FROM schemaversions WHERE ScriptName LIKE '%302_WorkflowClaimPools.sql'");
        var result = provider == DatabaseProvider.SqlServer ? Migration114To126RecoveryTestSupport.MigrateSqlServer(connection, 302) : Migration114To126RecoveryTestSupport.MigrateMySql(connection, 302);
        Assert.IsTrue(result.Successful, result.Error?.Message);
        try
        {
            await db.ExecuteAsync("UPDATE fn_workflow_claim SET StatusKey='available' WHERE StatusKey='completed'");
            Assert.Fail("已完成领取不能保留个人Todo并伪装为候选池开放状态。");
        }
        catch (DbException) { }
    }

    private static async Task<Guid> CreateFormAsync(HttpClient client)
    {
        var form = await PostAsync(client, "/api/v1/workflow/forms", new { formKey = "claim." + Guid.NewGuid().ToString("N"), draft = new { schemaVersion = 1, adapterVersion = 1,
            sections = new[] { new { sectionKey = "main", fields = new[] { "title", "left", "right" }.Select(key => new { fieldKey = key, fieldTypeKey = "text", required = false, constraints = new { } }).ToArray() } } } }, HttpStatusCode.Created);
        return (await PostAsync(client, $"/api/v1/workflow/forms/{form.GetProperty("id").GetGuid():D}/publish", new { expectedRevision = 1 })).GetProperty("id").GetGuid();
    }
    private static async Task<Guid> PublishAsync(HttpClient client, Guid form, object[] nodes)
    {
        var definition = await PostAsync(client, "/api/v1/workflow/definitions", new { definitionKey = "claim." + Guid.NewGuid().ToString("N"), draft = new { schemaVersion = 1, workflowKindKey = "automation", nodes } }, HttpStatusCode.Created);
        return (await PostAsync(client, $"/api/v1/workflow/definitions/{definition.GetProperty("id").GetGuid():D}/publish", new { expectedRevision = 1, formVersionId = form })).GetProperty("id").GetGuid();
    }
    private static async Task<Guid> StartAsync(HttpClient client, Guid version, string key) => (await PostAsync(client, "/api/v1/workflow/instances", new {
        definitionVersionId = version, businessType = "claim", businessId = key, initialValues = new { title = "initial", left = "left-initial", right = "right-initial" }, idempotencyKey = key }, HttpStatusCode.Created)).GetProperty("id").GetGuid();
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
