using System.Data.Common;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Dapper;
using Full.NET.Data.Abstractions;
using Full.NET.Abstractions.Tenancy;
using Full.NET.IntegrationTests.Api;
using Full.NET.IntegrationTests.Migrations;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Files.Contracts;
using Full.NET.Modules.Tenancy.Contracts;
using Full.NET.Modules.Workflow.Contracts;
using Full.NET.Modules.Workflow.Execution;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using MySqlConnector;

namespace Full.NET.IntegrationTests.Workflow;

/// <summary>真实双库验证独立选票、固定分母、两类交接、截止与不可变历史。</summary>
[TestClass]
public sealed class WorkflowVoteActivityApiTests
{
    [TestMethod]
    public async Task Vote_activity_with_sql_server() => await VerifyAsync(DatabaseProvider.SqlServer, await SharedDatabaseFixture.CreateSqlServerDatabaseAsync());
    [TestMethod]
    public async Task Vote_activity_with_mysql() => await VerifyAsync(DatabaseProvider.MySql, await SharedDatabaseFixture.CreateMySqlDatabaseAsync());

    private static async Task VerifyAsync(DatabaseProvider provider, string connectionString)
    {
        using var factory = new FullNetApiFactory(provider, connectionString); await factory.InitializeAsync(useSchemaTemplate: false);
        await using DbConnection db = provider == DatabaseProvider.SqlServer ? new SqlConnection(connectionString)
            : new MySqlConnection(Migration114To126RecoveryTestSupport.MySqlQueryConnectionString(connectionString));
        var designer = await factory.CreateHostIdentityAsync("vote-owner-" + Guid.NewGuid().ToString("N"),
            [WorkflowPermissions.FormsCreate, WorkflowPermissions.FormsPublish, WorkflowPermissions.FormsDeleteVersion,
                WorkflowPermissions.DefinitionsCreate, WorkflowPermissions.DefinitionsPublish, WorkflowPermissions.InstancesStart,
                WorkflowPermissions.InstancesCancel, WorkflowPermissions.InstancesRecover, WorkflowPermissions.InstancesRead, WorkflowPermissions.TodosRead]);
        using var owner = Client(factory, designer);
        var people = new List<HostTestIdentity>();
        for (var index = 0; index < 5; index++) people.Add(await factory.CreateHostIdentityAsync("vote-person-" + Guid.NewGuid().ToString("N"),
            [WorkflowPermissions.TodosRead, WorkflowPermissions.TodosVote, WorkflowPermissions.TodosTransfer, WorkflowPermissions.TodosSign]));
        var clients = people.Select(person => Client(factory, person)).ToArray();
        try
        {
            var main = await FormAsync(owner, false); var ballot = await FormAsync(owner, true);
            var version = await PublishAsync(owner, main, ballot, people.Take(3).Select(person => person.UserId).ToArray(), false);
            var instance = await StartAsync(owner, version, "vote-main");
            Assert.AreEqual(0, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_approval_slot WHERE InstanceId=@instance", new { instance }));
            Assert.AreEqual(3, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_vote_ballot WHERE InstanceId=@instance", new { instance }));
            var first = await TodoAsync(db, instance, people[0].UserId); var firstView = await RuntimeAsync(clients[0], first);
            Assert.AreEqual("human.vote", firstView.GetProperty("activityKindKey").GetString());
            Assert.AreEqual("none", firstView.GetProperty("approvalModeKey").GetString());
            Assert.AreEqual(ballot, firstView.GetProperty("voteForm").GetProperty("formVersionId").GetGuid());
            Assert.AreEqual("readOnly", firstView.GetProperty("fieldPolicies").GetProperty("accepted").GetString());
            Assert.AreEqual(JsonValueKind.Null, firstView.GetProperty("voteProgress").GetProperty("yesCount").ValueKind);
            await PostAsync(clients[1], $"/api/v1/workflow/todos/{first:D}/vote", Cast(firstView, "no", "foreign", "foreign"), HttpStatusCode.Forbidden);
            var request = Cast(firstView, "yes", "private-first", "same-request");
            var duplicate = await Task.WhenAll(PostAsync(clients[0], $"/api/v1/workflow/todos/{first:D}/vote", request),
                PostAsync(clients[0], $"/api/v1/workflow/todos/{first:D}/vote", request));
            Assert.AreEqual(duplicate[0].GetRawText(), duplicate[1].GetRawText());
            Assert.AreEqual(1, await db.ExecuteScalarAsync<long>("SELECT Revision FROM fn_workflow_form_submission WHERE InstanceId=@instance", new { instance }));
            var second = await TodoAsync(db, instance, people[1].UserId); var secondView = await RuntimeAsync(clients[1], second);
            Assert.AreEqual("{}", secondView.GetProperty("voteForm").GetProperty("values").GetRawText());
            await PostAsync(clients[1], $"/api/v1/workflow/todos/{second:D}/transfer", new { assigneeUserId = people[0].UserId,
                expectedRevision = 1, expectedFormRevision = 1, idempotencyKey = "no-second-ballot" }, HttpStatusCode.BadRequest);
            var transferred = await PostAsync(clients[1], $"/api/v1/workflow/todos/{second:D}/transfer", new { assigneeUserId = people[3].UserId,
                expectedRevision = 1, expectedFormRevision = 1, idempotencyKey = "transfer" });
            var newSecond = transferred.GetProperty("activeTodoId").GetGuid();
            Assert.AreEqual(HttpStatusCode.Forbidden, (await clients[1].GetAsync($"/api/v1/workflow/todos/{newSecond:D}/runtime")).StatusCode);
            await PostAsync(clients[3], $"/api/v1/workflow/todos/{newSecond:D}/vote", Cast(await RuntimeAsync(clients[3], newSecond), "yes", "successor-private", "second-vote"));
            Assert.AreEqual("active", await db.ExecuteScalarAsync<string>("SELECT StatusKey FROM fn_workflow_instance WHERE Id=@instance", new { instance }));
            var third = await TodoAsync(db, instance, people[2].UserId);
            var reassigned = await PostAsync(owner, $"/api/v1/workflow/instances/{instance:D}/reassign", new { todoId = third,
                assigneeUserId = people[4].UserId, expectedRevision = 4, expectedTodoRevision = 1, reason = "明确交接剩余个人选票", idempotencyKey = "admin-reassign" });
            var newThird = reassigned.GetProperty("activeTodoId").GetGuid();
            var final = await PostAsync(clients[4], $"/api/v1/workflow/todos/{newThird:D}/vote", Cast(await RuntimeAsync(clients[4], newThird), "abstain", "last-private", "last-vote"));
            Assert.AreEqual("completed", final.GetProperty("statusKey").GetString());
            Assert.AreEqual(3, await db.ExecuteScalarAsync<int>("SELECT MemberCount FROM fn_workflow_vote WHERE InstanceId=@instance", new { instance }));
            var mainValues = JsonDocument.Parse((await db.ExecuteScalarAsync<string>("SELECT SubmissionJson FROM fn_workflow_form_submission WHERE InstanceId=@instance", new { instance }))!);
            Assert.IsTrue(mainValues.RootElement.GetProperty("accepted").GetBoolean()); Assert.AreEqual("main-initial", mainValues.RootElement.GetProperty("title").GetString());
            var oldSecond = await RuntimeAsync(clients[1], second);
            Assert.AreEqual("{}", oldSecond.GetProperty("voteForm").GetProperty("values").GetRawText());
            Assert.AreEqual("private-first", (await RuntimeAsync(clients[0], first)).GetProperty("voteForm").GetProperty("values").GetProperty("title").GetString());
            Assert.AreEqual(duplicate[0].GetRawText(), (await PostAsync(clients[0], $"/api/v1/workflow/todos/{first:D}/vote", request)).GetRawText());
            await DeadlineAsync(factory, owner, clients, people, db, main, ballot);
            await VerifySignaturePermissionAsync(factory, owner, db, main, ballot);
            await VerifyTenantSignaturesAsync(factory, db, owner);
            await RecoverAsync(provider, connectionString, db);
        }
        finally { foreach (var client in clients) client.Dispose(); }
    }

    private static async Task DeadlineAsync(FullNetApiFactory factory, HttpClient owner, HttpClient[] clients,
        List<HostTestIdentity> people, DbConnection db, Guid main, Guid ballot)
    {
        var version = await PublishAsync(owner, main, ballot, people.Take(3).Select(person => person.UserId).ToArray(), true);
        foreach (var zeroValid in new[] { false, true })
        {
            var instance = await StartAsync(owner, version, "deadline-" + zeroValid);
            if (!zeroValid)
                for (var index = 0; index < 2; index++)
                {
                    var todo = await TodoAsync(db, instance, people[index].UserId);
                    await PostAsync(clients[index], $"/api/v1/workflow/todos/{todo:D}/vote", Cast(await RuntimeAsync(clients[index], todo), index == 0 ? "yes" : "abstain", "deadline-private", "deadline-vote-" + index));
                }
            var last = await TodoAsync(db, instance, people[2].UserId); var lastView = await RuntimeAsync(clients[2], last);
            await db.ExecuteAsync("UPDATE fn_workflow_vote SET ClosesAtUtc=@past WHERE InstanceId=@instance", new { instance, past = DateTimeOffset.UtcNow.AddMinutes(-1) });
            await PostAsync(clients[2], $"/api/v1/workflow/todos/{last:D}/vote", Cast(lastView, "yes", "too-late", "after-deadline"), HttpStatusCode.Conflict);
            await using var scope = factory.Services.CreateAsyncScope();
            var processor = ActivatorUtilities.CreateInstance<WorkflowVoteDeadlineProcessor>(scope.ServiceProvider);
            await processor.ProcessDueAsync(default); await processor.ProcessDueAsync(default);
            Assert.AreEqual(zeroValid ? "suspended" : "completed", await db.ExecuteScalarAsync<string>("SELECT StatusKey FROM fn_workflow_instance WHERE Id=@instance", new { instance }));
            Assert.AreEqual(zeroValid ? 0 : 2, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_action_record WHERE InstanceId=@instance AND ActionKey='vote'", new { instance }));
            Assert.AreEqual(1, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_execution_log WHERE InstanceId=@instance AND TransitionKey='vote.deadline'", new { instance }));
            if (zeroValid)
            {
                var revision = await db.ExecuteScalarAsync<long>("SELECT Revision FROM fn_workflow_instance WHERE Id=@instance", new { instance });
                await PostAsync(owner, $"/api/v1/workflow/instances/{instance:D}/recover", new { expectedRevision = revision, reason = "不能隐式裁定无有效票", idempotencyKey = "no-generic-recover" }, HttpStatusCode.Conflict);
            }
        }
    }
    private static async Task RecoverAsync(DatabaseProvider provider, string connection, DbConnection db)
    {
        await db.ExecuteAsync("DROP INDEX ix_fn_workflow_vote_deadline ON fn_workflow_vote");
        await db.ExecuteAsync(provider == DatabaseProvider.SqlServer ? "ALTER TABLE fn_workflow_vote DROP CONSTRAINT ck_fn_workflow_vote_policy" : "ALTER TABLE fn_workflow_vote DROP CHECK ck_fn_workflow_vote_policy");
        await db.ExecuteAsync(provider == DatabaseProvider.SqlServer ? "DELETE FROM dbo.SchemaVersions WHERE ScriptName LIKE '%301_WorkflowVotes.sql'" : "DELETE FROM schemaversions WHERE ScriptName LIKE '%301_WorkflowVotes.sql'");
        var result = provider == DatabaseProvider.SqlServer ? Migration114To126RecoveryTestSupport.MigrateSqlServer(connection, 301) : Migration114To126RecoveryTestSupport.MigrateMySql(connection, 301);
        Assert.IsTrue(result.Successful, result.Error?.Message);
    }
    private static async Task<Guid> FormAsync(HttpClient client, bool ballot)
    {
        object[] fields = ballot ? [new { fieldKey = "title", fieldTypeKey = "text", required = true, constraints = new { } }]
            : [new { fieldKey = "title", fieldTypeKey = "text", required = false, constraints = new { } }, new { fieldKey = "accepted", fieldTypeKey = "switch", required = false, constraints = new { } }];
        var form = await PostAsync(client, "/api/v1/workflow/forms", new { formKey = "vote." + Guid.NewGuid().ToString("N"), draft = new { schemaVersion = 1, adapterVersion = 1, sections = new[] { new { sectionKey = "main", fields } } } }, HttpStatusCode.Created);
        return (await PostAsync(client, $"/api/v1/workflow/forms/{form.GetProperty("id").GetGuid():D}/publish", new { expectedRevision = 1 })).GetProperty("id").GetGuid();
    }
    private static async Task<Guid> PublishAsync(HttpClient client, Guid main, Guid ballot, Guid[] candidates, bool deadline, bool sign = false)
    {
        var policy = new Dictionary<string, object> { ["ballotFormVersionId"] = ballot, ["participationPercentage"] = 66, ["approvalPercentage"] = 100, ["allowAbstain"] = true, ["resultFieldKey"] = "accepted" };
        if (deadline) policy["closeAfterSeconds"] = 60;
        var definition = await PostAsync(client, "/api/v1/workflow/definitions", new { definitionKey = "vote." + Guid.NewGuid().ToString("N"), draft = new { schemaVersion = 1,
            nodes = new[] { Node("start", "start", new { nextNodeKeys = new[] { "vote" } }), Node("vote", "human.vote", new { nextNodeKeys = new[] { "end" }, votePolicy = policy, signRequired = sign, commentRequired = sign,
                assigneePolicy = new { sources = new[] { new { resolverKindKey = "specified_users", userIds = candidates } } } }), Node("end", "end", new { }) } } }, HttpStatusCode.Created);
        return (await PostAsync(client, $"/api/v1/workflow/definitions/{definition.GetProperty("id").GetGuid():D}/publish", new { expectedRevision = 1, formVersionId = main })).GetProperty("id").GetGuid();
    }
    private static object Node(string key, string kind, object config) => new { nodeKey = key, nodeTypeKey = kind, nodeSchemaVersion = 1, config };
    private static async Task<Guid> StartAsync(HttpClient client, Guid version, string key) => (await PostAsync(client, "/api/v1/workflow/instances", new { definitionVersionId = version, businessType = "vote", businessId = key,
        initialValues = new { title = "main-initial", accepted = false }, idempotencyKey = key }, HttpStatusCode.Created)).GetProperty("id").GetGuid();
    private static object Cast(JsonElement runtime, string choice, string title, string key) => new { expectedRevision = runtime.GetProperty("revision").GetInt64(),
        expectedBallotRevision = runtime.GetProperty("voteForm").GetProperty("revision").GetInt64(), choiceKey = choice, values = new { title }, idempotencyKey = key };
    private static Task<Guid> TodoAsync(DbConnection db, Guid instance, Guid actor) => db.ExecuteScalarAsync<Guid>("SELECT Id FROM fn_workflow_todo WHERE InstanceId=@instance AND AssigneeUserId=@actor AND StatusKey='active'", new { instance, actor });
    private static Task<JsonElement> RuntimeAsync(HttpClient client, Guid todo) => client.GetFromJsonAsync<JsonElement>($"/api/v1/workflow/todos/{todo:D}/runtime");
    private static HttpClient Client(FullNetApiFactory factory, HostTestIdentity user) { var client = factory.CreateClientForHost("localhost"); client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", user.AccessToken); return client; }
    private static async Task<JsonElement> PostAsync(HttpClient client, string path, object body, HttpStatusCode expected = HttpStatusCode.OK)
    { using var response = await client.PostAsJsonAsync(path, body); var text = await response.Content.ReadAsStringAsync(); Assert.AreEqual(expected, response.StatusCode, path + ": " + text); return JsonSerializer.Deserialize<JsonElement>(text); }

    private static async Task VerifyTenantSignaturesAsync(FullNetApiFactory factory, DbConnection db, HttpClient hostReader)
    {
        using var owner = factory.CreateClientForHost("localhost"); owner.DefaultRequestHeaders.Add("Origin", "http://localhost");
        var login = await PostAsync(owner, "/api/v1/auth/login", new LoginRequest("admin", FullNetApiFactory.TestPassword));
        var token = login.GetProperty("accessToken").GetString()!; owner.DefaultRequestHeaders.Authorization = new("Bearer", token);
        var available = await owner.GetFromJsonAsync<TenantContextSummary[]>("/api/v1/tenancy/available"); var tenant = available!.Single(value => value.Identifier == "acme");
        owner.DefaultRequestHeaders.Authorization = new("Bearer", await IntegrationTestTenantContextHelper.SwitchToTenantAsync(owner, token, tenant.Id));
        var members = new List<HostTestIdentity>(); var clients = new List<HttpClient>();
        try
        {
            for (var index = 0; index < 2; index++)
            {
                var name = "vote-tenant-" + Guid.NewGuid().ToString("N"); var email = name + "@example.com";
                var user = await factory.CreateHostIdentityAsync(name, [WorkflowPermissions.TodosRead, WorkflowPermissions.TodosVote,
                    WorkflowPermissions.TodosSign, "tenancy.tenants.switch"], password: FullNetApiFactory.TestPassword);
                await factory.EnsureHostUserProfileEmailAsync(user.UserId, email, name); members.Add(user);
                var client = factory.CreateClientForHost("localhost"); clients.Add(client); client.DefaultRequestHeaders.Add("Origin", "http://localhost");
                var hostToken = await IntegrationTestAuthHelper.LoginAsHostUserAsync(client, name, FullNetApiFactory.TestPassword); client.DefaultRequestHeaders.Authorization = new("Bearer", hostToken);
                var invitation = await PostAsync(owner, "/api/v1/identity/tenant-members/invitations", new CreateTenantInvitationRequest(email, TenantMemberRoles.Member));
                await PostAsync(client, $"/api/v1/me/tenant-invitations/{invitation.GetProperty("invitation").GetProperty("id").GetGuid():D}/accept", new { });
                client.DefaultRequestHeaders.Authorization = new("Bearer", await IntegrationTestTenantContextHelper.SwitchToTenantAsync(client, hostToken, tenant.Id));
            }
            var main = await FormAsync(owner, false); var ballot = await FormAsync(owner, true);
            var version = await PublishAsync(owner, main, ballot, members.Select(member => member.UserId).ToArray(), false, true);
            var instance = await StartAsync(owner, version, "tenant-signed");
            for (var index = 0; index < 2; index++)
            {
                var todo = await TodoAsync(db, instance, members[index].UserId); var runtime = await RuntimeAsync(clients[index], todo);
                using (var foreign = await hostReader.GetAsync($"/api/v1/workflow/todos/{todo:D}/runtime")) Assert.AreEqual(HttpStatusCode.NotFound, foreign.StatusCode);
                using var body = new MultipartFormDataContent(); var png = new ByteArrayContent(Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+j2e8AAAAASUVORK5CYII="));
                png.Headers.ContentType = new("image/png"); body.Add(png, "file", "signature.png");
                using var upload = await clients[index].PostAsync($"/api/v1/workflow/todos/{todo:D}/signature-uploads?expectedRevision=1", body);
                Assert.AreEqual(HttpStatusCode.OK, upload.StatusCode, await upload.Content.ReadAsStringAsync());
                var fileId = (await upload.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("fileId").GetGuid();
                var request = new { expectedRevision = 1, expectedBallotRevision = 1, choiceKey = "yes", values = new { title = "private-tenant-" + index },
                    signatureFileId = fileId, comment = "本人确认独立选票", idempotencyKey = "tenant-cast-" + index };
                var duplicate = await Task.WhenAll(PostAsync(clients[index], $"/api/v1/workflow/todos/{todo:D}/vote", request),
                    PostAsync(clients[index], $"/api/v1/workflow/todos/{todo:D}/vote", request));
                Assert.AreEqual(duplicate[0].GetRawText(), duplicate[1].GetRawText());
                Assert.AreEqual(ballot, await db.ExecuteScalarAsync<Guid>("SELECT FormVersionId FROM fn_workflow_action_evidence WHERE TodoId=@todo AND ActionKey='vote'", new { todo }));
                Assert.AreEqual(2, await db.ExecuteScalarAsync<long>("SELECT FormRevision FROM fn_workflow_action_evidence WHERE TodoId=@todo AND ActionKey='vote'", new { todo }));
            }
            Assert.AreEqual(2, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_action_evidence WHERE InstanceId=@instance AND ActionKey='vote' AND TenantScopeKey=@scope", new { instance, scope = $"tenant:{tenant.Id:N}" }));
            Assert.AreEqual("completed", await db.ExecuteScalarAsync<string>("SELECT StatusKey FROM fn_workflow_instance WHERE Id=@instance", new { instance }));
        }
        finally { foreach (var client in clients) client.Dispose(); }
    }

    private static async Task VerifySignaturePermissionAsync(FullNetApiFactory factory, HttpClient owner, DbConnection db, Guid main, Guid ballot)
    {
        var user = await factory.CreateHostIdentityAsync("vote-no-sign-" + Guid.NewGuid().ToString("N"), [WorkflowPermissions.TodosRead, WorkflowPermissions.TodosVote]);
        using var client = Client(factory, user);
        var version = await PublishAsync(owner, main, ballot, [user.UserId], false, true);
        var instance = await StartAsync(owner, version, "no-sign-permission"); var todo = await TodoAsync(db, instance, user.UserId);
        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ICurrentTenantContextWriter>().SetHost();
        var bytes = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+j2e8AAAAASUVORK5CYII=");
        using var stream = new MemoryStream(bytes);
        var file = await scope.ServiceProvider.GetRequiredService<IHostFileUploadWriter>().UploadAsync(user.UserId, "owned.png", "image/png", stream, bytes.Length, default);
        Assert.IsTrue(file.IsSuccess, file.Error?.Code);
        await PostAsync(client, $"/api/v1/workflow/todos/{todo:D}/vote", new { expectedRevision = 1, expectedBallotRevision = 1,
            choiceKey = "yes", values = new { title = "private" }, comment = "不能借已持有图片绕过签名权限", signatureFileId = file.Value!.FileId,
            idempotencyKey = "forbidden-signature" }, HttpStatusCode.Forbidden);
        Assert.AreEqual(0, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_action_record WHERE InstanceId=@instance AND ActionKey='vote'", new { instance }));
        Assert.AreEqual(0, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_action_evidence WHERE InstanceId=@instance AND ActionKey='vote'", new { instance }));
        Assert.AreEqual("pending", await db.ExecuteScalarAsync<string>("SELECT StatusKey FROM fn_workflow_vote_ballot WHERE InstanceId=@instance", new { instance }));
    }
}
