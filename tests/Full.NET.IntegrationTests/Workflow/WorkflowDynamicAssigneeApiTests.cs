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

/// <summary>双库通过真实发布、办理、转办和 Worker 续办验证动态来源，不用客户端候选替代身份或历史事实。</summary>
[TestClass]
public sealed class WorkflowDynamicAssigneeApiTests
{
    [TestMethod]
    public async Task Dynamic_assignees_with_sql_server() => await VerifyAsync(DatabaseProvider.SqlServer, await SharedDatabaseFixture.CreateSqlServerDatabaseAsync());
    [TestMethod]
    public async Task Dynamic_assignees_with_mysql() => await VerifyAsync(DatabaseProvider.MySql, await SharedDatabaseFixture.CreateMySqlDatabaseAsync());
    private static async Task VerifyAsync(DatabaseProvider provider, string connection)
    {
        using var factory = new FullNetApiFactory(provider, connection); await factory.InitializeAsync(useSchemaTemplate: false);
        await using DbConnection db = provider == DatabaseProvider.SqlServer ? new SqlConnection(connection)
            : new MySqlConnection(Migration114To126RecoveryTestSupport.MySqlQueryConnectionString(connection));
        var owner = await factory.CreateHostIdentityAsync("dynamic-owner-" + Guid.NewGuid().ToString("N"),
            [WorkflowPermissions.FormsCreate, WorkflowPermissions.FormsPublish, WorkflowPermissions.DefinitionsCreate, WorkflowPermissions.DefinitionsPublish,
                WorkflowPermissions.InstancesStart, WorkflowPermissions.InstancesRead, WorkflowPermissions.ClaimPoolsClaim]);
        var first = await factory.CreateHostIdentityAsync("dynamic-first-" + Guid.NewGuid().ToString("N"),
            [WorkflowPermissions.TodosRead, WorkflowPermissions.TodosComplete, WorkflowPermissions.TodosTransfer, WorkflowPermissions.ClaimPoolsClaim]);
        var second = await factory.CreateHostIdentityAsync("dynamic-second-" + Guid.NewGuid().ToString("N"),
            [WorkflowPermissions.TodosRead, WorkflowPermissions.TodosComplete, WorkflowPermissions.TodosTransfer, WorkflowPermissions.ClaimPoolsClaim]);
        using var starter = Client(factory, owner); using var original = Client(factory, first); using var handler = Client(factory, second);
        var form = await FormAsync(starter, "users", "select", new[] { first.UserId, second.UserId });
        var fieldVersion = await PublishAsync(starter, form, [Node("start", "start", new { nextNodeKeys = new[] { "task" } }),
            TaskNode("task", "end", new { resolverKindKey = "form_users", fieldKey = "users" }), Node("end", "end", new { })]);
        var fieldInstance = await StartAsync(starter, fieldVersion, "form-user", new { users = second.UserId.ToString("D") });
        Assert.AreEqual(second.UserId, await db.ExecuteScalarAsync<Guid>("SELECT AssigneeUserId FROM fn_workflow_todo WHERE InstanceId=@instance AND StatusKey='active'", new { instance = fieldInstance }));
        var fieldTodo = await db.ExecuteScalarAsync<Guid>("SELECT Id FROM fn_workflow_todo WHERE InstanceId=@instance AND StatusKey='active'", new { instance = fieldInstance });
        await PostAsync(original, $"/api/v1/workflow/todos/{fieldTodo:D}/complete", new { expectedRevision = 1, expectedFormRevision = 1, fieldPatch = new { }, idempotencyKey = "foreign-form-user" }, HttpStatusCode.Forbidden);
        await CompleteAsync(handler, fieldTodo, "field-complete");

        var previousVersion = await PublishAsync(starter, form, [Node("start", "start", new { nextNodeKeys = new[] { "first" } }),
            TaskNode("first", "second", new { resolverKindKey = "specified_users", userIds = new[] { first.UserId } }),
            TaskNode("second", "end", new { resolverKindKey = "previous_node_actors", nodeKey = "first" }), Node("end", "end", new { })]);
        var previous = await StartAsync(starter, previousVersion, "previous-user", new { users = first.UserId.ToString("D") });
        var todo = await db.ExecuteScalarAsync<Guid>("SELECT Id FROM fn_workflow_todo WHERE InstanceId=@previous AND StatusKey='active'", new { previous });
        var runtime = await RuntimeAsync(original, todo);
        var moved = await PostAsync(original, $"/api/v1/workflow/todos/{todo:D}/transfer", new { assigneeUserId = second.UserId,
            expectedRevision = runtime.GetProperty("revision").GetInt64(), expectedFormRevision = runtime.GetProperty("submissionRevision").GetInt64(), idempotencyKey = "transfer-first" });
        var completed = await CompleteAsync(handler, moved.GetProperty("activeTodoId").GetGuid(), "actual-first-handler");
        Assert.AreEqual(JsonValueKind.Null, completed.GetProperty("activeTodoId").ValueKind);
        Assert.AreEqual(1, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_activity_work WHERE InstanceId=@previous AND StatusKey='pending'", new { previous }));
        var actualStep=await db.ExecuteScalarAsync<Guid>("SELECT StepId FROM fn_workflow_activation_binding WHERE InstanceId=@previous AND ExecutionEpochId=@previous AND NodeKey='first'",new { previous });
        // 更晚的同名完成记录没有被本轮实际激活绑定，不能挤掉转办后真实办理人的步骤。
        await db.ExecuteAsync("INSERT INTO fn_workflow_step (Id,InstanceId,ExecutionEpochId,NodeKey,NodeTypeKey,StatusKey,AssignedUserId,DueAtUtc,AttemptCount,Revision,ExecutionSequence,StartedAtUtc,CompletedAtUtc,ParallelJoinId,ParallelBranchKey) SELECT @id,InstanceId,ExecutionEpochId,NodeKey,NodeTypeKey,'completed',NULL,NULL,0,1,ExecutionSequence+100,@now,@now,NULL,NULL FROM fn_workflow_step WHERE Id=@actualStep",new { id=Guid.CreateVersion7(),actualStep,now=DateTimeOffset.UtcNow });
        await using (var scope = factory.Services.CreateAsyncScope()) Assert.AreEqual(1, await scope.ServiceProvider.GetRequiredService<WorkflowActivityWorkProcessor>().ProcessPendingAsync(default));
        var next = await db.QuerySingleAsync<ActualTodo>("SELECT Id, AssigneeUserId FROM fn_workflow_todo WHERE InstanceId=@previous AND StatusKey='active'", new { previous });
        Assert.AreEqual(second.UserId, next.AssigneeUserId); Assert.AreNotEqual(first.UserId, next.AssigneeUserId);
        await CompleteAsync(handler, next.Id, "previous-complete");
        Assert.AreEqual("completed", await db.ExecuteScalarAsync<string>("SELECT StatusKey FROM fn_workflow_instance WHERE Id=@previous", new { previous }));

        var claimVersion = await PublishAsync(starter, form, [Node("start", "start", new { nextNodeKeys = new[] { "claim" } }),
            Node("claim", "human.claim", new { nextNodeKeys = new[] { "end" }, claimPolicy = new { allowRelease = true },
                assigneePolicy = new { sources = new[] { new { resolverKindKey = "form_users", fieldKey = "users" } } } }), Node("end", "end", new { })]);
        var claim = await StartAsync(starter, claimVersion, "form-claim", new { users = second.UserId.ToString("D") });
        var pool = await db.ExecuteScalarAsync<Guid>("SELECT Id FROM fn_workflow_claim WHERE InstanceId=@claim", new { claim });
        await PostAsync(original, $"/api/v1/workflow/claim-pools/{pool:D}/claim", new { expectedRevision = 1, idempotencyKey = "not-selected" }, HttpStatusCode.Forbidden);
        var claimed = await PostAsync(handler, $"/api/v1/workflow/claim-pools/{pool:D}/claim", new { expectedRevision = 1, idempotencyKey = "selected" });
        await CompleteAsync(handler, claimed.GetProperty("activeTodoId").GetGuid(), "form-claim-complete");
        await VerifyTenantDepartmentAsync(factory, db, starter);
    }

    private static async Task VerifyTenantDepartmentAsync(FullNetApiFactory factory, DbConnection db, HttpClient host)
    {
        using var client = factory.CreateClientForHost("localhost"); client.DefaultRequestHeaders.Add("Origin", "http://localhost");
        var login = await PostAsync(client, "/api/v1/auth/login", new LoginRequest("admin", FullNetApiFactory.TestPassword));
        var token = login.GetProperty("accessToken").GetString()!; client.DefaultRequestHeaders.Authorization = new("Bearer", token);
        var tenants = await client.GetFromJsonAsync<TenantContextSummary[]>("/api/v1/tenancy/available"); var tenant = tenants!.Single(x => x.Identifier == "acme");
        client.DefaultRequestHeaders.Authorization = new("Bearer", await IntegrationTestTenantContextHelper.SwitchToTenantAsync(client, token, tenant.Id));
        var actor = (await client.GetFromJsonAsync<JsonElement>("/api/v1/me")).GetProperty("id").GetGuid();
        var unit = (await PostAsync(client, "/api/v1/organization/units", new { parentId = (Guid?)null, code = "wf-dynamic-" + Guid.NewGuid().ToString("N")[..12],
            name = "表单部门", displayOrder = 1 }, HttpStatusCode.Created)).GetProperty("id").GetGuid();
        await PostAsync(client, "/api/v1/organization/user-units", new { userId = actor, unitId = unit, isPrimary = false }, HttpStatusCode.Created);
        var form = await FormAsync(client, "units", "select", new[] { unit });
        var version = await PublishAsync(client, form, [Node("start", "start", new { nextNodeKeys = new[] { "claim" } }),
            Node("claim", "human.claim", new { nextNodeKeys = new[] { "end" }, claimPolicy = new { allowRelease = false },
                assigneePolicy = new { sources = new[] { new { resolverKindKey = "form_units", fieldKey = "units" } } } }), Node("end", "end", new { })]);
        var instance = await StartAsync(client, version, "tenant-form-unit", new { units = unit.ToString("D") });
        var pool = await db.ExecuteScalarAsync<Guid>("SELECT Id FROM fn_workflow_claim WHERE InstanceId=@instance", new { instance });
        await PostAsync(host, $"/api/v1/workflow/claim-pools/{pool:D}/claim", new { expectedRevision = 1, idempotencyKey = "wrong-scope" }, HttpStatusCode.NotFound);
        // 对真实组织绑定施加撤销故障，旧候选和来源快照不能保留当前成员资格。
        await db.ExecuteAsync("UPDATE fn_organization_user_unit SET IsActive=0 WHERE UserId=@actor AND UnitId=@unit AND TenantId=@tenant", new { actor, unit, tenant = tenant.Id });
        await PostAsync(client, $"/api/v1/workflow/claim-pools/{pool:D}/claim", new { expectedRevision = 1, idempotencyKey = "removed-unit" }, HttpStatusCode.Forbidden);
        await db.ExecuteAsync("UPDATE fn_organization_user_unit SET IsActive=1 WHERE UserId=@actor AND UnitId=@unit AND TenantId=@tenant", new { actor, unit, tenant = tenant.Id });
        await PostAsync(client, $"/api/v1/workflow/claim-pools/{pool:D}/claim", new { expectedRevision = 1, idempotencyKey = "restored-unit" });
    }
    private static async Task<Guid> FormAsync(HttpClient client, string field, string kind, Guid[] choices)
    {
        var form = await PostAsync(client, "/api/v1/workflow/forms", new { formKey = "dynamic." + Guid.NewGuid().ToString("N"), draft = new { schemaVersion = 1, adapterVersion = 1,
            sections = new[] { new { sectionKey = "main", fields = new[] { new { fieldKey = field, fieldTypeKey = kind, required = false,
                constraints = new { options = choices.Select(id => id.ToString("D")).ToArray() } } } } } } }, HttpStatusCode.Created);
        return (await PostAsync(client, $"/api/v1/workflow/forms/{form.GetProperty("id").GetGuid():D}/publish", new { expectedRevision = 1 })).GetProperty("id").GetGuid();
    }
    private static async Task<Guid> PublishAsync(HttpClient client, Guid form, object[] nodes)
    {
        var draft = await PostAsync(client, "/api/v1/workflow/definitions", new { definitionKey = "dynamic." + Guid.NewGuid().ToString("N"), draft = new { schemaVersion = 1, workflowKindKey = "automation", nodes } }, HttpStatusCode.Created);
        return (await PostAsync(client, $"/api/v1/workflow/definitions/{draft.GetProperty("id").GetGuid():D}/publish", new { expectedRevision = 1, formVersionId = form })).GetProperty("id").GetGuid();
    }
    private static async Task<Guid> StartAsync(HttpClient client, Guid version, string key, object values) => (await PostAsync(client, "/api/v1/workflow/instances", new {
        definitionVersionId = version, businessType = "dynamic-assignee", businessId = key, initialValues = values, idempotencyKey = key }, HttpStatusCode.Created)).GetProperty("id").GetGuid();
    private static async Task<JsonElement> CompleteAsync(HttpClient client, Guid todo, string key)
    { var runtime = await RuntimeAsync(client, todo); return await PostAsync(client, $"/api/v1/workflow/todos/{todo:D}/complete", new { expectedRevision = runtime.GetProperty("revision").GetInt64(),
        expectedFormRevision = runtime.GetProperty("submissionRevision").GetInt64(), fieldPatch = new { }, idempotencyKey = key }); }
    private static Task<JsonElement> RuntimeAsync(HttpClient client, Guid todo) => client.GetFromJsonAsync<JsonElement>($"/api/v1/workflow/todos/{todo:D}/runtime");
    private static object Node(string key, string kind, object config) => new { nodeKey = key, nodeTypeKey = kind, nodeSchemaVersion = 1, config };
    private static object TaskNode(string key, string next, object source) => Node(key, "human.task", new { nextNodeKeys = new[] { next }, taskPolicy = new { completionModeKey = "all" }, assigneePolicy = new { sources = new[] { source } } });
    private static HttpClient Client(FullNetApiFactory factory, HostTestIdentity identity) { var client = factory.CreateClientForHost("localhost"); client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", identity.AccessToken); return client; }
    private static async Task<JsonElement> PostAsync(HttpClient client, string path, object request, HttpStatusCode expected = HttpStatusCode.OK)
    { using var response = await client.PostAsJsonAsync(path, request); var text = await response.Content.ReadAsStringAsync(); Assert.AreEqual(expected, response.StatusCode, path + ": " + text); return JsonSerializer.Deserialize<JsonElement>(text); }
    private sealed record ActualTodo(Guid Id, Guid AssigneeUserId);
}
