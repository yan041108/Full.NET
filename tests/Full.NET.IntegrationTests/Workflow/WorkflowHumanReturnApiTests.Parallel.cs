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
using Full.NET.Modules.Workflow.Execution;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using MySqlConnector;

namespace Full.NET.IntegrationTests.Workflow;

public sealed partial class WorkflowHumanReturnApiTests
{
    [TestMethod]
    public async Task Parallel_return_with_sql_server() => await VerifyParallelAsync(DatabaseProvider.SqlServer, await SharedDatabaseFixture.CreateSqlServerDatabaseAsync());
    [TestMethod]
    public async Task Parallel_return_with_mysql() => await VerifyParallelAsync(DatabaseProvider.MySql, await SharedDatabaseFixture.CreateMySqlDatabaseAsync());

    private static async Task VerifyParallelAsync(DatabaseProvider provider, string connection)
    {
        using var factory = new FullNetApiFactory(provider, connection); await factory.InitializeAsync(useSchemaTemplate: false);
        await using DbConnection db = provider == DatabaseProvider.SqlServer ? new SqlConnection(connection) : new MySqlConnection(Migration114To126RecoveryTestSupport.MySqlQueryConnectionString(connection));
        var identity = await factory.CreateHostIdentityAsync("return-parallel-" + Guid.NewGuid().ToString("N"),
            [WorkflowPermissions.FormsCreate, WorkflowPermissions.FormsPublish, WorkflowPermissions.DefinitionsCreate, WorkflowPermissions.DefinitionsPublish,
                WorkflowPermissions.InstancesStart, WorkflowPermissions.InstancesCancel, WorkflowPermissions.TodosRead, WorkflowPermissions.TodosApprove, WorkflowPermissions.TodosReturn]);
        var partnerName = "return-partner-" + Guid.NewGuid().ToString("N"); var partnerEmail = partnerName + "@example.test";
        var partner = await factory.CreateHostIdentityAsync(partnerName, [WorkflowPermissions.TodosRead, WorkflowPermissions.TodosApprove, "tenancy.tenants.switch"], password: FullNetApiFactory.TestPassword);
        await factory.EnsureHostUserProfileEmailAsync(partner.UserId, partnerEmail, partnerName);
        using var host = Client(factory, identity); var form = await ParallelFormAsync(host);
        await ParallelCaseAsync(factory, db, host, identity.UserId, form, false);
        await ParallelCaseAsync(factory, db, host, identity.UserId, form, true);
        await ParallelCaseAsync(factory, db, host, identity.UserId, form, true, true);
        foreach (var kind in new[] { "group", "task", "vote", "claim", "delay" }) await WaitingScopeCaseAsync(factory, db, host, identity.UserId, form, partner.UserId, kind);
        using var owner = factory.CreateClientForHost("localhost"); owner.DefaultRequestHeaders.Add("Origin", "http://localhost");
        var login = await PostAsync(owner, "/api/v1/auth/login", new LoginRequest("admin", FullNetApiFactory.TestPassword)); var token = login.GetProperty("accessToken").GetString()!;
        owner.DefaultRequestHeaders.Authorization = new("Bearer", token);
        var tenants = await owner.GetFromJsonAsync<Full.NET.Modules.Tenancy.Contracts.TenantContextSummary[]>("/api/v1/tenancy/available");
        var tenant = tenants!.Single(value => value.Identifier == "acme");
        owner.DefaultRequestHeaders.Authorization = new("Bearer", await IntegrationTestTenantContextHelper.SwitchToTenantAsync(owner, token, tenant.Id));
        using var partnerClient = factory.CreateClientForHost("localhost"); partnerClient.DefaultRequestHeaders.Add("Origin", "http://localhost");
        partnerClient.DefaultRequestHeaders.Authorization = new("Bearer", await IntegrationTestAuthHelper.LoginAsHostUserAsync(partnerClient, partnerName, FullNetApiFactory.TestPassword));
        var invitation = await PostAsync(owner, "/api/v1/identity/tenant-members/invitations", new CreateTenantInvitationRequest(partnerEmail, TenantMemberRoles.Member));
        await PostAsync(partnerClient, $"/api/v1/me/tenant-invitations/{invitation.GetProperty("invitation").GetProperty("id").GetGuid():D}/accept", new { });
        var actor = (await owner.GetFromJsonAsync<JsonElement>("/api/v1/me")).GetProperty("id").GetGuid(); var tenantForm = await ParallelFormAsync(owner);
        await ParallelCaseAsync(factory, db, owner, actor, tenantForm, false);
        await ParallelCaseAsync(factory, db, owner, actor, tenantForm, true);
        await ParallelCaseAsync(factory, db, owner, actor, tenantForm, true, true);
        foreach (var kind in new[] { "group", "task", "vote", "claim", "delay" }) await WaitingScopeCaseAsync(factory, db, owner, actor, tenantForm, partner.UserId, kind);
    }

