using System.Data.Common;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Dapper;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.IntegrationTests.Migrations;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Tenancy.Contracts;
using Full.NET.Modules.Workflow.Contracts;
using Full.NET.Modules.Workflow.Execution;
using Full.NET.Modules.Workflow.Domain;
using Full.NET.Modules.Workflow.Persistence;
using Full.NET.Modules.Workflow.Serialization;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using MySqlConnector;

namespace Full.NET.IntegrationTests.Workflow;

/// <summary>双库验证完整人工目标的耐久退回，不使用历史代表人伪造单人活动。</summary>
[TestClass]
public sealed partial class WorkflowHumanReturnApiTests
{
    [TestMethod]
    public async Task Exclusive_return_with_sql_server() => await VerifyExclusiveAsync(DatabaseProvider.SqlServer, await SharedDatabaseFixture.CreateSqlServerDatabaseAsync());
    [TestMethod]
    public async Task Exclusive_return_with_mysql() => await VerifyExclusiveAsync(DatabaseProvider.MySql, await SharedDatabaseFixture.CreateMySqlDatabaseAsync());

    private static async Task VerifyExclusiveAsync(DatabaseProvider provider, string connection)
    {
        using var factory = new FullNetApiFactory(provider, connection); await factory.InitializeAsync(useSchemaTemplate: false);
        await using DbConnection db = provider == DatabaseProvider.SqlServer ? new SqlConnection(connection) : new MySqlConnection(Migration114To126RecoveryTestSupport.MySqlQueryConnectionString(connection));
        var identity = await factory.CreateHostIdentityAsync("return-end-" + Guid.NewGuid().ToString("N"),
            [WorkflowPermissions.FormsCreate, WorkflowPermissions.FormsPublish, WorkflowPermissions.DefinitionsCreate, WorkflowPermissions.DefinitionsPublish,
                WorkflowPermissions.InstancesStart, WorkflowPermissions.TodosRead, WorkflowPermissions.TodosApprove, WorkflowPermissions.TodosReturn]);
        using var host = Client(factory, identity); var form = await FormAsync(host);
        await ExclusiveCaseAsync(factory, db, host, identity.UserId, form, false);
        await ExclusiveCaseAsync(factory, db, host, identity.UserId, form, true);
        using var owner = factory.CreateClientForHost("localhost"); owner.DefaultRequestHeaders.Add("Origin", "http://localhost");
        var login = await PostAsync(owner, "/api/v1/auth/login", new LoginRequest("admin", FullNetApiFactory.TestPassword)); var token = login.GetProperty("accessToken").GetString()!;
        owner.DefaultRequestHeaders.Authorization = new("Bearer", token);
        var tenants = await owner.GetFromJsonAsync<TenantContextSummary[]>("/api/v1/tenancy/available"); var tenant = tenants!.Single(value => value.Identifier == "acme");
        owner.DefaultRequestHeaders.Authorization = new("Bearer", await IntegrationTestTenantContextHelper.SwitchToTenantAsync(owner, token, tenant.Id));
        var actor = (await owner.GetFromJsonAsync<JsonElement>("/api/v1/me")).GetProperty("id").GetGuid(); var tenantForm = await FormAsync(owner);
        await ExclusiveCaseAsync(factory, db, owner, actor, tenantForm, false);
        await ExclusiveCaseAsync(factory, db, owner, actor, tenantForm, true);
    }

