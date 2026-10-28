using System.Data.Common;
using System.Net;
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

public sealed partial class WorkflowHumanReturnApiTests
{
    [TestMethod]
    public async Task Route_inside_return_with_sql_server() => await VerifyRouteReturnAsync(DatabaseProvider.SqlServer, await SharedDatabaseFixture.CreateSqlServerDatabaseAsync());
    [TestMethod]
    public async Task Route_inside_return_with_mysql() => await VerifyRouteReturnAsync(DatabaseProvider.MySql, await SharedDatabaseFixture.CreateMySqlDatabaseAsync());

    private static async Task VerifyRouteReturnAsync(DatabaseProvider provider, string connection)
    {
        using var factory = new FullNetApiFactory(provider, connection); await factory.InitializeAsync(useSchemaTemplate: false);
        await using DbConnection db = provider == DatabaseProvider.SqlServer ? new SqlConnection(connection) : new MySqlConnection(Migration114To126RecoveryTestSupport.MySqlQueryConnectionString(connection));
        var identity = await factory.CreateHostIdentityAsync("return-route-" + Guid.NewGuid().ToString("N"),
            [WorkflowPermissions.FormsCreate, WorkflowPermissions.FormsPublish, WorkflowPermissions.DefinitionsCreate, WorkflowPermissions.DefinitionsPublish,
                WorkflowPermissions.InstancesStart, WorkflowPermissions.TodosRead, WorkflowPermissions.TodosApprove, WorkflowPermissions.TodosReturn]);
        using var host = Client(factory, identity); await RouteReturnCaseAsync(factory, db, host, identity.UserId, await FormAsync(host));
        using var owner = factory.CreateClientForHost("localhost"); owner.DefaultRequestHeaders.Add("Origin", "http://localhost");
        var login = await PostAsync(owner, "/api/v1/auth/login", new LoginRequest("admin", FullNetApiFactory.TestPassword)); var token = login.GetProperty("accessToken").GetString()!;
        owner.DefaultRequestHeaders.Authorization = new("Bearer", token); var tenants = await owner.GetFromJsonAsync<TenantContextSummary[]>("/api/v1/tenancy/available");
        var tenant = tenants!.Single(value => value.Identifier == "acme"); owner.DefaultRequestHeaders.Authorization = new("Bearer", await IntegrationTestTenantContextHelper.SwitchToTenantAsync(owner, token, tenant.Id));
        var actor = (await owner.GetFromJsonAsync<JsonElement>("/api/v1/me")).GetProperty("id").GetGuid();
        await RouteReturnCaseAsync(factory, db, owner, actor, await FormAsync(owner));
    }

