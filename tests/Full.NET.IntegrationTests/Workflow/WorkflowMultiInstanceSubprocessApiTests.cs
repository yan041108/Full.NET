using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Dapper;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.IntegrationTests.Migrations;
using Full.NET.Modules.Workflow.Contracts;
using Full.NET.Modules.Workflow.Execution;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using MySqlConnector;
using static Full.NET.IntegrationTests.Workflow.WorkflowDurableDelayApiTests;

namespace Full.NET.IntegrationTests.Workflow;

/// <summary>实际办理子流程验证固定分母、串并行、封口取消和来源恢复；两种提供程序执行同一场景。</summary>
[TestClass]
public sealed class WorkflowMultiInstanceSubprocessApiTests
{
    [TestMethod]
    public async Task Multi_instance_subprocess_with_sql_server() => await VerifyAsync(DatabaseProvider.SqlServer, await SharedDatabaseFixture.CreateSqlServerDatabaseAsync());
    [TestMethod]
    public async Task Multi_instance_subprocess_with_mysql() => await VerifyAsync(DatabaseProvider.MySql, await SharedDatabaseFixture.CreateMySqlDatabaseAsync());

    private static async Task VerifyAsync(DatabaseProvider provider, string connection)
    {
        using var factory = new FullNetApiFactory(provider, connection); await factory.InitializeAsync(useSchemaTemplate: false);
        var actor = await factory.CreateHostIdentityAsync("multi." + Guid.NewGuid().ToString("N"), [WorkflowPermissions.FormsCreate, WorkflowPermissions.FormsPublish,
            WorkflowPermissions.DefinitionsCreate, WorkflowPermissions.DefinitionsPublish, WorkflowPermissions.DefinitionsRead, WorkflowPermissions.InstancesStart,
            WorkflowPermissions.InstancesRead, WorkflowPermissions.InstancesPause, WorkflowPermissions.InstancesResume, WorkflowPermissions.InstancesCancel,
            WorkflowPermissions.TodosRead, WorkflowPermissions.TodosApprove, WorkflowPermissions.TodosReject]);
        using var client = factory.CreateClientForHost("localhost"); client.DefaultRequestHeaders.Authorization = new("Bearer", actor.AccessToken);
        await using DbConnection db = provider == DatabaseProvider.SqlServer ? new SqlConnection(connection) : new MySqlConnection(Migration114To126RecoveryTestSupport.MySqlQueryConnectionString(connection));
        object Node(string key, string kind, object config) => new { nodeKey = key, nodeTypeKey = kind, nodeSchemaVersion = 1, config };
        object Field(string key, string type, object? constraints = null) => new { fieldKey = key, fieldTypeKey = type, required = false, constraints = constraints ?? new { } };
        async Task<Guid> Form(object[] fields)
        {
            var draft = await PostAsync(client, "/api/v1/workflow/forms", new { formKey = "multi." + Guid.NewGuid().ToString("N"),
                draft = new { schemaVersion = 1, adapterVersion = 1, sections = new[] { new { sectionKey = "main", fields } } } }, HttpStatusCode.Created);
            return (await PostAsync(client, $"/api/v1/workflow/forms/{draft.GetProperty("id").GetGuid():D}/publish", new { expectedRevision = 1 })).GetProperty("id").GetGuid();
        }
        var childForm = await Form([Field("subject", "text"), Field("member", "text"), Field("score", "integer")]);
        var childVersion = await PublishNodesAsync(client, childForm, [Node("start", "start", new { nextNodeKeys = new[] { "approve" } }),
            Node("approve", "human.approval", new { nextNodeKeys = new[] { "end" }, fieldPolicies = new { subject = "readOnly", member = "readOnly", score = "editable" },
                assigneePolicy = new { sources = new[] { new { resolverKindKey = "specified_users", userIds = new[] { actor.UserId } } } } }), Node("end", "end", new { })]);
        var parentForm = await Form([Field("title", "text"), Field("result", "integer"), Field("count", "integer"), Field("members", "checkbox", new { options = new[] { "b", "a", "c" } })]);
        async Task<Guid> Publish(object source, string mode = "parallel", int percentage = 100, string empty = "reject") => await PublishNodesAsync(client, parentForm,
            [Node("start", "start", new { nextNodeKeys = new[] { "children" } }), Node("children", "process.subprocess", new {
                nextNodeKeys = new[] { "end" }, fieldPolicies = new { title = "readOnly", result = "editable", count = "readOnly", members = "readOnly" },
                subprocessPolicy = new { definitionVersionId = childVersion, inputFields = new { subject = "title" }, outputFields = new { result = "score" } },
                multiInstancePolicy = new { modeKey = mode, source, requiredPercentage = percentage, emptyModeKey = empty, outputReducers = new { result = "sum" } } }), Node("end", "end", new { })]);
        async Task<Guid> Start(Guid version, int count = 2, string[]? members = null) => (await PostAsync(client, "/api/v1/workflow/instances", new {
            definitionVersionId = version, businessType = "multi.test", businessId = Guid.NewGuid().ToString("N"),
            initialValues = new { title = "frozen", result = 0, count, members = members ?? new[] { "b", "a" } }, idempotencyKey = Guid.NewGuid().ToString("N") }, HttpStatusCode.Created)).GetProperty("id").GetGuid();
        async Task Tick(bool work = false)
        {
            await using var scope = factory.Services.CreateAsyncScope();
            if (work) await scope.ServiceProvider.GetRequiredService<WorkflowActivityWorkProcessor>().ProcessPendingAsync(default);
            else await scope.ServiceProvider.GetRequiredService<WorkflowSubprocessProcessor>().ProcessPendingAsync(default);
        }
        async Task Until(Func<Task<bool>> condition, string failure)
        {
            for (var i = 0; i < 72; i++) { if (await condition()) return; await Tick(); await Tick(true); }
            Assert.Fail(failure);
        }
        Task<int> Linked(Guid id) => db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_subprocess_item WHERE InstanceId=@id AND ChildInstanceId IS NOT NULL", new { id });
        async Task<Guid> Child(Guid id, int sequence) => await db.ExecuteScalarAsync<Guid>("SELECT ChildInstanceId FROM fn_workflow_subprocess_item WHERE InstanceId=@id AND SequenceNo=@sequence", new { id, sequence });
        async Task<string?> RootStatus(Guid id) => await db.ExecuteScalarAsync<string>("SELECT StatusKey FROM fn_workflow_instance WHERE Id=@id", new { id });
        async Task Action(Guid id, string action) => _ = await PostAsync(client, $"/api/v1/workflow/instances/{id:D}/{action}", new {
            expectedRevision = await db.ExecuteScalarAsync<long>("SELECT Revision FROM fn_workflow_instance WHERE Id=@id", new { id }),
            reason = "多实例真实验收", idempotencyKey = Guid.NewGuid().ToString("N") });
        async Task Complete(Guid child, int score, bool reject = false)
        {
            var todo = await db.ExecuteScalarAsync<Guid>("SELECT Id FROM fn_workflow_todo WHERE InstanceId=@child AND StatusKey='active'", new { child });
            var detail = await client.GetFromJsonAsync<JsonElement>($"/api/v1/workflow/todos/{todo:D}");
            await PostAsync(client, $"/api/v1/workflow/todos/{todo:D}/{(reject ? "reject" : "approve")}", new {
                expectedRevision = detail.GetProperty("revision").GetInt64(), expectedFormRevision = detail.GetProperty("submissionRevision").GetInt64(),
                fieldPatch = new { score }, comment = "实际处理", idempotencyKey = Guid.NewGuid().ToString("N") });
        }
        async Task<int> Result(Guid id) => JsonSerializer.Deserialize<JsonElement>((await db.ExecuteScalarAsync<string>("SELECT SubmissionJson FROM fn_workflow_form_submission WHERE InstanceId=@id", new { id }))!).GetProperty("result").GetInt32();

        var serial = await Start(await Publish(new { kindKey = "fixed_count", count = 2 }, "serial"));
        await Task.WhenAll(Tick(), Tick());
        Assert.AreEqual(2, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_subprocess_item WHERE InstanceId=@id", new { id = serial }));
        await Until(async () => await Linked(serial) == 1, "串行第一项未启动");
        for (var i = 0; i < 3; i++) await Tick(); Assert.AreEqual(1, await Linked(serial));
        await Complete(await Child(serial, 1), 3); await Until(async () => await Linked(serial) == 2, "串行第二项未启动");
        await Complete(await Child(serial, 2), 7); await Until(async () => await RootStatus(serial) == "completed", "串行未归约完成");
        Assert.AreEqual(10, await Result(serial));
        Assert.AreEqual(2, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_subprocess_choice WHERE InstanceId=@id", new { id = serial }));
        Assert.IsTrue(await db.ExecuteScalarAsync<int>("SELECT Reserved FROM fn_workflow_exec_budget WHERE InstanceId=@id", new { id = serial }) < 4096);

        // 三个真实子根都已运行；首个成功提交时封口，随后成功不能进入原输出。
        var parallel = await Start(await Publish(new { kindKey = "number_field", fieldKey = "count", maximumCount = 3 }, percentage: 33), count: 3);
        await Until(async () => await Linked(parallel) == 3, "并行子项未启动");
        var first = await Child(parallel, 1); var late = await Child(parallel, 2); var remainder = await Child(parallel, 3);
        await Complete(first, 11); await Tick();
        Assert.AreEqual("closing", await db.ExecuteScalarAsync<string>("SELECT StatusKey FROM fn_workflow_subprocess_set WHERE InstanceId=@id", new { id = parallel }));
        var selectedHash = await db.ExecuteScalarAsync<string>("SELECT SelectionHash FROM fn_workflow_subprocess_set WHERE InstanceId=@id", new { id = parallel });
        await Complete(late, 99); await Action(parallel, "pause");
        await Until(async () => await db.ExecuteScalarAsync<string>("SELECT StatusKey FROM fn_workflow_subprocess_set WHERE InstanceId=@id", new { id = parallel }) == "result_ready", "暂停时结果未停车");
        Assert.AreEqual("cancelled", await RootStatus(remainder)); Assert.AreEqual("suspended", await RootStatus(parallel)); Assert.AreEqual(0, await Result(parallel));
        Assert.AreEqual(1, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_subprocess_choice WHERE InstanceId=@id", new { id = parallel }));
        Assert.AreEqual(selectedHash, await db.ExecuteScalarAsync<string>("SELECT SelectionHash FROM fn_workflow_subprocess_set WHERE InstanceId=@id", new { id = parallel }));
        await Action(parallel, "resume"); await Until(async () => await RootStatus(parallel) == "completed", "并行结果未回写"); Assert.AreEqual(11, await Result(parallel));
        Assert.IsNull(await db.ExecuteScalarAsync<Guid?>("SELECT CancelledById FROM fn_workflow_instance WHERE Id=@id", new { id = remainder }));

        var collection = await Start(await Publish(new { kindKey = "collection_field", fieldKey = "members", itemFieldKey = "member", maximumCount = 3 }, "serial"));
        await Until(async () => await Linked(collection) == 1, "集合首项未启动");
        var itemInput = JsonSerializer.Deserialize<JsonElement>((await db.ExecuteScalarAsync<string>("SELECT InputJson FROM fn_workflow_subprocess_item WHERE InstanceId=@id AND SequenceNo=1", new { id = collection }))!);
        Assert.AreEqual("b", itemInput.GetProperty("member").GetString());
        await Complete(await Child(collection, 1), 2); await Until(async () => await Linked(collection) == 2, "集合第二项未启动");
        await Complete(await Child(collection, 2), 4); await Until(async () => await RootStatus(collection) == "completed", "集合未完成"); Assert.AreEqual(6, await Result(collection));

        var empty = await Start(await Publish(new { kindKey = "number_field", fieldKey = "count", maximumCount = 2 }, empty: "skip"), count: 0);
        await Until(async () => await RootStatus(empty) == "completed", "显式空集合跳过未完成"); Assert.AreEqual(0, await Linked(empty)); Assert.AreEqual(0, await Result(empty));
        var invalid = await Start(await Publish(new { kindKey = "number_field", fieldKey = "count", maximumCount = 2 }), count: 3);
        await Until(async () => await RootStatus(invalid) == "suspended", "超限来源没有失败关闭"); Assert.AreEqual(0, await Linked(invalid)); await Action(invalid, "cancel");

        var rejected = await Start(await Publish(new { kindKey = "fixed_count", count = 2 }));
        await Until(async () => await Linked(rejected) == 2, "拒绝场景未启动"); await Complete(await Child(rejected, 1), 0, reject: true);
        await Until(async () => await RootStatus(rejected) == "suspended" && await RootStatus(await Child(rejected, 2)) == "cancelled", "不可能达标时未停止剩余子根");
        Assert.AreEqual(0, await Result(rejected)); await Action(rejected, "cancel");
        Assert.AreEqual(1, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_domain_audit WHERE InstanceId=@id AND OperationKey='activity.execute' AND OutcomeKey='failed'", new { id = rejected }));

        var before = await Start(await Publish(new { kindKey = "fixed_count", count = 2 })); await Action(before, "cancel");
        await Until(async () => await db.ExecuteScalarAsync<string>("SELECT StatusKey FROM fn_workflow_subprocess_call WHERE InstanceId=@id", new { id = before }) == "cancelled", "准备阶段取消未收敛"); Assert.AreEqual(0, await Linked(before));
        var during = await Start(await Publish(new { kindKey = "fixed_count", count = 2 })); await Until(async () => await Linked(during) == 2, "取消场景未启动"); await Action(during, "cancel");
        await Until(async () => await db.ExecuteScalarAsync<string>("SELECT StatusKey FROM fn_workflow_subprocess_call WHERE InstanceId=@id", new { id = during }) == "cancelled", "父取消未收敛");
        Assert.AreEqual("cancelled", await RootStatus(await Child(during, 1))); Assert.AreEqual("cancelled", await RootStatus(await Child(during, 2)));

        // 使用真实Identity自有角色目录；固化后删除成员关系，恢复仍使用原分母与输入。
        var role = Guid.CreateVersion7(); var sourceRole = await db.ExecuteScalarAsync<Guid>("SELECT RoleId FROM fn_identity_user_role WHERE UserId=@user", new { user = actor.UserId });
        await db.ExecuteAsync("""
            INSERT INTO fn_identity_role (Id,TenantId,ScopeKey,Code,Name,IsSystem,IsActive,IsSuperAdministrator,DataScopeKind,CreatedAtUtc,Version)
            SELECT @role,TenantId,ScopeKey,@code,Name,IsSystem,IsActive,IsSuperAdministrator,DataScopeKind,CreatedAtUtc,Version FROM fn_identity_role WHERE Id=@sourceRole
            """, new { role, sourceRole, code = "multi-" + role.ToString("N") });
        await db.ExecuteAsync("INSERT INTO fn_identity_user_role (UserId,RoleId) VALUES (@user,@role)", new { user = actor.UserId, role });
        var roles = await Start(await Publish(new { kindKey = "role_members", roleId = role, itemFieldKey = "member", maximumCount = 2 }));
        await Until(async () => await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_subprocess_item WHERE InstanceId=@id", new { id = roles }) == 1, "角色来源未固化");
        await db.ExecuteAsync("DELETE FROM fn_identity_user_role WHERE UserId=@user AND RoleId=@role", new { user = actor.UserId, role });
        await Until(async () => await Linked(roles) == 1, "固化角色项未启动"); await Complete(await Child(roles, 1), 5);
        await Until(async () => await RootStatus(roles) == "completed", "角色项未完成"); Assert.AreEqual(5, await Result(roles));

        var sets = await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_subprocess_set");
        var choices = await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_subprocess_choice");
        await db.ExecuteAsync(provider == DatabaseProvider.SqlServer ? "ALTER TABLE fn_workflow_subprocess_set DROP CONSTRAINT ck_fn_workflow_subprocess_set_count" : "ALTER TABLE fn_workflow_subprocess_set DROP CHECK ck_fn_workflow_subprocess_set_count");
        await db.ExecuteAsync(provider == DatabaseProvider.SqlServer ? "DELETE FROM dbo.SchemaVersions WHERE ScriptName LIKE '%321_WorkflowMultiInstanceSets.sql'" : "DELETE FROM schemaversions WHERE ScriptName LIKE '%321_WorkflowMultiInstanceSets.sql'");
        await WorkflowHumanMigrationRecoveryTests.MigrateAsync(provider, connection); await WorkflowHumanMigrationRecoveryTests.MigrateAsync(provider, connection);
        Assert.AreEqual(sets, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_subprocess_set")); Assert.AreEqual(choices, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_subprocess_choice"));
        if (provider == DatabaseProvider.MySql) Assert.AreEqual(0, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM information_schema.ROUTINES WHERE ROUTINE_SCHEMA=DATABASE() AND ROUTINE_NAME IN ('fn_workflow_subprocess_set_ensure','fn_workflow_subprocess_choice_ensure')"));
        await Assert.ThrowsAsync<DbException>(async () => await db.ExecuteAsync("UPDATE fn_workflow_subprocess_set SET ItemCount=33 WHERE InstanceId=@id", new { id = serial }));
    }
}