    private static async Task ExclusiveCaseAsync(FullNetApiFactory factory, DbConnection db, HttpClient client, Guid actor, Guid form, bool nested)
    {
        var version = await PublishAsync(client, form, [Node("start", "start", new { nextNodeKeys = new[] { "before" } }), Human("before", "choice", [actor], "all"),
            Node("choice", "gateway.exclusive", new { nextNodeKeys = new[] { "left", "right" }, defaultNextNodeKey = "right", branches = new[] {
                new { branchKey = "selected", nextNodeKey = "left", condition = new { fieldKey = "title", @operator = "equals", value = "left" } } } }),
            Human("left", "end", [actor], "all"), Human("right", "end", [actor], "all"), Node("end", "end", new { })]);
        var root = (await PostAsync(client, "/api/v1/workflow/instances", new { definitionVersionId = version, businessType = "return-end",
            businessId = Guid.NewGuid().ToString("N"), initialValues = new { title = "left" }, idempotencyKey = Guid.NewGuid().ToString("N") }, HttpStatusCode.Created)).GetProperty("id").GetGuid();
        var target = await StepAsync(db, root, "before"); await ActAsync(client, await TodoAsync(db, root, actor), "approve", "initial-before");
        var source = await TodoAsync(db, root, actor); var runtime = await client.GetFromJsonAsync<JsonElement>($"/api/v1/workflow/todos/{source:D}/runtime");
        var request = new { targetStepId = target, expectedRevision = runtime.GetProperty("revision").GetInt64(), expectedFormRevision = runtime.GetProperty("submissionRevision").GetInt64(),
            fieldPatch = new { title = "right" }, comment = "返回条件之前，按本次固定输入重新选择分支", idempotencyKey = "exclusive-return" };
        var receipt = await PostAsync(client, $"/api/v1/workflow/todos/{source:D}/return", request);
        await using (var services = factory.Services.CreateAsyncScope()) Assert.AreEqual(1, await services.ServiceProvider.GetRequiredService<WorkflowActivityWorkProcessor>().ProcessPendingAsync(default));
        var renewedTarget = await StepAsync(db, root, "before"); await ActAsync(client, await TodoAsync(db, root, actor), "approve", "renewed-before");
        Assert.AreNotEqual(Guid.Empty, await StepAsync(db, root, "right"));
        if (nested)
        {
            var current = await TodoAsync(db, root, actor); runtime = await client.GetFromJsonAsync<JsonElement>($"/api/v1/workflow/todos/{current:D}/runtime");
            await PostAsync(client, $"/api/v1/workflow/todos/{current:D}/return", new { targetStepId = renewedTarget,
                expectedRevision = runtime.GetProperty("revision").GetInt64(), expectedFormRevision = runtime.GetProperty("submissionRevision").GetInt64(),
                fieldPatch = new { title = "left" }, comment = "同一共同终点的嵌套返回保留真实退出来源", idempotencyKey = "nested-exclusive-return" });
            await using (var services = factory.Services.CreateAsyncScope()) Assert.AreEqual(1, await services.ServiceProvider.GetRequiredService<WorkflowActivityWorkProcessor>().ProcessPendingAsync(default));
            await ActAsync(client, await TodoAsync(db, root, actor), "approve", "nested-before");
            Assert.AreNotEqual(Guid.Empty, await StepAsync(db, root, "left"));
        }
        await ActAsync(client, await TodoAsync(db, root, actor), "approve", "chosen-source");
        Assert.AreEqual("active", await db.ExecuteScalarAsync<string>("SELECT StatusKey FROM fn_workflow_instance WHERE Id=@root", new { root }));
        Assert.AreEqual(1, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_step WHERE InstanceId=@root AND NodeTypeKey='end'", new { root }));
        for (var count = 0; count < 4; count++)
        {
            await using var services = factory.Services.CreateAsyncScope();
            if (await services.ServiceProvider.GetRequiredService<WorkflowActivityWorkProcessor>().ProcessPendingAsync(default) == 0) break;
        }
        Assert.AreEqual("completed", await db.ExecuteScalarAsync<string>("SELECT StatusKey FROM fn_workflow_instance WHERE Id=@root", new { root }));
        Assert.AreEqual(nested ? 2 : 1, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_return_round WHERE InstanceId=@root AND Revision=2", new { root }));
        Assert.AreEqual(1, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_step WHERE InstanceId=@root AND ExecutionEpochId=@root AND NodeTypeKey='end'", new { root }));
        Assert.AreEqual(2, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_step WHERE InstanceId=@root AND NodeTypeKey='end'", new { root }));
        Assert.AreEqual(0, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_execution_epoch WHERE InstanceId=@root AND StatusKey='active'", new { root }));
        await WorkflowCausalityAssertions.CompleteRootAsync(db, root);
        Assert.AreEqual(receipt.GetRawText(), (await PostAsync(client, $"/api/v1/workflow/todos/{source:D}/return", request)).GetRawText());
    }

    [TestMethod]
    public async Task Human_return_with_sql_server() => await VerifyAsync(DatabaseProvider.SqlServer, await SharedDatabaseFixture.CreateSqlServerDatabaseAsync());
    [TestMethod]
    public async Task Human_return_with_mysql() => await VerifyAsync(DatabaseProvider.MySql, await SharedDatabaseFixture.CreateMySqlDatabaseAsync());
    private static async Task VerifyAsync(DatabaseProvider provider, string connection)
    {
        using var factory = new FullNetApiFactory(provider, connection); await factory.InitializeAsync(useSchemaTemplate: false);
        await using DbConnection db = provider == DatabaseProvider.SqlServer ? new SqlConnection(connection) : new MySqlConnection(Migration114To126RecoveryTestSupport.MySqlQueryConnectionString(connection));
        var owner = await factory.CreateHostIdentityAsync("return-owner-" + Guid.NewGuid().ToString("N"), [WorkflowPermissions.FormsCreate, WorkflowPermissions.FormsPublish,
            WorkflowPermissions.DefinitionsCreate, WorkflowPermissions.DefinitionsPublish, WorkflowPermissions.InstancesStart, WorkflowPermissions.InstancesRead, WorkflowPermissions.TodosRead, WorkflowPermissions.TodosReturn]);
        var permissions = new[] { WorkflowPermissions.TodosRead, WorkflowPermissions.TodosApprove, WorkflowPermissions.TodosComplete, WorkflowPermissions.TodosReturn };
        var first = await factory.CreateHostIdentityAsync("return-first-" + Guid.NewGuid().ToString("N"), permissions);
        var second = await factory.CreateHostIdentityAsync("return-second-" + Guid.NewGuid().ToString("N"), permissions);
        using var starter = Client(factory, owner); using var firstClient = Client(factory, first); using var secondClient = Client(factory, second);
        var form = await FormAsync(starter);
        foreach (var mode in new[] { "all", "sequential", "group", "task" })
            await CaseAsync(factory, db, starter, form, [firstClient, secondClient], [first.UserId, second.UserId], mode);
        await VerifyTenantAsync(factory, db, starter);
        await VerifyRoundRecoveryAsync(provider, connection, db);
    }

    private static async Task CaseAsync(FullNetApiFactory factory, DbConnection db, HttpClient starter, Guid form, HttpClient[] handlers, Guid[] users, string mode, HttpClient? foreign = null)
    {
        var version = await PublishAsync(starter, form, [Node("start", "start", new { nextNodeKeys = new[] { "target" } }),
            Human("target", "current", users, mode), Human("current", "end", users, "group"), Node("end", "end", new { })]);
        var instance = (await PostAsync(starter, "/api/v1/workflow/instances", new { definitionVersionId = version, businessType = "human-return", businessId = mode + Guid.NewGuid().ToString("N"), initialValues = new { }, idempotencyKey = Guid.NewGuid().ToString("N") }, HttpStatusCode.Created)).GetProperty("id").GetGuid();
        var target = await StepAsync(db, instance, "target");
        for (var index = 0; index < users.Length; index++) await ActAsync(handlers[index], await TodoAsync(db, instance, users[index]), mode == "task" ? "complete" : "approve", "initial-" + index);
        var current = await StepAsync(db, instance, "current"); var source = await TodoAsync(db, instance, users[0]);
        var unbound=Guid.CreateVersion7();
        // 同名且序号更大的完成记录不在实际链及有效绑定中，不能成为退回目标。
        await db.ExecuteAsync("INSERT INTO fn_workflow_step (Id,InstanceId,ExecutionEpochId,NodeKey,NodeTypeKey,StatusKey,AssignedUserId,DueAtUtc,AttemptCount,Revision,ExecutionSequence,StartedAtUtc,CompletedAtUtc,ParallelJoinId,ParallelBranchKey) SELECT @unbound,InstanceId,ExecutionEpochId,NodeKey,NodeTypeKey,'completed',NULL,NULL,0,1,ExecutionSequence+100,@now,@now,NULL,NULL FROM fn_workflow_step WHERE Id=@target",new { unbound,target,now=DateTimeOffset.UtcNow });
        if (foreign is not null) { using var denied = await foreign.GetAsync($"/api/v1/workflow/todos/{source:D}/return-targets"); Assert.AreEqual(HttpStatusCode.NotFound, denied.StatusCode); }
        var candidates = await handlers[0].GetFromJsonAsync<JsonElement>($"/api/v1/workflow/todos/{source:D}/return-targets");
        Assert.IsTrue(candidates.EnumerateArray().Any(item => item.GetProperty("stepId").GetGuid() == target));
        Assert.IsFalse(candidates.EnumerateArray().Any(item=>item.GetProperty("stepId").GetGuid()==unbound));
        Assert.AreEqual(JsonValueKind.Null, candidates.EnumerateArray().Single(item => item.GetProperty("stepId").GetGuid() == target).GetProperty("assigneeUserId").ValueKind);
        var runtime = await handlers[0].GetFromJsonAsync<JsonElement>($"/api/v1/workflow/todos/{source:D}/runtime");
        await VerifyAtomicRoundRollbackAsync(factory, db, instance, current, target);
        await PostAsync(handlers[0],$"/api/v1/workflow/todos/{source:D}/return",new { targetStepId=unbound,expectedRevision=runtime.GetProperty("revision").GetInt64(),
            expectedFormRevision=runtime.GetProperty("submissionRevision").GetInt64(),fieldPatch=new { },comment="拒绝非实际祖先",idempotencyKey="unbound-"+mode },HttpStatusCode.UnprocessableEntity);
        var request = new { targetStepId = target, expectedRevision = runtime.GetProperty("revision").GetInt64(), expectedFormRevision = runtime.GetProperty("submissionRevision").GetInt64(), fieldPatch = new { }, comment = "补齐资料后重新办理", idempotencyKey = "return-" + mode };
        // 两个相同请求只落一条固定回执和一个耐久激活，不把后来产生的待办写回首次结果。
        var receipts = await Task.WhenAll(PostAsync(handlers[0], $"/api/v1/workflow/todos/{source:D}/return", request), PostAsync(handlers[0], $"/api/v1/workflow/todos/{source:D}/return", request));
        Assert.AreEqual(receipts[0].GetRawText(), receipts[1].GetRawText()); Assert.AreEqual(JsonValueKind.Null, receipts[0].GetProperty("activeTodoId").ValueKind);
        Assert.AreEqual("rolled_back", await db.ExecuteScalarAsync<string>("SELECT StatusKey FROM fn_workflow_step WHERE Id=@target", new { target }));
        Assert.AreEqual("returned", await db.ExecuteScalarAsync<string>("SELECT StatusKey FROM fn_workflow_step WHERE Id=@current", new { current }));
        Assert.AreEqual(0, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_approval_group WHERE StepId=@current AND StatusKey='active'", new { current }));
        Assert.AreEqual(0, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_todo WHERE InstanceId=@instance AND StatusKey='active'", new { instance }));
        Assert.AreEqual(1, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_activity_work WHERE InstanceId=@instance AND StatusKey='pending'", new { instance }));
        Assert.AreEqual(current,await db.ExecuteScalarAsync<Guid>("SELECT SourceStepId FROM fn_workflow_activity_work WHERE InstanceId=@instance AND StatusKey='pending' AND CauseKindKey='return'",new { instance }));
        var roundId = await db.ExecuteScalarAsync<Guid>("SELECT Id FROM fn_workflow_return_round WHERE InstanceId=@instance", new { instance });
        Assert.AreNotEqual(instance, roundId);
        Assert.AreEqual(instance, await db.ExecuteScalarAsync<Guid>("SELECT ParentEpochId FROM fn_workflow_execution_epoch WHERE Id=@roundId", new { roundId }));
        Assert.AreEqual(roundId, await db.ExecuteScalarAsync<Guid>("SELECT ExecutionEpochId FROM fn_workflow_activity_work WHERE ReturnRoundId=@roundId", new { roundId }));
        await using (var scope = factory.Services.CreateAsyncScope()) Assert.AreEqual(1, await scope.ServiceProvider.GetRequiredService<WorkflowActivityWorkProcessor>().ProcessPendingAsync(default));
        var reactivated = await StepAsync(db, instance, "target"); Assert.AreNotEqual(target, reactivated);
        Assert.AreEqual(roundId, await db.ExecuteScalarAsync<Guid>("SELECT ExecutionEpochId FROM fn_workflow_step WHERE Id=@reactivated", new { reactivated }));
        Assert.AreEqual(await db.ExecuteScalarAsync<string>("SELECT AssigneeSnapshotHash FROM fn_workflow_step WHERE Id=@target", new { target }),
            await db.ExecuteScalarAsync<string>("SELECT AssigneeSnapshotHash FROM fn_workflow_step WHERE Id=@reactivated", new { reactivated }));
        Assert.AreEqual(1,await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_step_cause WHERE InstanceId=@instance AND StepId=@reactivated AND ParentStepId=@current AND KindKey='return'",new { instance,reactivated,current }));
        Assert.IsNotNull(await db.ExecuteScalarAsync<string>("SELECT AssigneeSnapshotHash FROM fn_workflow_step WHERE Id=@reactivated", new { reactivated }));
        Assert.AreEqual(2, await db.ExecuteScalarAsync<int>(mode == "task" ? "SELECT COUNT(*) FROM fn_workflow_task_member WHERE TaskId=@reactivated" : "SELECT COUNT(*) FROM fn_workflow_approval_slot WHERE StepId=@reactivated", new { reactivated }));
        var replay = await PostAsync(handlers[0], $"/api/v1/workflow/todos/{source:D}/return", request); Assert.AreEqual(receipts[0].GetRawText(), replay.GetRawText());
        for (var index = 0; index < users.Length; index++) await ActAsync(handlers[index], await TodoAsync(db, instance, users[index]), mode == "task" ? "complete" : "approve", "again-" + index);
        if (mode is "all" or "group") await ReturnAgainAsync(factory, db, instance, reactivated, handlers, users);
        else for (var index = 0; index < users.Length; index++) await ActAsync(handlers[index], await TodoAsync(db, instance, users[index]), "approve", "finish-" + index);
        Assert.AreEqual("closed", await db.ExecuteScalarAsync<string>("SELECT StatusKey FROM fn_workflow_execution_epoch WHERE Id=@roundId", new { roundId }));
        Assert.AreEqual(2L, await db.ExecuteScalarAsync<long>("SELECT Revision FROM fn_workflow_return_round WHERE Id=@roundId AND ExitWorkId IS NOT NULL", new { roundId }));
        await using (var scope = factory.Services.CreateAsyncScope()) Assert.AreEqual(1, await scope.ServiceProvider.GetRequiredService<WorkflowActivityWorkProcessor>().ProcessPendingAsync(default));
        Assert.AreEqual(2, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_activity_work WHERE ReturnRoundId=@roundId AND StatusKey='completed'", new { roundId }));
        Assert.AreEqual("completed", await db.ExecuteScalarAsync<string>("SELECT StatusKey FROM fn_workflow_instance WHERE Id=@instance", new { instance }));
        replay = await PostAsync(handlers[0], $"/api/v1/workflow/todos/{source:D}/return", request); Assert.AreEqual(receipts[0].GetRawText(), replay.GetRawText());
    }

    private static async Task VerifyAtomicRoundRollbackAsync(FullNetApiFactory factory, DbConnection db, Guid root, Guid source, Guid target)
    {
        var tenant = await db.ExecuteScalarAsync<Guid?>("SELECT TenantId FROM fn_workflow_instance WHERE Id=@root", new { root });
        var scopeKey = tenant is { } tenantId ? $"tenant:{tenantId:N}" : "host";
        var sourceRevision = await db.ExecuteScalarAsync<long>("SELECT Revision FROM fn_workflow_step WHERE Id=@source", new { source });
        var targetSequence = await db.ExecuteScalarAsync<long>("SELECT ExecutionSequence FROM fn_workflow_step WHERE Id=@target", new { target });
        var targetHash = (await db.ExecuteScalarAsync<string>("SELECT AssigneeSnapshotHash FROM fn_workflow_step WHERE Id=@target", new { target }))!;
        var before = await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_activation_binding WHERE InstanceId=@root", new { root });
        var fault = await Assert.ThrowsExactlyAsync<InvalidOperationException>(async () =>
        {
            await using var services = factory.Services.CreateAsyncScope();
            var writer = services.ServiceProvider.GetRequiredService<ICurrentTenantContextWriter>();
            if (tenant is { } id) writer.SetTenant((await services.ServiceProvider.GetRequiredService<IActiveTenantContextResolver>().ResolveActiveByIdAsync(id, default))!);
            else writer.SetHost();
            try
            {
                var query = services.ServiceProvider.GetRequiredService<IQueryExecutor>(); var commands = services.ServiceProvider.GetRequiredService<ICommandExecutor>();
                await services.ServiceProvider.GetRequiredService<ICommandTransaction>().ExecuteAsync<bool>(async token =>
                {
                    Assert.AreEqual(1, await commands.ExecuteAsync(WorkflowSql.LockInstanceForTodoAction,
                        WorkflowSqlParameters.Create(("Id", root), ("TenantScopeKey", scopeKey)), token));
                    var instance = (await query.QuerySingleOrDefaultAsync<WorkflowInstanceRecord>(WorkflowSql.FindInstanceById,
                        WorkflowSqlParameters.Create(("Id", root), ("TenantScopeKey", scopeKey)), token))!;
                    var asset = (await query.QuerySingleOrDefaultAsync<WorkflowRuntimeAssetRecord>(WorkflowSql.FindRuntimeAsset,
                        WorkflowSqlParameters.Create(("DefinitionVersionId", instance.DefinitionVersionId), ("TenantScopeKey", scopeKey)), token))!;
                    Assert.IsTrue(WorkflowRuntimePlan.TryCreate(JsonSerializer.Deserialize(asset.CanonicalJson, WorkflowJsonSerializerContext.Default.WorkflowDefinitionDraft)!,
                        JsonSerializer.Deserialize(asset.FormSchemaJson, WorkflowJsonSerializerContext.Default.WorkflowFormSchema), out var plan));
                    Assert.AreEqual(1, await commands.ExecuteAsync(WorkflowSql.ReturnStepWithRevision,
                        WorkflowSqlParameters.Create(("Id", source), ("InstanceId", root), ("CompletedAtUtc", DateTimeOffset.UtcNow), ("Revision", sourceRevision)), token));
                    await commands.ExecuteAsync(WorkflowReturnClosureSql.InvalidateSteps,
                        WorkflowSqlParameters.Create(("InstanceId", root), ("TenantScopeKey", scopeKey), ("InvalidatedStepIds", new[] { target })), token);
                    var submission = (await query.QuerySingleOrDefaultAsync<WorkflowFormSubmissionRecord>(WorkflowSql.FindFormSubmissionByInstance,
                        WorkflowSqlParameters.Create(("InstanceId", root), ("TenantScopeKey", scopeKey)), token))!;
                    var sequence = await query.QuerySingleOrDefaultAsync<long>(WorkflowSql.FindNextStepExecutionSequence,
                        WorkflowSqlParameters.Create(("InstanceId", root)), token);
                    var entered = await services.ServiceProvider.GetRequiredService<WorkflowReturnRoundCoordinator>().EnterAsync(instance, plan!, source, target, "target", targetHash,
                        JsonSerializer.Deserialize(submission.SubmissionJson, WorkflowJsonSerializerContext.Default.DictionaryStringJsonElement)!, submission.Revision,
                        sequence, DateTimeOffset.UtcNow, token);
                    Assert.IsTrue(entered.IsSuccess);
                    throw new InvalidOperationException("return.round.rollback.probe");
                }, default);
            }
            finally { writer.Clear(); }
        });
        Assert.AreEqual("return.round.rollback.probe", fault.Message);
        Assert.AreEqual("active", await db.ExecuteScalarAsync<string>("SELECT StatusKey FROM fn_workflow_step WHERE Id=@source", new { source }));
        Assert.AreEqual(sourceRevision, await db.ExecuteScalarAsync<long>("SELECT Revision FROM fn_workflow_step WHERE Id=@source", new { source }));
        Assert.AreEqual("completed", await db.ExecuteScalarAsync<string>("SELECT StatusKey FROM fn_workflow_step WHERE Id=@target", new { target }));
        Assert.AreEqual(0, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_return_round WHERE InstanceId=@root", new { root }));
        Assert.AreEqual(0, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_activity_work WHERE InstanceId=@root", new { root }));
        Assert.AreEqual(1, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_execution_epoch WHERE InstanceId=@root", new { root }));
        Assert.AreEqual(before, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_activation_binding WHERE InstanceId=@root", new { root }));
    }

    private static async Task ReturnAgainAsync(FullNetApiFactory factory, DbConnection db, Guid instance, Guid target, HttpClient[] handlers, Guid[] users)
    {
        var todo = await TodoAsync(db, instance, users[0]);
        var runtime = await handlers[0].GetFromJsonAsync<JsonElement>($"/api/v1/workflow/todos/{todo:D}/runtime");
        await PostAsync(handlers[0], $"/api/v1/workflow/todos/{todo:D}/return", new { targetStepId = target,
            expectedRevision = runtime.GetProperty("revision").GetInt64(), expectedFormRevision = runtime.GetProperty("submissionRevision").GetInt64(),
            fieldPatch = new { }, comment = "同一出口再次退回后逐层恢复真实外层", idempotencyKey = "nested-return" });
        await using (var scope = factory.Services.CreateAsyncScope()) Assert.AreEqual(1, await scope.ServiceProvider.GetRequiredService<WorkflowActivityWorkProcessor>().ProcessPendingAsync(default));
        for (var index = 0; index < users.Length; index++) await ActAsync(handlers[index], await TodoAsync(db, instance, users[index]), "approve", "nested-target-" + index);
        for (var index = 0; index < users.Length; index++) await ActAsync(handlers[index], await TodoAsync(db, instance, users[index]), "approve", "nested-current-" + index);
        Assert.AreEqual(2, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_return_round WHERE InstanceId=@instance", new { instance }));
        // 首次退出只关闭内轮；原父轮由该唯一leased工作再次核对来源后级联退出。
        Assert.AreEqual(1, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_return_round WHERE InstanceId=@instance AND Revision=1", new { instance }));
        await using (var scope = factory.Services.CreateAsyncScope()) Assert.AreEqual(1, await scope.ServiceProvider.GetRequiredService<WorkflowActivityWorkProcessor>().ProcessPendingAsync(default));
        Assert.AreEqual(0, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_return_round WHERE InstanceId=@instance AND Revision=1", new { instance }));
    }

    private static async Task VerifyRoundRecoveryAsync(DatabaseProvider provider, string connection, DbConnection db)
    {
        var rounds = (await db.QueryAsync<Guid>("SELECT Id FROM fn_workflow_return_round ORDER BY CreatedAtUtc,Id")).ToArray();
        Assert.IsTrue(rounds.Length >= 2);
        var foreignRoot = await db.ExecuteScalarAsync<Guid>("SELECT InstanceId FROM fn_workflow_return_round WHERE Id=@id", new { id = rounds[^1] });
        var work = await db.ExecuteScalarAsync<Guid>("SELECT EntryWorkId FROM fn_workflow_return_round WHERE Id=@id", new { id = rounds[^1] });
        await Assert.ThrowsAsync<DbException>(async () => { _ = await db.ExecuteAsync("UPDATE fn_workflow_return_round SET ExitSourceEpochId=@foreignRoot WHERE Id=@id", new { foreignRoot, id = rounds[0] }); });
        await Assert.ThrowsAsync<DbException>(async () => { _ = await db.ExecuteAsync("UPDATE fn_workflow_return_round SET EntrySourceEpochId=@foreignRoot WHERE Id=@id", new { foreignRoot, id = rounds[0] }); });
        await Assert.ThrowsAsync<DbException>(async () => { _ = await db.ExecuteAsync("UPDATE fn_workflow_activity_work SET ReturnRoundId=@id WHERE Id=@work", new { id = rounds[0], work }); });
        await db.ExecuteAsync(provider == DatabaseProvider.SqlServer ? "ALTER TABLE fn_workflow_activity_work DROP CONSTRAINT fk_fn_workflow_activity_work_return" : "ALTER TABLE fn_workflow_activity_work DROP FOREIGN KEY fk_fn_workflow_activity_work_return");
        await db.ExecuteAsync(provider == DatabaseProvider.SqlServer ? "DROP INDEX ux_fn_workflow_return_round_exit_work ON fn_workflow_return_round" : "ALTER TABLE fn_workflow_return_round DROP INDEX ux_fn_workflow_return_round_exit_work");
        await db.ExecuteAsync(provider == DatabaseProvider.SqlServer ? "ALTER TABLE fn_workflow_return_round DROP CONSTRAINT fk_fn_workflow_return_round_entry_epoch" : "ALTER TABLE fn_workflow_return_round DROP FOREIGN KEY fk_fn_workflow_return_round_entry_epoch");
        await db.ExecuteAsync(provider == DatabaseProvider.SqlServer ? "ALTER TABLE fn_workflow_return_round ALTER COLUMN EntrySourceEpochId uniqueidentifier NULL" : "ALTER TABLE fn_workflow_return_round MODIFY COLUMN EntrySourceEpochId binary(16) NULL COMMENT '原进入来源待恢复'");
        await db.ExecuteAsync("UPDATE fn_workflow_return_round SET EntrySourceEpochId=NULL");
        await db.ExecuteAsync(provider == DatabaseProvider.SqlServer ? "DELETE FROM dbo.SchemaVersions WHERE ScriptName LIKE '%329_WorkflowReturnRounds.sql'" : "DELETE FROM schemaversions WHERE ScriptName LIKE '%329_WorkflowReturnRounds.sql'");
        await WorkflowHumanMigrationRecoveryTests.MigrateAsync(provider, connection); await WorkflowHumanMigrationRecoveryTests.MigrateAsync(provider, connection);
        CollectionAssert.AreEquivalent(rounds, (await db.QueryAsync<Guid>("SELECT Id FROM fn_workflow_return_round")).ToArray());
        Assert.AreEqual(0, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_return_round AS round INNER JOIN fn_workflow_step AS step ON step.Id=round.SourceStepId AND step.InstanceId=round.InstanceId WHERE round.EntrySourceEpochId IS NULL OR round.EntrySourceEpochId<>step.ExecutionEpochId"));

    }

    private static async Task VerifyTenantAsync(FullNetApiFactory factory, DbConnection db, HttpClient host)
    {
        using var owner = factory.CreateClientForHost("localhost"); owner.DefaultRequestHeaders.Add("Origin", "http://localhost");
        var login = await PostAsync(owner, "/api/v1/auth/login", new LoginRequest("admin", FullNetApiFactory.TestPassword)); var token = login.GetProperty("accessToken").GetString()!;
        owner.DefaultRequestHeaders.Authorization = new("Bearer", token); var tenants = await owner.GetFromJsonAsync<TenantContextSummary[]>("/api/v1/tenancy/available"); var tenant = tenants!.Single(value => value.Identifier == "acme");
        owner.DefaultRequestHeaders.Authorization = new("Bearer", await IntegrationTestTenantContextHelper.SwitchToTenantAsync(owner, token, tenant.Id));
        var ownerId = (await owner.GetFromJsonAsync<JsonElement>("/api/v1/me")).GetProperty("id").GetGuid();
        var username = "return-tenant-" + Guid.NewGuid().ToString("N"); var email = username + "@example.com";
        var member = await factory.CreateHostIdentityAsync(username, [WorkflowPermissions.TodosRead, WorkflowPermissions.TodosApprove, "tenancy.tenants.switch"], password: FullNetApiFactory.TestPassword);
        await factory.EnsureHostUserProfileEmailAsync(member.UserId, email, username); using var handler = factory.CreateClientForHost("localhost"); handler.DefaultRequestHeaders.Add("Origin", "http://localhost");
        token = await IntegrationTestAuthHelper.LoginAsHostUserAsync(handler, username, FullNetApiFactory.TestPassword); handler.DefaultRequestHeaders.Authorization = new("Bearer", token);
        var invitation = await PostAsync(owner, "/api/v1/identity/tenant-members/invitations", new CreateTenantInvitationRequest(email, TenantMemberRoles.Member));
        await PostAsync(handler, $"/api/v1/me/tenant-invitations/{invitation.GetProperty("invitation").GetProperty("id").GetGuid():D}/accept", new { });
        handler.DefaultRequestHeaders.Authorization = new("Bearer", await IntegrationTestTenantContextHelper.SwitchToTenantAsync(handler, token, tenant.Id));
        await CaseAsync(factory, db, owner, await FormAsync(owner), [owner, handler], [ownerId, member.UserId], "group", host);
    }
    private static object Human(string key, string next, Guid[] users, string mode) => mode == "group"
        ? Node(key, "human.group_approval", new { nextNodeKeys = new[] { next }, groupApprovalPolicy = new { modeKey = "all" }, groups = users.Select((id, index) => new { groupKey = "g" + index, assigneePolicy = new { sources = new[] { new { resolverKindKey = "specified_users", userIds = new[] { id } } } } }).ToArray() })
        : mode == "task" ? Node(key, "human.task", new { nextNodeKeys = new[] { next }, taskPolicy = new { completionModeKey = "sequential" }, assigneePolicy = new { sources = new[] { new { resolverKindKey = "specified_users", userIds = users } } } })
        : users.Length == 1 ? Node(key, "human.approval", new { nextNodeKeys = new[] { next }, assigneePolicy = new { sources = new[] { new { resolverKindKey = "specified_users", userIds = users } } } })
        : Node(key, "human.approval", new { nextNodeKeys = new[] { next }, approvalPolicy = new { modeKey = mode, approverUserIds = users } });
    private static async Task ActAsync(HttpClient client, Guid todo, string action, string key)
    { var runtime = await client.GetFromJsonAsync<JsonElement>($"/api/v1/workflow/todos/{todo:D}/runtime"); await PostAsync(client, $"/api/v1/workflow/todos/{todo:D}/{action}", new { expectedRevision = runtime.GetProperty("revision").GetInt64(), expectedFormRevision = runtime.GetProperty("submissionRevision").GetInt64(), fieldPatch = new { }, idempotencyKey = key }); }
    private static Task<Guid> StepAsync(DbConnection db, Guid instance, string node) => db.ExecuteScalarAsync<Guid>("SELECT Id FROM fn_workflow_step WHERE InstanceId=@instance AND NodeKey=@node AND StatusKey='active'", new { instance, node });
    private static Task<Guid> TodoAsync(DbConnection db, Guid instance, Guid user) => db.ExecuteScalarAsync<Guid>("SELECT Id FROM fn_workflow_todo WHERE InstanceId=@instance AND AssigneeUserId=@user AND StatusKey='active'", new { instance, user });
    private static object Node(string key, string kind, object config) => new { nodeKey = key, nodeTypeKey = kind, nodeSchemaVersion = 1, config };
    private static async Task<Guid> FormAsync(HttpClient client)
    { var form = await PostAsync(client, "/api/v1/workflow/forms", new { formKey = "return." + Guid.NewGuid().ToString("N"), draft = new { schemaVersion = 1, adapterVersion = 1, sections = new[] { new { sectionKey = "main", fields = new[] { new { fieldKey = "title", fieldTypeKey = "text", required = false, constraints = new { } } } } } } }, HttpStatusCode.Created); return (await PostAsync(client, $"/api/v1/workflow/forms/{form.GetProperty("id").GetGuid():D}/publish", new { expectedRevision = 1 })).GetProperty("id").GetGuid(); }
    private static async Task<Guid> PublishAsync(HttpClient client, Guid form, object[] nodes)
    { var draft = await PostAsync(client, "/api/v1/workflow/definitions", new { definitionKey = "return." + Guid.NewGuid().ToString("N"), draft = new { schemaVersion = 1, workflowKindKey = "automation", nodes } }, HttpStatusCode.Created); return (await PostAsync(client, $"/api/v1/workflow/definitions/{draft.GetProperty("id").GetGuid():D}/publish", new { expectedRevision = 1, formVersionId = form })).GetProperty("id").GetGuid(); }
    private static HttpClient Client(FullNetApiFactory factory, HostTestIdentity identity) { var client = factory.CreateClientForHost("localhost"); client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", identity.AccessToken); return client; }
    private static async Task<JsonElement> PostAsync(HttpClient client, string path, object request, HttpStatusCode expected = HttpStatusCode.OK)
    { using var response = await client.PostAsJsonAsync(path, request); var text = await response.Content.ReadAsStringAsync(); Assert.AreEqual(expected, response.StatusCode, path + ": " + text); return JsonSerializer.Deserialize<JsonElement>(text); }
}
