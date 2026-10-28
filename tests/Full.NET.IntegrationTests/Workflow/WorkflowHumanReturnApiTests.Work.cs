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
using Full.NET.Modules.Workflow.Domain;
using Full.NET.Modules.Workflow.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using MySqlConnector;

namespace Full.NET.IntegrationTests.Workflow;

public sealed partial class WorkflowHumanReturnApiTests
{
    [TestMethod]
    public async Task Deferred_return_work_with_sql_server() => await VerifyDeferredReturnAsync(DatabaseProvider.SqlServer, await SharedDatabaseFixture.CreateSqlServerDatabaseAsync());
    [TestMethod]
    public async Task Deferred_return_work_with_mysql() => await VerifyDeferredReturnAsync(DatabaseProvider.MySql, await SharedDatabaseFixture.CreateMySqlDatabaseAsync());

    private static async Task VerifyDeferredReturnAsync(DatabaseProvider provider, string connection)
    {
        using var factory = new FullNetApiFactory(provider, connection); await factory.InitializeAsync(useSchemaTemplate: false);
        await using DbConnection db = provider == DatabaseProvider.SqlServer ? new SqlConnection(connection) : new MySqlConnection(Migration114To126RecoveryTestSupport.MySqlQueryConnectionString(connection));
        var identity = await factory.CreateHostIdentityAsync("return-work-" + Guid.NewGuid().ToString("N"),
            [WorkflowPermissions.FormsCreate, WorkflowPermissions.FormsPublish, WorkflowPermissions.DefinitionsCreate, WorkflowPermissions.DefinitionsPublish,
                WorkflowPermissions.InstancesStart, WorkflowPermissions.InstancesCancel, WorkflowPermissions.TodosRead, WorkflowPermissions.TodosApprove, WorkflowPermissions.TodosReturn]);
        using var host = Client(factory, identity); var form = await FormAsync(host);
        await DeferredWorkCaseAsync(factory, db, host, identity.UserId, form, false); await DeferredWorkCaseAsync(factory, db, host, identity.UserId, form, true);
        using var owner = factory.CreateClientForHost("localhost"); owner.DefaultRequestHeaders.Add("Origin", "http://localhost");
        var login = await PostAsync(owner, "/api/v1/auth/login", new LoginRequest("admin", FullNetApiFactory.TestPassword)); var token = login.GetProperty("accessToken").GetString()!;
        owner.DefaultRequestHeaders.Authorization = new("Bearer", token); var tenants = await owner.GetFromJsonAsync<TenantContextSummary[]>("/api/v1/tenancy/available");
        var tenant = tenants!.Single(value => value.Identifier == "acme"); owner.DefaultRequestHeaders.Authorization = new("Bearer", await IntegrationTestTenantContextHelper.SwitchToTenantAsync(owner, token, tenant.Id));
        var actor = (await owner.GetFromJsonAsync<JsonElement>("/api/v1/me")).GetProperty("id").GetGuid(); var tenantForm = await FormAsync(owner);
        await DeferredWorkCaseAsync(factory, db, owner, actor, tenantForm, false); await DeferredWorkCaseAsync(factory, db, owner, actor, tenantForm, true);
    }

