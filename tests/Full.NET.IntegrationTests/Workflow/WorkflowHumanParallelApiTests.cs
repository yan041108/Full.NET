using System.Data.Common;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Dapper;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.IntegrationTests.Migrations;
using Full.NET.Modules.Files.Contracts;
using Full.NET.Modules.Workflow.Contracts;
using Microsoft.Data.SqlClient;
using MySqlConnector;

namespace Full.NET.IntegrationTests.Workflow;

/// <summary>并行投票与领取分别维护输入和结果；个人选票附件不能经实例权限泄露给其他参与人。</summary>
[TestClass]
public sealed class WorkflowHumanParallelApiTests
{
    [TestMethod]
    public async Task Parallel_vote_and_claim_with_sql_server() =>
        await VerifyAsync(DatabaseProvider.SqlServer, await SharedDatabaseFixture.CreateSqlServerDatabaseAsync());

    [TestMethod]
    public async Task Parallel_vote_and_claim_with_mysql() =>
        await VerifyAsync(DatabaseProvider.MySql, await SharedDatabaseFixture.CreateMySqlDatabaseAsync());

    private static async Task VerifyAsync(DatabaseProvider provider, string connection)
    {
        using var factory = new FullNetApiFactory(provider, connection); await factory.InitializeAsync(useSchemaTemplate: false);
        await using DbConnection db = provider == DatabaseProvider.SqlServer ? new SqlConnection(connection)
            : new MySqlConnection(Migration114To126RecoveryTestSupport.MySqlQueryConnectionString(connection));
        var designer = await factory.CreateHostIdentityAsync("parallel-designer-" + Guid.NewGuid().ToString("N"),
            [WorkflowPermissions.FormsCreate, WorkflowPermissions.FormsPublish, WorkflowPermissions.DefinitionsCreate,
                WorkflowPermissions.DefinitionsPublish, WorkflowPermissions.InstancesStart, WorkflowPermissions.InstancesRead, WorkflowPermissions.TodosRead]);
        var people = new List<HostTestIdentity>();
        for (var index = 0; index < 2; index++) people.Add(await factory.CreateHostIdentityAsync("parallel-person-" + Guid.NewGuid().ToString("N"),
            [WorkflowPermissions.TodosRead, WorkflowPermissions.TodosVote, WorkflowPermissions.TodosComplete,
                WorkflowPermissions.ClaimPoolsClaim, HostFilePermissions.Upload]));
        using var owner = Client(factory, designer); using var first = Client(factory, people[0]); using var second = Client(factory, people[1]);
        var main = await FormAsync(owner, [Field("title", "text"), Field("leftAccepted", "switch"), Field("rightText", "text")]);
        var ballot = await FormAsync(owner, [Field("title", "text"), new { fieldKey = "evidence", fieldTypeKey = "attachment", required = false,
            constraints = new { maxCount = 1, maxSizeBytes = 1024, allowedExtensions = new[] { "png" } } }]);
        var candidates = people.Select(person => person.UserId).ToArray();
        var sources = new { sources = new[] { new { resolverKindKey = "specified_users", userIds = candidates } } };
        var definition = await PostAsync(owner, "/api/v1/workflow/definitions", new { definitionKey = "human.parallel." + Guid.NewGuid().ToString("N"),
            draft = new { schemaVersion = 1, workflowKindKey = "automation", nodes = new[] {
                Node("start", "start", new { nextNodeKeys = new[] { "fork" } }),
                Node("fork", "gateway.parallel", new { gatewayRoleKey = "fork", joinNodeKey = "join",
                    branches = new[] { new { branchKey = "left", nextNodeKey = "vote" }, new { branchKey = "right", nextNodeKey = "claim" } } }),
                Node("vote", "human.vote", new { nextNodeKeys = new[] { "join" }, assigneePolicy = sources,
                    fieldPolicies = new { title = "readOnly", leftAccepted = "editable", rightText = "readOnly" },
                    votePolicy = new { ballotFormVersionId = ballot, participationPercentage = 100, approvalPercentage = 100,
                        allowAbstain = true, resultFieldKey = "leftAccepted" } }),
                Node("claim", "human.claim", new { nextNodeKeys = new[] { "join" }, assigneePolicy = sources, claimPolicy = new { allowRelease = true },
                    fieldPolicies = new { title = "readOnly", leftAccepted = "readOnly", rightText = "editable" } }),
                Node("join", "gateway.parallel", new { gatewayRoleKey = "join", forkNodeKey = "fork", nextNodeKeys = new[] { "end" } }),
                Node("end", "end", new { }) } } }, HttpStatusCode.Created);
        var version = (await PostAsync(owner, $"/api/v1/workflow/definitions/{definition.GetProperty("id").GetGuid():D}/publish",
            new { expectedRevision = 1, formVersionId = main })).GetProperty("id").GetGuid();
        var started = await PostAsync(owner, "/api/v1/workflow/instances", new { definitionVersionId = version, businessType = "human.parallel",
            businessId = "private-ballots", initialValues = new { title = "shared-main", leftAccepted = false, rightText = "initial" }, idempotencyKey = "parallel-start" }, HttpStatusCode.Created);
        var instance = started.GetProperty("id").GetGuid();
        Assert.AreEqual(2, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_todo WHERE InstanceId=@instance", new { instance }));
        var firstTodo = await VoteTodoAsync(db, instance, people[0].UserId); var secondTodo = await VoteTodoAsync(db, instance, people[1].UserId);
        var fileA = await UploadAsync(first); var fileB = await UploadAsync(second);
        var requestA = Cast(await RuntimeAsync(first, firstTodo), "first-private", fileA, "private-a");
        var receiptA = await PostAsync(first, $"/api/v1/workflow/todos/{firstTodo:D}/vote", requestA);
        Assert.AreEqual("{}", (await RuntimeAsync(second, secondTodo)).GetProperty("voteForm").GetProperty("values").GetRawText());
        string Attachment(Guid file, Guid? todo = null) => $"/api/v1/workflow/todos/{(todo ?? (file == fileA ? firstTodo : secondTodo)):D}/form-attachments/{file:D}/content";
        await StatusAsync(first, Attachment(fileA), HttpStatusCode.OK);
        await StatusAsync(second, Attachment(fileA), HttpStatusCode.Forbidden);
        await StatusAsync(owner, Attachment(fileA), HttpStatusCode.Forbidden);
        await StatusAsync(second, Attachment(fileA, secondTodo), HttpStatusCode.Forbidden);
        await StatusAsync(owner, $"/api/v1/workflow/instances/{instance:D}/form-attachments/{fileA:D}/content", HttpStatusCode.Forbidden);

        var pool = await db.ExecuteScalarAsync<Guid>("SELECT Id FROM fn_workflow_claim WHERE InstanceId=@instance", new { instance });
        var claims = await Task.WhenAll(first.PostAsJsonAsync($"/api/v1/workflow/claim-pools/{pool:D}/claim", new { expectedRevision = 1, idempotencyKey = "claim-first" }),
            second.PostAsJsonAsync($"/api/v1/workflow/claim-pools/{pool:D}/claim", new { expectedRevision = 1, idempotencyKey = "claim-second" }));
        using var claimA = claims[0]; using var claimB = claims[1];
        Assert.AreEqual(1, claims.Count(response => response.StatusCode == HttpStatusCode.OK));
        Assert.AreEqual(1, claims.Count(response => response.StatusCode == HttpStatusCode.Conflict));
        var winner = claimA.StatusCode == HttpStatusCode.OK ? first : second;
        var claimed = await (claimA.StatusCode == HttpStatusCode.OK ? claimA : claimB).Content.ReadFromJsonAsync<JsonElement>();
        var claimTodo = claimed.GetProperty("activeTodoId").GetGuid();
        var runtime = await RuntimeAsync(winner, claimTodo);
        Assert.AreEqual("initial", runtime.GetProperty("submission").GetProperty("rightText").GetString());
        await PostAsync(winner, $"/api/v1/workflow/todos/{claimTodo:D}/complete", new {
            expectedRevision = runtime.GetProperty("revision").GetInt64(), expectedFormRevision = runtime.GetProperty("submissionRevision").GetInt64(),
            fieldPatch = new { rightText = "right-only" }, idempotencyKey = "complete-right" });
        using (var shared = JsonDocument.Parse((await db.ExecuteScalarAsync<string>("SELECT SubmissionJson FROM fn_workflow_form_submission WHERE InstanceId=@instance", new { instance }))!))
        { Assert.AreEqual("initial", shared.RootElement.GetProperty("rightText").GetString()); Assert.IsFalse(shared.RootElement.GetProperty("leftAccepted").GetBoolean()); }
        var lastRuntime = await RuntimeAsync(second, secondTodo);
        Assert.AreEqual("initial", lastRuntime.GetProperty("submission").GetProperty("rightText").GetString());
        var final = await PostAsync(second, $"/api/v1/workflow/todos/{secondTodo:D}/vote", Cast(lastRuntime, "second-private", fileB, "private-b"));
        Assert.AreEqual("completed", final.GetProperty("statusKey").GetString());
        using (var shared = JsonDocument.Parse((await db.ExecuteScalarAsync<string>("SELECT SubmissionJson FROM fn_workflow_form_submission WHERE InstanceId=@instance", new { instance }))!))
        {
            Assert.AreEqual("shared-main", shared.RootElement.GetProperty("title").GetString());
            Assert.AreEqual("right-only", shared.RootElement.GetProperty("rightText").GetString()); Assert.IsTrue(shared.RootElement.GetProperty("leftAccepted").GetBoolean());
            Assert.IsFalse(shared.RootElement.TryGetProperty("evidence", out _));
        }
        Assert.AreEqual(2, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_vote_attachment WHERE InstanceId=@instance", new { instance }));
        Assert.AreEqual(2, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_files_file_reference_claim WHERE FileId IN @files AND ConsumerModule='workflow' AND State='active'", new { files = new[] { fileA, fileB } }));
        Assert.AreEqual("first-private", (await RuntimeAsync(first, firstTodo)).GetProperty("voteForm").GetProperty("values").GetProperty("title").GetString());
        Assert.AreEqual("second-private", (await RuntimeAsync(second, secondTodo)).GetProperty("voteForm").GetProperty("values").GetProperty("title").GetString());
        await StatusAsync(first, Attachment(fileA), HttpStatusCode.OK); await StatusAsync(second, Attachment(fileB), HttpStatusCode.OK);
        await StatusAsync(first, Attachment(fileB), HttpStatusCode.Forbidden); await StatusAsync(second, Attachment(fileA), HttpStatusCode.Forbidden);
        await StatusAsync(owner, Attachment(fileB), HttpStatusCode.Forbidden);
        await StatusAsync(winner, Attachment(winner == first ? fileA : fileB, claimTodo), HttpStatusCode.Forbidden);
        Assert.AreEqual(receiptA.GetRawText(), (await PostAsync(first, $"/api/v1/workflow/todos/{firstTodo:D}/vote", requestA)).GetRawText());
    }

    private static object Field(string key, string kind) => new { fieldKey = key, fieldTypeKey = kind, required = false, constraints = new { } };
    private static object Node(string key, string kind, object config) => new { nodeKey = key, nodeTypeKey = kind, nodeSchemaVersion = 1, config };
    private static async Task<Guid> FormAsync(HttpClient client, object[] fields)
    {
        var form = await PostAsync(client, "/api/v1/workflow/forms", new { formKey = "human.parallel." + Guid.NewGuid().ToString("N"),
            draft = new { schemaVersion = 1, adapterVersion = 1, sections = new[] { new { sectionKey = "main", fields } } } }, HttpStatusCode.Created);
        return (await PostAsync(client, $"/api/v1/workflow/forms/{form.GetProperty("id").GetGuid():D}/publish", new { expectedRevision = 1 })).GetProperty("id").GetGuid();
    }
    private static async Task<Guid> UploadAsync(HttpClient client)
    {
        using var body = new MultipartFormDataContent(); var bytes = new ByteArrayContent(Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+j2e8AAAAASUVORK5CYII="));
        bytes.Headers.ContentType = new("image/png"); body.Add(bytes, "file", "ballot.png");
        using var response = await client.PostAsync("/api/v1/files/host-files/", body);
        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }
    private static async Task StatusAsync(HttpClient client, string path, HttpStatusCode expected)
    { using var response = await client.GetAsync(path); Assert.AreEqual(expected, response.StatusCode, path + ": " + await response.Content.ReadAsStringAsync()); }
    private static object Cast(JsonElement runtime, string title, Guid file, string key) => new {
        expectedRevision = runtime.GetProperty("revision").GetInt64(), expectedBallotRevision = runtime.GetProperty("voteForm").GetProperty("revision").GetInt64(),
        choiceKey = "yes", values = new { title, evidence = new[] { file } }, idempotencyKey = key };
    private static Task<Guid> VoteTodoAsync(DbConnection db, Guid instance, Guid actor) => db.ExecuteScalarAsync<Guid>(
        "SELECT todo.Id FROM fn_workflow_todo AS todo INNER JOIN fn_workflow_step AS step ON step.Id=todo.StepId WHERE todo.InstanceId=@instance AND todo.AssigneeUserId=@actor AND step.NodeTypeKey='human.vote'", new { instance, actor });
    private static Task<JsonElement> RuntimeAsync(HttpClient client, Guid todo) => client.GetFromJsonAsync<JsonElement>($"/api/v1/workflow/todos/{todo:D}/runtime");
    private static HttpClient Client(FullNetApiFactory factory, HostTestIdentity user)
    { var client = factory.CreateClientForHost("localhost"); client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", user.AccessToken); return client; }
    private static async Task<JsonElement> PostAsync(HttpClient client, string path, object body, HttpStatusCode expected = HttpStatusCode.OK)
    { using var response = await client.PostAsJsonAsync(path, body); var text = await response.Content.ReadAsStringAsync(); Assert.AreEqual(expected, response.StatusCode, path + ": " + text); return JsonSerializer.Deserialize<JsonElement>(text); }
}