    /// <summary>Return包含一个严格更小的Route回边；两类收据分别关闭自身代次并恢复实际父代次。</summary>
    private static async Task RouteReturnCaseAsync(FullNetApiFactory factory, DbConnection db, HttpClient client, Guid actor, Guid form)
    {
        var version = await PublishAsync(client, form, [Node("start", "start", new { nextNodeKeys = new[] { "before" } }), Human("before", "route", [actor], "all"),
            Node("route", "control.route", new { nextNodeKeys = new[] { "source" }, routePolicy = new { modeKey = "rules", targets = new[] {
                new { targetKey = "retry", nodeKey = "before", regionKey = "inside-return", maximumIterations = 2,
                    condition = new { fieldKey = "title", @operator = "equals", value = "retry" } } } } }),
            Human("source", "end", [actor], "all"), Node("end", "end", new { })]);
        var root = (await PostAsync(client, "/api/v1/workflow/instances", new { definitionVersionId = version, businessType = "return-route",
            businessId = Guid.NewGuid().ToString("N"), initialValues = new { title = "stop" }, idempotencyKey = Guid.NewGuid().ToString("N") }, HttpStatusCode.Created)).GetProperty("id").GetGuid();
        var target = await StepAsync(db, root, "before"); await ActAsync(client, await NodeTodoAsync(db, root, "before"), "approve", "initial-before"); await DrainAsync(factory);
        var original = await NodeTodoAsync(db, root, "source"); var runtime = await client.GetFromJsonAsync<JsonElement>($"/api/v1/workflow/todos/{original:D}/runtime");
        var candidates = await client.GetFromJsonAsync<JsonElement>($"/api/v1/workflow/todos/{original:D}/return-targets");
        Assert.IsTrue(candidates.EnumerateArray().Any(candidate => candidate.GetProperty("stepId").GetGuid() == target));
        await PostAsync(client, $"/api/v1/workflow/todos/{original:D}/return", new { targetStepId = target, expectedRevision = runtime.GetProperty("revision").GetInt64(),
            expectedFormRevision = runtime.GetProperty("submissionRevision").GetInt64(), fieldPatch = new { title = "retry" }, comment = "在退回轮次内执行受限路由", idempotencyKey = "return-with-route" });
        var round = await db.ExecuteScalarAsync<Guid>("SELECT Id FROM fn_workflow_return_round WHERE InstanceId=@root", new { root }); await DrainAsync(factory);
        await ActAsync(client, await NodeTodoAsync(db, root, "before"), "approve", "return-before"); await DrainAsync(factory);
        var innerBefore = await StepAsync(db, root, "before"); var routeEpoch = await db.ExecuteScalarAsync<Guid>("SELECT ExecutionEpochId FROM fn_workflow_step WHERE Id=@innerBefore", new { innerBefore });
        Assert.AreNotEqual(round, routeEpoch); Assert.AreEqual(round, await db.ExecuteScalarAsync<Guid>("SELECT ParentEpochId FROM fn_workflow_execution_epoch WHERE Id=@routeEpoch", new { routeEpoch }));
        Assert.AreEqual("active", await db.ExecuteScalarAsync<string>("SELECT StatusKey FROM fn_workflow_execution_epoch WHERE Id=@round", new { round }));
        var innerTodo = await NodeTodoAsync(db, root, "before"); runtime = await client.GetFromJsonAsync<JsonElement>($"/api/v1/workflow/todos/{innerTodo:D}/runtime");
        await PostAsync(client, $"/api/v1/workflow/todos/{innerTodo:D}/approve", new { expectedRevision = runtime.GetProperty("revision").GetInt64(),
            expectedFormRevision = runtime.GetProperty("submissionRevision").GetInt64(), fieldPatch = new { title = "stop" }, idempotencyKey = "stop-inner-route" }); await DrainAsync(factory);
        Assert.AreEqual("closed", await db.ExecuteScalarAsync<string>("SELECT StatusKey FROM fn_workflow_execution_epoch WHERE Id=@routeEpoch", new { routeEpoch }));
        var source = await StepAsync(db, root, "source"); Assert.AreEqual(round, await db.ExecuteScalarAsync<Guid>("SELECT ExecutionEpochId FROM fn_workflow_step WHERE Id=@source", new { source }));
        Assert.AreEqual(1L, await db.ExecuteScalarAsync<long>("SELECT Revision FROM fn_workflow_return_round WHERE Id=@round", new { round }));
        await ActAsync(client, await NodeTodoAsync(db, root, "source"), "approve", "return-source"); await DrainAsync(factory);
        Assert.AreEqual("completed", await db.ExecuteScalarAsync<string>("SELECT StatusKey FROM fn_workflow_instance WHERE Id=@root", new { root }));
        Assert.AreEqual(2L, await db.ExecuteScalarAsync<long>("SELECT Revision FROM fn_workflow_return_round WHERE Id=@round AND ExitWorkId IS NOT NULL", new { round }));
        Assert.AreEqual(0, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_execution_epoch WHERE InstanceId=@root AND StatusKey='active'", new { root }));
        Assert.AreEqual(3, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_route_decision WHERE InstanceId=@root AND StatusKey='applied'", new { root }));
        await WorkflowCausalityAssertions.CompleteRootAsync(db, root);
    }
}