    /// <summary>正常API执行超过单次预算后产生真实工作；整体退回必须使已领取的旧工作无法提交。</summary>
    private static async Task DeferredWorkCaseAsync(FullNetApiFactory factory, DbConnection db, HttpClient client, Guid actor, Guid form, bool claimed)
    {
        var nodes = new List<object> { Node("start", "start", new { nextNodeKeys = new[] { "before" } }), Human("before", "fork", [actor], "all"),
            Node("fork", "gateway.parallel", new { gatewayRoleKey = "fork", joinNodeKey = "join", branches = new[] {
                new { branchKey = "left", nextNodeKey = "source" }, new { branchKey = "right", nextNodeKey = "empty0" } } }),
            Human("source", "join", [actor], "all"), Human("right", "join", [actor], "all"),
            Node("join", "gateway.parallel", new { gatewayRoleKey = "join", forkNodeKey = "fork", nextNodeKeys = new[] { "end" } }), Node("end", "end", new { }) };
        for (var index = 0; index < 40; index++) nodes.Add(Node("empty" + index, "control.empty", new { nextNodeKeys = new[] { index == 39 ? "right" : "empty" + (index + 1) } }));
        var version = await PublishAsync(client, form, nodes.ToArray());
        var root = (await PostAsync(client, "/api/v1/workflow/instances", new { definitionVersionId = version, businessType = "return-work",
            businessId = Guid.NewGuid().ToString("N"), initialValues = new { title = "initial" }, idempotencyKey = Guid.NewGuid().ToString("N") }, HttpStatusCode.Created)).GetProperty("id").GetGuid();
        var target = await StepAsync(db, root, "before"); await ActAsync(client, await NodeTodoAsync(db, root, "before"), "approve", "before");
        var work = await db.QuerySingleAsync<DeferredWorkProof>("SELECT Id,ExecutionEpochId,ParallelJoinId,ParallelBranchKey,NodeKey,LeaseGeneration FROM fn_workflow_activity_work WHERE InstanceId=@root AND StatusKey='pending'", new { root });
        Assert.IsNotNull(work.ParallelJoinId); Assert.AreEqual("right", work.ParallelBranchKey);
        var owner = "return-lease-probe"; var generation = work.LeaseGeneration;
        if (claimed)
        {
            await using var services = factory.Services.CreateAsyncScope();
            Assert.AreEqual(1, await services.ServiceProvider.GetRequiredService<ICommandExecutor>().ExecuteAsync(WorkflowActivitySql.ClaimWork,
                WorkflowSqlParameters.Create(("Id", work.Id), ("LeaseGeneration", generation), ("LeaseOwnerKey", owner), ("LeaseExpiresAtUtc", DateTimeOffset.UtcNow.AddMinutes(5)), ("Now", DateTimeOffset.UtcNow)), default));
            generation++;
            Assert.AreEqual("leased", await db.ExecuteScalarAsync<string>("SELECT StatusKey FROM fn_workflow_activity_work WHERE Id=@id", new { id = work.Id }));
        }
        var todo = await NodeTodoAsync(db, root, "source"); var runtime = await client.GetFromJsonAsync<JsonElement>($"/api/v1/workflow/todos/{todo:D}/runtime");
        var request = new { targetStepId = target, expectedRevision = runtime.GetProperty("revision").GetInt64(), expectedFormRevision = runtime.GetProperty("submissionRevision").GetInt64(),
            fieldPatch = new { }, comment = "整体退回使旧分支工作及租约失效", idempotencyKey = "return-deferred" };
        var receipt = await PostAsync(client, $"/api/v1/workflow/todos/{todo:D}/return", request);
        Assert.AreEqual("cancelled", await db.ExecuteScalarAsync<string>("SELECT StatusKey FROM fn_workflow_activity_work WHERE Id=@id", new { id = work.Id }));
        Assert.AreEqual(generation + 1, await db.ExecuteScalarAsync<long>("SELECT LeaseGeneration FROM fn_workflow_activity_work WHERE Id=@id AND LeaseOwnerKey IS NULL AND LeaseExpiresAtUtc IS NULL", new { id = work.Id }));
        await using (var services = factory.Services.CreateAsyncScope())
        {
            var query = services.ServiceProvider.GetRequiredService<IQueryExecutor>(); var scopeKey = await db.ExecuteScalarAsync<string>("SELECT TenantScopeKey FROM fn_workflow_instance WHERE Id=@root", new { root });
            var instance = (await query.QuerySingleOrDefaultAsync<WorkflowInstanceRecord>(WorkflowSql.FindInstanceById, WorkflowSqlParameters.Create(("Id", root), ("TenantScopeKey", scopeKey)), default))!;
            Assert.IsFalse(await WorkflowExecutionFrameScope.IsValidAsync(query, instance, work.ExecutionEpochId, work.ParallelJoinId, work.ParallelBranchKey, default));
            if (claimed) Assert.AreEqual(0, await services.ServiceProvider.GetRequiredService<ICommandExecutor>().ExecuteAsync(WorkflowActivitySql.FinishWork,
                WorkflowSqlParameters.Create(("Id", work.Id), ("LeaseOwnerKey", owner), ("LeaseGeneration", generation), ("Now", DateTimeOffset.UtcNow),
                    ("StatusKey", "completed"), ("ResultStatusKey", "active"), ("ResultTodoId", null)), default));
        }
        Assert.AreEqual(receipt.GetRawText(), (await PostAsync(client, $"/api/v1/workflow/todos/{todo:D}/return", request)).GetRawText());
        Assert.AreEqual(generation + 1, await db.ExecuteScalarAsync<long>("SELECT LeaseGeneration FROM fn_workflow_activity_work WHERE Id=@id", new { id = work.Id }));
        await DrainAsync(factory); await ActAsync(client, await NodeTodoAsync(db, root, "before"), "approve", "new-before"); await DrainAsync(factory);
        Assert.AreEqual(0, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_step WHERE InstanceId=@root AND NodeKey=@node AND ParallelJoinId=@fork", new { root, node = work.NodeKey, fork = work.ParallelJoinId }));
        Assert.AreNotEqual(Guid.Empty, await StepAsync(db, root, "right"));
        Assert.AreEqual("active", await db.ExecuteScalarAsync<string>("SELECT StatusKey FROM fn_workflow_instance WHERE Id=@root", new { root }));
        await PostAsync(client, $"/api/v1/workflow/instances/{root:D}/cancel", new { expectedRevision = await db.ExecuteScalarAsync<long>("SELECT Revision FROM fn_workflow_instance WHERE Id=@root", new { root }),
            reason = "关闭新分支验收资源", idempotencyKey = "close-new-work" });
    }
    private sealed record DeferredWorkProof(Guid Id, Guid ExecutionEpochId, Guid? ParallelJoinId, string? ParallelBranchKey, string NodeKey, long LeaseGeneration);
}
