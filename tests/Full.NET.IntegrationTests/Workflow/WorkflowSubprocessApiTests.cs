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
using Microsoft.Extensions.DependencyInjection.Extensions;
using MySqlConnector;
using static Full.NET.IntegrationTests.Workflow.WorkflowDurableDelayApiTests;

namespace Full.NET.IntegrationTests.Workflow;

/// <summary>真实父子根验证关联原子性、暂停结果停车和系统取消，不用伪造子终态替代实际办理。</summary>
[TestClass]
public sealed class WorkflowSubprocessApiTests
{
    [TestMethod]
    public async Task Subprocess_with_sql_server() => await VerifyAsync(DatabaseProvider.SqlServer, await SharedDatabaseFixture.CreateSqlServerDatabaseAsync());
    [TestMethod]
    public async Task Subprocess_with_mysql() => await VerifyAsync(DatabaseProvider.MySql, await SharedDatabaseFixture.CreateMySqlDatabaseAsync());

    private static async Task VerifyAsync(DatabaseProvider provider, string connection)
    {
        var linkFault = new LinkFault();
        using var factory = new FullNetApiFactory(provider, connection, configureTestServices: services => {
            var descriptor = services.Last(item => item.ServiceType == typeof(ICommandExecutor));
            var create = descriptor.ImplementationFactory ?? throw new InvalidOperationException("Expected the command executor factory.");
            services.Replace(ServiceDescriptor.Scoped<ICommandExecutor>(scope => new LinkFaultExecutor((ICommandExecutor)create(scope), linkFault)));
        });
        await factory.InitializeAsync(useSchemaTemplate: false);
        var actor = await factory.CreateHostIdentityAsync("subprocess." + Guid.NewGuid().ToString("N"),
            [WorkflowPermissions.FormsCreate, WorkflowPermissions.FormsPublish, WorkflowPermissions.DefinitionsCreate, WorkflowPermissions.DefinitionsRead, WorkflowPermissions.DefinitionsUpdate,
                WorkflowPermissions.DefinitionsPublish, WorkflowPermissions.InstancesStart, WorkflowPermissions.InstancesRead,
                WorkflowPermissions.InstancesPause, WorkflowPermissions.InstancesResume, WorkflowPermissions.InstancesCancel,
                WorkflowPermissions.TodosRead, WorkflowPermissions.TodosApprove]);
        using var client = factory.CreateClientForHost("localhost"); client.DefaultRequestHeaders.Authorization = new("Bearer", actor.AccessToken);
        await using DbConnection db = provider == DatabaseProvider.SqlServer ? new SqlConnection(connection)
            : new MySqlConnection(Migration114To126RecoveryTestSupport.MySqlQueryConnectionString(connection));
        async Task<Guid> Form(string[] fields)
        {
            var form = await PostAsync(client, "/api/v1/workflow/forms", new { formKey = "subprocess." + Guid.NewGuid().ToString("N"),
                draft = new { schemaVersion = 1, adapterVersion = 1, sections = new[] { new { sectionKey = "main", fields = fields.Select(key =>
                    new { fieldKey = key, fieldTypeKey = "text", required = false, constraints = new { } }).ToArray() } } } }, HttpStatusCode.Created);
            return (await PostAsync(client, $"/api/v1/workflow/forms/{form.GetProperty("id").GetGuid():D}/publish", new { expectedRevision = 1 })).GetProperty("id").GetGuid();
        }
        object Node(string key, string kind, object config) => new { nodeKey = key, nodeTypeKey = kind, nodeSchemaVersion = 1, config };
        var childForm = await Form(["subject", "answer"]);
        var childVersion = await PublishNodesAsync(client, childForm, [Node("start", "start", new { nextNodeKeys = new[] { "review" } }),
            Node("review", "human.approval", new { nextNodeKeys = new[] { "end" }, fieldPolicies = new { subject = "readOnly", answer = "editable" },
                assigneePolicy = new { sources = new[] { new { resolverKindKey = "specified_users", userIds = new[] { actor.UserId } } } } }), Node("end", "end", new { })]);
        var parentForm = await Form(["title", "result", "unrelated"]);
        var parentVersion = await PublishNodesAsync(client, parentForm, [Node("start", "start", new { nextNodeKeys = new[] { "child" } }),
            Node("child", "process.subprocess", new { nextNodeKeys = new[] { "end" }, fieldPolicies = new { title = "readOnly", result = "editable" },
                subprocessPolicy = new { definitionVersionId = childVersion, inputFields = new { subject = "title" }, outputFields = new { result = "answer" } } }), Node("end", "end", new { })]);
        object ReferenceDraft(Guid target) => new { schemaVersion = 1, workflowKindKey = "automation", nodes = new[] {
            Node("start", "start", new { nextNodeKeys = new[] { "child" } }), Node("child", "process.subprocess", new {
                nextNodeKeys = new[] { "end" }, fieldPolicies = new { title = "readOnly", result = "editable" },
                subprocessPolicy = new { definitionVersionId = target, inputFields = new { title = "title" }, outputFields = new { result = "result" } } }), Node("end", "end", new { }) } };
        var own = await PostAsync(client, "/api/v1/workflow/definitions", new { definitionKey = "self." + Guid.NewGuid().ToString("N"),
            draft = new { schemaVersion = 1, workflowKindKey = "automation", nodes = new[] { Node("start", "start", new { nextNodeKeys = new[] { "end" } }), Node("end", "end", new { }) } } }, HttpStatusCode.Created);
        var ownId = own.GetProperty("id").GetGuid();
        var ownVersion = (await PostAsync(client, $"/api/v1/workflow/definitions/{ownId:D}/publish", new { expectedRevision = 1, formVersionId = parentForm })).GetProperty("id").GetGuid();
        async Task UpdateReference(Guid target)
        {
            var current = await client.GetFromJsonAsync<JsonElement>($"/api/v1/workflow/definitions/{ownId:D}");
            var revision = current.GetProperty("draftRevision").GetInt64();
            using var response = await client.PutAsJsonAsync($"/api/v1/workflow/definitions/{ownId:D}/draft", new { expectedRevision = revision, draft = ReferenceDraft(target) });
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync());
            await PostAsync(client, $"/api/v1/workflow/definitions/{ownId:D}/publish", new { expectedRevision = revision + 1, formVersionId = parentForm }, HttpStatusCode.BadRequest);
        }
        await UpdateReference(ownVersion);
        var intermediary = await PostAsync(client, "/api/v1/workflow/definitions", new { definitionKey = "indirect." + Guid.NewGuid().ToString("N"), draft = ReferenceDraft(ownVersion) }, HttpStatusCode.Created);
        var intermediaryVersion = (await PostAsync(client, $"/api/v1/workflow/definitions/{intermediary.GetProperty("id").GetGuid():D}/publish", new { expectedRevision = 1, formVersionId = parentForm })).GetProperty("id").GetGuid();
        await UpdateReference(intermediaryVersion);
        Assert.AreEqual(1, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_definition_version WHERE DefinitionId=@id", new { id = ownId }));
        object Request(Guid version, string type = "subprocess.test") => new { definitionVersionId = version, businessType = type,
            businessId = Guid.NewGuid().ToString("N"), initialValues = new { title = "source", result = "before", unrelated = "keep" }, idempotencyKey = Guid.NewGuid().ToString("N") };
        async Task<Guid> Start() => (await PostAsync(client, "/api/v1/workflow/instances", Request(parentVersion), HttpStatusCode.Created)).GetProperty("id").GetGuid();
        async Task<int> Process(bool work = false)
        {
            await using var scope = factory.Services.CreateAsyncScope();
            return work ? await scope.ServiceProvider.GetRequiredService<WorkflowActivityWorkProcessor>().ProcessPendingAsync(default)
                : await scope.ServiceProvider.GetRequiredService<WorkflowSubprocessProcessor>().ProcessPendingAsync(default);
        }
        async Task Action(Guid id, string action) => _ = await PostAsync(client, $"/api/v1/workflow/instances/{id:D}/{action}", new {
            expectedRevision = await db.ExecuteScalarAsync<long>("SELECT Revision FROM fn_workflow_instance WHERE Id=@id", new { id }),
            reason = "父子执行验收", idempotencyKey = Guid.NewGuid().ToString("N") });
        async Task<Guid> Child(Guid id) => await db.ExecuteScalarAsync<Guid>("SELECT ChildInstanceId FROM fn_workflow_subprocess_item WHERE InstanceId=@id", new { id });
        async Task Approve(Guid id)
        {
            var todo = await db.ExecuteScalarAsync<Guid>("SELECT Id FROM fn_workflow_todo WHERE InstanceId=@id AND StatusKey='active'", new { id });
            var detail = await client.GetFromJsonAsync<JsonElement>($"/api/v1/workflow/todos/{todo:D}");
            await PostAsync(client, $"/api/v1/workflow/todos/{todo:D}/approve", new { expectedRevision = detail.GetProperty("revision").GetInt64(),
                expectedFormRevision = detail.GetProperty("submissionRevision").GetInt64(), fieldPatch = new { answer = "from child" }, idempotencyKey = Guid.NewGuid().ToString("N") });
        }

        using (var spoof = await client.PostAsJsonAsync("/api/v1/workflow/instances", Request(parentVersion, "workflow.child")))
            Assert.AreEqual(HttpStatusCode.Forbidden, spoof.StatusCode, await spoof.Content.ReadAsStringAsync());
        var success = await Start();
        Assert.AreEqual(0, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_todo WHERE InstanceId=@id", new { id = success }));
        await Action(success, "pause"); Assert.AreEqual(0, await Process());
        Assert.AreEqual(0, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_subprocess_item WHERE InstanceId=@id AND ChildInstanceId IS NOT NULL", new { id = success }));
        await Action(success, "resume");
        Assert.AreEqual(1, (await Task.WhenAll(Process(), Process())).Sum());
        var child = await Child(success); Assert.AreNotEqual(Guid.Empty, child);
        Assert.AreEqual(1, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_instance WHERE BusinessType='workflow.child'"));
        Assert.IsTrue(await db.ExecuteScalarAsync<int>("SELECT Granted FROM fn_workflow_exec_budget WHERE InstanceId=@id", new { id = child }) < 4096);
        await Action(success, "pause"); await Approve(child);
        Assert.AreEqual(0, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_outbox_message WHERE MessageType=@type", new { type = WorkflowNotificationIntegrationEventTypes.InstanceCompleted }));
        Assert.AreEqual("completed", await db.ExecuteScalarAsync<string>("SELECT StatusKey FROM fn_workflow_instance WHERE Id=@id", new { id = child }));
        Assert.AreEqual(1, await Process()); Assert.AreEqual(0, await Process()); Assert.AreEqual(0, await Process(work: true));
        Assert.AreEqual("result_ready", await db.ExecuteScalarAsync<string>("SELECT StatusKey FROM fn_workflow_subprocess_call WHERE InstanceId=@id", new { id = success }));
        await Action(success, "resume"); Assert.AreEqual(1, await Process()); Assert.AreEqual(1, await Process(work: true));
        Assert.AreEqual("completed", await db.ExecuteScalarAsync<string>("SELECT StatusKey FROM fn_workflow_instance WHERE Id=@id", new { id = success }));
        Assert.AreEqual(1, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_outbox_message WHERE MessageType=@type", new { type = WorkflowNotificationIntegrationEventTypes.InstanceCompleted }));
        var values = JsonSerializer.Deserialize<JsonElement>(await db.ExecuteScalarAsync<string>("SELECT SubmissionJson FROM fn_workflow_form_submission WHERE InstanceId=@id", new { id = success }) ?? "{}");
        Assert.AreEqual("from child", values.GetProperty("result").GetString()); Assert.AreEqual("keep", values.GetProperty("unrelated").GetString());
        Assert.AreEqual(0, await Process()); Assert.AreEqual(0, await Process(work: true));

        var beforeStart = await Start(); await Action(beforeStart, "cancel");
        for (var attempt = 0; attempt < 3; attempt++) await Process();
        Assert.AreEqual(0, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_subprocess_item WHERE InstanceId=@id AND ChildInstanceId IS NOT NULL", new { id = beforeStart }));
        var afterStart = await Start(); await Process(); var cancelledChild = await Child(afterStart); Assert.AreNotEqual(Guid.Empty, cancelledChild);
        await Action(afterStart, "cancel"); for (var attempt = 0; attempt < 4; attempt++) await Process();
        Assert.AreEqual("cancelled", await db.ExecuteScalarAsync<string>("SELECT StatusKey FROM fn_workflow_instance WHERE Id=@id", new { id = cancelledChild }));
        Assert.IsNull(await db.ExecuteScalarAsync<Guid?>("SELECT CancelledById FROM fn_workflow_instance WHERE Id=@id", new { id = cancelledChild }));
        Assert.AreEqual(1, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_domain_audit WHERE InstanceId=@id AND OperationKey='instance.cancel' AND ActorUserId IS NULL", new { id = cancelledChild }));
        Assert.AreEqual(0, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_action_record WHERE InstanceId=@id AND ActionKey='cancel'", new { id = cancelledChild }));
        Assert.AreEqual("cancelled", await db.ExecuteScalarAsync<string>("SELECT StatusKey FROM fn_workflow_subprocess_call WHERE InstanceId=@id", new { id = afterStart }));
        Assert.AreEqual(0, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_activity_work WHERE InstanceId=@id", new { id = afterStart }));

        // 子根插入后的关联写入失败，必须回滚整次子创建，而非留下无父项的运行根。
        var count = await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_instance WHERE BusinessType='workflow.child'");
        var failedLink = await Start(); linkFault.Arm(); await Process();
        Assert.AreEqual(count, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_instance WHERE BusinessType='workflow.child'"));
        Assert.AreEqual(0, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_subprocess_item WHERE InstanceId=@id AND ChildInstanceId IS NOT NULL", new { id = failedLink }));
        Assert.AreEqual("suspended", await db.ExecuteScalarAsync<string>("SELECT StatusKey FROM fn_workflow_instance WHERE Id=@id", new { id = failedLink }));

        var calls = await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_subprocess_call");
        await db.ExecuteAsync("DROP INDEX ix_fn_workflow_subprocess_call_pending ON fn_workflow_subprocess_call");
        await db.ExecuteAsync(provider == DatabaseProvider.SqlServer
            ? "ALTER TABLE dbo.fn_workflow_subprocess_item DROP CONSTRAINT fk_fn_workflow_subprocess_item_child"
            : "ALTER TABLE fn_workflow_subprocess_item DROP FOREIGN KEY fk_fn_workflow_subprocess_item_child");
        // 模拟317已落旧宽度、319中途失败且未记账，恢复必须保留现有父子事实和索引。
        if (provider == DatabaseProvider.SqlServer)
            await db.ExecuteAsync("DROP INDEX ux_fn_workflow_business_cmd_node ON fn_workflow_business_cmd");
        await db.ExecuteAsync(provider == DatabaseProvider.SqlServer
            ? "ALTER TABLE dbo.fn_workflow_business_cmd ALTER COLUMN NodeKey varchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL"
            : "ALTER TABLE fn_workflow_business_cmd MODIFY COLUMN NodeKey varchar(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '旧节点键宽度'");
        await db.ExecuteAsync(provider == DatabaseProvider.SqlServer ? "DELETE FROM dbo.SchemaVersions WHERE ScriptName LIKE '%319_WorkflowSubprocesses.sql'"
            : "DELETE FROM schemaversions WHERE ScriptName LIKE '%319_WorkflowSubprocesses.sql'");
        await WorkflowHumanMigrationRecoveryTests.MigrateAsync(provider, connection); await WorkflowHumanMigrationRecoveryTests.MigrateAsync(provider, connection);
        Assert.AreEqual(calls, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_subprocess_call"));
        Assert.AreEqual(128, await db.ExecuteScalarAsync<int>(provider == DatabaseProvider.SqlServer
            ? "SELECT max_length FROM sys.columns WHERE object_id=OBJECT_ID('fn_workflow_business_cmd') AND name='NodeKey'"
            : "SELECT CHARACTER_MAXIMUM_LENGTH FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_business_cmd' AND COLUMN_NAME='NodeKey'"));
        Assert.AreEqual(1, await db.ExecuteScalarAsync<int>(provider == DatabaseProvider.SqlServer
            ? "SELECT COUNT(*) FROM sys.indexes WHERE object_id=OBJECT_ID('fn_workflow_subprocess_call') AND name='ix_fn_workflow_subprocess_call_pending'"
            : "SELECT COUNT(*) FROM information_schema.STATISTICS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_subprocess_call' AND INDEX_NAME='ix_fn_workflow_subprocess_call_pending' AND SEQ_IN_INDEX=1"));
    }
    private sealed class LinkFaultExecutor(ICommandExecutor inner, LinkFault fault) : ICommandExecutor
    {
        public Task<int> ExecuteAsync(SqlStatement statement, object? parameters = null, CancellationToken cancellationToken = default) =>
            statement.Name == "workflow.subprocess_item.link_child" && fault.Take() ? Task.FromResult(0) : inner.ExecuteAsync(statement, parameters, cancellationToken);
    }
    private sealed class LinkFault
    {
        private int armed;
        public void Arm() => Interlocked.Exchange(ref armed, 1);
        public bool Take() => Interlocked.Exchange(ref armed, 0) == 1;
    }
}