    /// <summary>局部退回保持原兄弟；整体退回取消原fork，私有轮次重建新fork后只退出一次。</summary>
    private static async Task ParallelCaseAsync(FullNetApiFactory factory, DbConnection db, HttpClient client, Guid actor, Guid form, bool whole, bool escalated = false)
    {
        var version = await PublishAsync(client, form, [Node("start", "start", new { nextNodeKeys = new[] { "before" } }), Human("before", "fork", [actor], "all"),
            Node("fork", "gateway.parallel", new { gatewayRoleKey = "fork", joinNodeKey = "join", branches = new[] {
                new { branchKey = "left", nextNodeKey = "leftTarget" }, new { branchKey = "right", nextNodeKey = "right" } } }),
            Human("leftTarget", "leftSource", [actor], "all"), Human("leftSource", "join", [actor], "all"), Human("right", "join", [actor], "all"),
            Node("join", "gateway.parallel", new { gatewayRoleKey = "join", forkNodeKey = "fork", nextNodeKeys = new[] { "after" } }),
            Human("after", "end", [actor], "all"), Node("end", "end", new { })]);
        var root = (await PostAsync(client, "/api/v1/workflow/instances", new { definitionVersionId = version, businessType = "return-parallel",
            businessId = Guid.NewGuid().ToString("N"), initialValues = new { title = "initial", untouched = "original" }, idempotencyKey = Guid.NewGuid().ToString("N") }, HttpStatusCode.Created)).GetProperty("id").GetGuid();
        var before = await StepAsync(db, root, "before"); await ActAsync(client, await NodeTodoAsync(db, root, "before"), "approve", "initial-before");
        var fork = await db.ExecuteScalarAsync<Guid>("SELECT Id FROM fn_workflow_parallel_join WHERE InstanceId=@root AND StatusKey='waiting'", new { root });
        var oldRight = await NodeTodoAsync(db, root, "right"); var leftTarget = await StepAsync(db, root, "leftTarget");
        await ActAsync(client, await NodeTodoAsync(db, root, "leftTarget"), "approve", "initial-left-target");
        var source = await NodeTodoAsync(db, root, "leftSource"); var runtime = await client.GetFromJsonAsync<JsonElement>($"/api/v1/workflow/todos/{source:D}/runtime");
        Guid? cancelledRound = null;
        if (escalated)
        {
            await PostAsync(client, $"/api/v1/workflow/todos/{source:D}/return", new { targetStepId = leftTarget,
                expectedRevision = runtime.GetProperty("revision").GetInt64(), expectedFormRevision = runtime.GetProperty("submissionRevision").GetInt64(),
                fieldPatch = new { title = "local-first" }, comment = "先在原分支建立局部新轮次", idempotencyKey = "first-local-return" });
            cancelledRound = await db.ExecuteScalarAsync<Guid>("SELECT Id FROM fn_workflow_return_round WHERE InstanceId=@root", new { root });
            await DrainAsync(factory); leftTarget = await StepAsync(db, root, "leftTarget");
            await ActAsync(client, await NodeTodoAsync(db, root, "leftTarget"), "approve", "first-local-target");
            source = await NodeTodoAsync(db, root, "leftSource"); runtime = await client.GetFromJsonAsync<JsonElement>($"/api/v1/workflow/todos/{source:D}/runtime");
        }
        var target = whole ? before : leftTarget;
        var candidates = await client.GetFromJsonAsync<JsonElement>($"/api/v1/workflow/todos/{source:D}/return-targets?page=1&pageSize=1");
        Assert.AreEqual(leftTarget, candidates[0].GetProperty("stepId").GetGuid());
        var secondPage = await client.GetFromJsonAsync<JsonElement>($"/api/v1/workflow/todos/{source:D}/return-targets?page=2&pageSize=1");
        Assert.AreEqual(before, secondPage[0].GetProperty("stepId").GetGuid());
        var request = new { targetStepId = target, expectedRevision = runtime.GetProperty("revision").GetInt64(), expectedFormRevision = runtime.GetProperty("submissionRevision").GetInt64(),
            fieldPatch = new { title = whole ? "whole-output" : "local-output" }, comment = "按实际范围退回，保留不受影响的分支", idempotencyKey = "parallel-return" };
        if (whole)
        {
            // 模拟另一个受控生产者已提交的外层修订；分支仍持有原共同输入与独立修订。
            await db.ExecuteAsync("UPDATE fn_workflow_form_submission SET SubmissionJson=@json,Revision=Revision+1 WHERE InstanceId=@root",
                new { root, json = JsonSerializer.Serialize(new { title = "conflicting-parent", untouched = "outer-update" }) });
            await PostAsync(client, $"/api/v1/workflow/todos/{source:D}/return", request, HttpStatusCode.Conflict);
            Assert.AreEqual("active", await db.ExecuteScalarAsync<string>("SELECT StatusKey FROM fn_workflow_todo WHERE Id=@source", new { source }));
            Assert.AreEqual("waiting", await db.ExecuteScalarAsync<string>("SELECT StatusKey FROM fn_workflow_parallel_join WHERE Id=@fork", new { fork }));
            Assert.AreEqual(escalated ? 1 : 0, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_return_round WHERE InstanceId=@root", new { root }));
            await db.ExecuteAsync("UPDATE fn_workflow_form_submission SET SubmissionJson=@json,Revision=Revision+1 WHERE InstanceId=@root",
                new { root, json = JsonSerializer.Serialize(new { title = "initial", untouched = "outer-update" }) });
        }
        var receipts = await Task.WhenAll(PostAsync(client, $"/api/v1/workflow/todos/{source:D}/return", request), PostAsync(client, $"/api/v1/workflow/todos/{source:D}/return", request));
        Assert.AreEqual(receipts[0].GetRawText(), receipts[1].GetRawText());
        Assert.AreEqual(whole ? "cancelled" : "active", await db.ExecuteScalarAsync<string>("SELECT StatusKey FROM fn_workflow_todo WHERE Id=@oldRight", new { oldRight }));
        Assert.AreEqual(whole ? "cancelled" : "waiting", await db.ExecuteScalarAsync<string>("SELECT StatusKey FROM fn_workflow_parallel_join WHERE Id=@fork", new { fork }));
        using (var submission = JsonDocument.Parse((await db.ExecuteScalarAsync<string>("SELECT SubmissionJson FROM fn_workflow_form_submission WHERE InstanceId=@root", new { root }))!))
            Assert.AreEqual(whole ? "whole-output" : "initial", submission.RootElement.GetProperty("title").GetString());
        var round = await db.ExecuteScalarAsync<Guid>("SELECT Id FROM fn_workflow_return_round WHERE InstanceId=@root AND SourceStepId=(SELECT StepId FROM fn_workflow_todo WHERE Id=@source)", new { root, source });
        Assert.AreEqual(root, await db.ExecuteScalarAsync<Guid>("SELECT ParentEpochId FROM fn_workflow_return_round WHERE Id=@round", new { round }));
        Assert.AreEqual(cancelledRound ?? root, await db.ExecuteScalarAsync<Guid>("SELECT EntrySourceEpochId FROM fn_workflow_return_round WHERE Id=@round", new { round }));
        if (cancelledRound is { } closed)
        {
            Assert.AreEqual("closed", await db.ExecuteScalarAsync<string>("SELECT StatusKey FROM fn_workflow_execution_epoch WHERE Id=@closed", new { closed }));
            Assert.AreEqual(1, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_return_round WHERE Id=@closed AND Revision=1 AND ExitWorkId IS NULL", new { closed }));
        }
        Assert.AreEqual(whole ? (Guid?)null : fork, await db.ExecuteScalarAsync<Guid?>("SELECT ParallelJoinId FROM fn_workflow_return_round WHERE Id=@round", new { round }));
        Assert.AreEqual(fork, await db.ExecuteScalarAsync<Guid>("SELECT SourceParallelJoinId FROM fn_workflow_return_round WHERE Id=@round", new { round }));
        using (var input = JsonDocument.Parse((await db.ExecuteScalarAsync<string>("SELECT InputJson FROM fn_workflow_activity_work WHERE ReturnRoundId=@round", new { round }))!))
            Assert.AreEqual(whole ? "outer-update" : "original", input.RootElement.GetProperty("untouched").GetString());
        using (var output = JsonDocument.Parse((await db.ExecuteScalarAsync<string>("SELECT OutputJson FROM fn_workflow_todo_view WHERE Id=@source", new { source }))!))
            Assert.AreEqual("original", output.RootElement.GetProperty("untouched").GetString());
        Assert.AreEqual(runtime.GetProperty("submissionRevision").GetInt64() + 1,
            await db.ExecuteScalarAsync<long>("SELECT OutputRevision FROM fn_workflow_todo_view WHERE Id=@source", new { source }));
        if (whole) Assert.AreEqual(await db.ExecuteScalarAsync<long>("SELECT Revision FROM fn_workflow_form_submission WHERE InstanceId=@root", new { root }),
            await db.ExecuteScalarAsync<long>("SELECT InputFormRevision FROM fn_workflow_return_round WHERE Id=@round", new { round }));

        await DrainAsync(factory);
        if (whole)
        {
            await ActAsync(client, await NodeTodoAsync(db, root, "before"), "approve", "renewed-before");
            var renewedFork = await db.ExecuteScalarAsync<Guid>("SELECT Id FROM fn_workflow_parallel_join WHERE InstanceId=@root AND StatusKey='waiting'", new { root });
            Assert.AreNotEqual(fork, renewedFork);
            Assert.AreEqual(round, await db.ExecuteScalarAsync<Guid>("SELECT ExecutionEpochId FROM fn_workflow_parallel_join WHERE Id=@renewedFork", new { renewedFork }));
        }
        else Assert.AreEqual(oldRight, await NodeTodoAsync(db, root, "right"));
        await ActAsync(client, await NodeTodoAsync(db, root, "leftTarget"), "approve", "renewed-left-target");
        await ActAsync(client, await NodeTodoAsync(db, root, "leftSource"), "approve", "renewed-left-source");
        await DrainAsync(factory);
        await ActAsync(client, await NodeTodoAsync(db, root, "right"), "approve", "finish-right");
        await DrainAsync(factory);
        Assert.AreEqual(2L, await db.ExecuteScalarAsync<long>("SELECT Revision FROM fn_workflow_return_round WHERE Id=@round AND ExitWorkId IS NOT NULL", new { round }));
        Assert.AreEqual("closed", await db.ExecuteScalarAsync<string>("SELECT StatusKey FROM fn_workflow_execution_epoch WHERE Id=@round", new { round }));
        await ActAsync(client, await NodeTodoAsync(db, root, "after"), "approve", "finish-after");
        await DrainAsync(factory);
        Assert.AreEqual("completed", await db.ExecuteScalarAsync<string>("SELECT StatusKey FROM fn_workflow_instance WHERE Id=@root", new { root }));
        Assert.AreEqual(0, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_todo WHERE InstanceId=@root AND StatusKey IN ('active','suspended','before_countersign')", new { root }));
        Assert.AreEqual(0, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_execution_epoch WHERE InstanceId=@root AND StatusKey='active'", new { root }));
        Assert.AreEqual(whole ? 0 : 2, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_parallel_branch_arrival WHERE ParallelJoinId=@fork", new { fork }));
        using (var submission = JsonDocument.Parse((await db.ExecuteScalarAsync<string>("SELECT SubmissionJson FROM fn_workflow_form_submission WHERE InstanceId=@root", new { root }))!))
        {
            Assert.AreEqual(whole ? "whole-output" : "local-output", submission.RootElement.GetProperty("title").GetString());
            Assert.AreEqual(whole ? "outer-update" : "original", submission.RootElement.GetProperty("untouched").GetString());
        }
        Assert.AreEqual(receipts[0].GetRawText(), (await PostAsync(client, $"/api/v1/workflow/todos/{source:D}/return", request)).GetRawText());
        await WorkflowCausalityAssertions.CompleteRootAsync(db, root);
    }
    /// <summary>整体重开同时关闭实际人工/定时事实，旧席位与票不能进入新fork，新尝试从零开始。</summary>
    private static async Task WaitingScopeCaseAsync(FullNetApiFactory factory, DbConnection db, HttpClient client, Guid actor, Guid form, Guid partner, string kind)
    {
        var assignees = new { sources = new[] { new { resolverKindKey = "specified_users", userIds = new[] { actor } } } };
        var right = kind switch
        {
            "group" => Human("right", "join", [actor, partner], "group"),
            "task" => Human("right", "join", [actor], "task"),
            "vote" => Node("right", "human.vote", new { nextNodeKeys = new[] { "join" }, assigneePolicy = assignees,
                votePolicy = new { ballotFormVersionId = form, participationPercentage = 100, approvalPercentage = 100, allowAbstain = true } }),
            "claim" => Node("right", "human.claim", new { nextNodeKeys = new[] { "join" }, assigneePolicy = assignees, claimPolicy = new { allowRelease = true } }),
            _ => Node("right", "timer.delay", new { nextNodeKeys = new[] { "join" }, delayPolicy = new { modeKey = "relative", timeZoneKey = "UTC", duration = 24, unitKey = "hour" } })
        };
        var version = await PublishAsync(client, form, [Node("start", "start", new { nextNodeKeys = new[] { "before" } }), Human("before", "fork", [actor], "all"),
            Node("fork", "gateway.parallel", new { gatewayRoleKey = "fork", joinNodeKey = "join", branches = new[] {
                new { branchKey = "left", nextNodeKey = "source" }, new { branchKey = "right", nextNodeKey = "right" } } }),
            Node("source", "human.approval", new { nextNodeKeys = new[] { "join" }, assigneePolicy = assignees,
                slaPolicy = new { dueAfterMinutes = 60, pauseModeKey = "freeze", actionKey = "suspend" } }), right,
            Node("join", "gateway.parallel", new { gatewayRoleKey = "join", forkNodeKey = "fork", nextNodeKeys = new[] { "end" } }), Node("end", "end", new { })]);
        var root = (await PostAsync(client, "/api/v1/workflow/instances", new { definitionVersionId = version, businessType = "return-waits",
            businessId = Guid.NewGuid().ToString("N"), initialValues = new { title = "initial" }, idempotencyKey = Guid.NewGuid().ToString("N") }, HttpStatusCode.Created)).GetProperty("id").GetGuid();
        var before = await StepAsync(db, root, "before"); await ActAsync(client, await NodeTodoAsync(db, root, "before"), "approve", "before");
        var oldRight = await StepAsync(db, root, "right"); var oldSource = await StepAsync(db, root, "source"); var todo = await NodeTodoAsync(db, root, "source");
        var runtime = await client.GetFromJsonAsync<JsonElement>($"/api/v1/workflow/todos/{todo:D}/runtime");
        await PostAsync(client, $"/api/v1/workflow/todos/{todo:D}/return", new { targetStepId = before, expectedRevision = runtime.GetProperty("revision").GetInt64(),
            expectedFormRevision = runtime.GetProperty("submissionRevision").GetInt64(), fieldPatch = new { }, comment = "关闭真实等待事实后整体重开", idempotencyKey = "return-wait" });
        Assert.AreEqual("cancelled", await db.ExecuteScalarAsync<string>("SELECT StatusKey FROM fn_workflow_sla WHERE Id=@oldSource", new { oldSource }));
        Assert.AreEqual("cancelled", await db.ExecuteScalarAsync<string>("SELECT StatusKey FROM fn_workflow_step WHERE Id=@oldRight", new { oldRight }));
        // 表名来自本测试固定分支，参数仍使用真实步骤身份，不接受请求中的表名。
        var facts = kind switch { "group" => ("fn_workflow_approval_group", "StepId"), "task" => ("fn_workflow_task_member", "TaskId"),
            "vote" => ("fn_workflow_vote_ballot", "VoteId"), "claim" => ("fn_workflow_claim", "Id"), _ => ("fn_workflow_delay", "Id") };
        Assert.AreEqual(kind == "group" ? 2 : 1, await db.ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM {facts.Item1} WHERE {facts.Item2}=@oldRight AND StatusKey='cancelled'", new { oldRight }));
        await DrainAsync(factory); await ActAsync(client, await NodeTodoAsync(db, root, "before"), "approve", "new-before");
        var renewed = await StepAsync(db, root, "right"); Assert.AreNotEqual(oldRight, renewed);
        var initialState = kind switch { "group" => "active", "claim" => "available", "delay" => "waiting", _ => "pending" };
        Assert.AreEqual(kind == "group" ? 2 : 1, await db.ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM {facts.Item1} WHERE {facts.Item2}=@renewed AND StatusKey=@initialState", new { renewed, initialState }));
        if (kind == "vote") Assert.AreEqual(1, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_vote_ballot WHERE VoteId=@renewed AND ChoiceKey IS NULL AND Revision=1", new { renewed }));
        if (kind == "group") Assert.AreEqual(2, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_approval_slot WHERE StepId=@renewed AND DecisionKey IS NULL AND Revision=1", new { renewed }));
        Assert.AreEqual("cancelled", await db.ExecuteScalarAsync<string>("SELECT StatusKey FROM fn_workflow_step WHERE Id=@oldRight", new { oldRight }));
        await PostAsync(client, $"/api/v1/workflow/instances/{root:D}/cancel", new { expectedRevision = await db.ExecuteScalarAsync<long>("SELECT Revision FROM fn_workflow_instance WHERE Id=@root", new { root }),
            reason = "验收后关闭新等待", idempotencyKey = "close-renewed" });
    }

    private static async Task<Guid> ParallelFormAsync(HttpClient client)
    {
        var fields = new[] { "title", "untouched" }.Select(key => new { fieldKey = key, fieldTypeKey = "text", required = false, constraints = new { } }).ToArray();
        var form = await PostAsync(client, "/api/v1/workflow/forms", new { formKey = "parallel.return." + Guid.NewGuid().ToString("N"),
            draft = new { schemaVersion = 1, adapterVersion = 1, sections = new[] { new { sectionKey = "main", fields } } } }, HttpStatusCode.Created);
        return (await PostAsync(client, $"/api/v1/workflow/forms/{form.GetProperty("id").GetGuid():D}/publish", new { expectedRevision = 1 })).GetProperty("id").GetGuid();
    }
    private static Task<Guid> NodeTodoAsync(DbConnection db, Guid root, string node) => db.ExecuteScalarAsync<Guid>(
        "SELECT todo.Id FROM fn_workflow_todo AS todo INNER JOIN fn_workflow_step AS step ON step.Id=todo.StepId WHERE todo.InstanceId=@root AND step.NodeKey=@node AND todo.StatusKey='active'", new { root, node });
    private static async Task DrainAsync(FullNetApiFactory factory)
    {
        for (var index = 0; index < 8; index++)
        {
            await using var scope = factory.Services.CreateAsyncScope();
            if (await scope.ServiceProvider.GetRequiredService<WorkflowActivityWorkProcessor>().ProcessPendingAsync(default) == 0) return;
        }
        Assert.Fail("退回工作未能在有界次数内收敛。");
    }
}
