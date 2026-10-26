using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Dapper;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.IntegrationTests.Migrations;
using Full.NET.Modules.DataApproval;
using Full.NET.Modules.DataApproval.Contracts;
using Full.NET.Modules.SerialNumbers.Contracts;
using Full.NET.Modules.Workflow.Contracts;
using Full.NET.Modules.Workflow.Execution;
using Full.NET.Modules.Workflow.Execution.BusinessCommands;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MySqlConnector;
using static Full.NET.IntegrationTests.Workflow.WorkflowDurableDelayApiTests;

namespace Full.NET.IntegrationTests.Workflow;

/// <summary>真实审批、所有者写入、MemoryPack回执及根续办共同验证，不以模拟效果替代业务凭据。</summary>
[TestClass]
public sealed class WorkflowBusinessCommandApiTests
{
    [TestMethod]
    public async Task Business_commands_with_sql_server() => await VerifyAsync(DatabaseProvider.SqlServer, await SharedDatabaseFixture.CreateSqlServerDatabaseAsync());
    [TestMethod]
    public async Task Business_commands_with_mysql() => await VerifyAsync(DatabaseProvider.MySql, await SharedDatabaseFixture.CreateMySqlDatabaseAsync());

    private static async Task VerifyAsync(DatabaseProvider provider, string connection)
    {
        var receiptFault = new ReceiptFault();
        using var factory = new FullNetApiFactory(provider, connection, includeEnterpriseRequest: true, configureTestServices: services => {
            var worker = new ServiceCollection(); new DataApprovalModule().AddBackgroundServices(worker, new ConfigurationBuilder().Build());
            foreach (var descriptor in worker.Where(item => item.ServiceType == typeof(IWorkflowBusinessCommandSink) || item.ServiceType == typeof(IWorkflowInstanceCompletedSink)))
                services.Add(descriptor);
            var executor = services.Last(item => item.ServiceType == typeof(ICommandExecutor));
            var createExecutor = executor.ImplementationFactory ?? throw new InvalidOperationException("Expected the registered command factory.");
            services.Replace(ServiceDescriptor.Scoped<ICommandExecutor>(scope => new ReceiptFaultExecutor((ICommandExecutor)createExecutor(scope), receiptFault)));
        });
        await factory.InitializeAsync(useSchemaTemplate: false);
        var actor = await factory.CreateHostIdentityAsync("business.command." + Guid.NewGuid().ToString("N"), [DataApprovalPermissions.Create, DataApprovalPermissions.Read,
            DataApprovalPermissions.ScenariosManage, SerialNumberRulePermissions.Create, SerialNumberRulePermissions.Read, WorkflowPermissions.FormsCreate,
            WorkflowPermissions.FormsPublish, WorkflowPermissions.DefinitionsCreate, WorkflowPermissions.DefinitionsRead, WorkflowPermissions.DefinitionsPublish, WorkflowPermissions.TodosRead,
            WorkflowPermissions.TodosApprove, WorkflowPermissions.InstancesPause, WorkflowPermissions.InstancesResume, WorkflowPermissions.InstancesCancel]);
        using var client = factory.CreateClientForHost("localhost"); client.DefaultRequestHeaders.Authorization = new("Bearer", actor.AccessToken);
        var catalog = await client.GetFromJsonAsync<WorkflowBusinessActionCatalogResponse>("/api/v1/workflow/definitions/business-action-catalog");
        Assert.AreEqual(2, catalog!.Items.Count); Assert.IsTrue(catalog.Items.All(item => item.PlacementKey == "root_finalizer" && item.ActionHash.Length == 64));
        await using DbConnection db = provider == DatabaseProvider.SqlServer ? new SqlConnection(connection) : new MySqlConnection(Migration114To126RecoveryTestSupport.MySqlQueryConnectionString(connection));
        var form = await PostAsync(client, "/api/v1/workflow/forms", new { formKey = "command." + Guid.NewGuid().ToString("N"), draft = new { schemaVersion = 1, adapterVersion = 1,
            sections = new[] { new { sectionKey = "main", fields = new[] { new { fieldKey = "note", fieldTypeKey = "text", required = false, constraints = new { } } } } } } }, HttpStatusCode.Created);
        var formVersion = (await PostAsync(client, $"/api/v1/workflow/forms/{form.GetProperty("id").GetGuid():D}/publish", new { expectedRevision = 1 })).GetProperty("id").GetGuid();
        object Node(string key, string kind, object config) => new { nodeKey = key, nodeTypeKey = kind, nodeSchemaVersion = 1, config };
        var applyNode = "a" + new string('p', 127); var endNode = "e" + new string('n', 127);
        var definition = await PostAsync(client, "/api/v1/workflow/definitions", new { definitionKey = "command." + Guid.NewGuid().ToString("N"), draft = new { schemaVersion = 1,
            workflowKindKey = "approval", nodes = new[] { Node("start", "start", new { nextNodeKeys = new[] { "review" } }),
                Node("review", "human.approval", new { nextNodeKeys = new[] { applyNode }, assigneePolicy = new { sources = new[] { new { resolverKindKey = "specified_users", userIds = new[] { actor.UserId } } } } }),
                Node(applyNode, "business.command", new { nextNodeKeys = new[] { endNode }, businessCommandPolicy = new { actionKey = "data_approval.apply_serial_rule_update", actionVersion = 1 } }), Node(endNode, "end", new { }) } } }, HttpStatusCode.Created);
        var version = (await PostAsync(client, $"/api/v1/workflow/definitions/{definition.GetProperty("id").GetGuid():D}/publish", new { expectedRevision = 1, formVersionId = formVersion })).GetProperty("id").GetGuid();
        using (var binding = await client.PutAsJsonAsync($"/api/v1/data-approvals/scenarios/{DataApprovalScenarioKeys.SerialRuleHostUpdate}", new { isEnabled = true, workflowDefinitionVersionId = version, version = (long?)null }))
            Assert.AreEqual(HttpStatusCode.OK, binding.StatusCode, await binding.Content.ReadAsStringAsync());
        async Task<(Guid RequestId, Guid InstanceId, Guid RuleId)> Start()
        {
            var rule = await PostAsync(client, "/api/v1/serial-numbers/rules", new CreateSerialNumberRuleRequest("cmd." + Guid.NewGuid().ToString("N"), "Before", null,
                SerialNumberRuleScope.Host, SerialNumberResetInterval.Never, "CMD-{sequence:4}", 1, 9999, 0, true), HttpStatusCode.Created);
            var ruleId = rule.GetProperty("id").GetGuid();
            var snapshot = JsonSerializer.Serialize(new UpdateSerialNumberRuleRequest("Applied", null, SerialNumberRuleScope.Host, SerialNumberResetInterval.Never,
                "CMD-{sequence:4}", 1, 9999, 0, true, rule.GetProperty("version").GetInt64()), new JsonSerializerOptions(JsonSerializerDefaults.Web));
            var request = await PostAsync(client, "/api/v1/data-approvals/requests", new CreateDataApprovalRequestBody(DataApprovalScenarioKeys.SerialRuleHostUpdate, ruleId, snapshot, Guid.NewGuid().ToString("N")), HttpStatusCode.Created);
            var instanceId = request.GetProperty("workflowInstanceId").GetGuid();
            var todo = await db.ExecuteScalarAsync<Guid>("SELECT Id FROM fn_workflow_todo WHERE InstanceId=@instanceId AND StatusKey='active'", new { instanceId });
            var detail = await client.GetFromJsonAsync<JsonElement>($"/api/v1/workflow/todos/{todo:D}");
            await PostAsync(client, $"/api/v1/workflow/todos/{todo:D}/approve", new { expectedRevision = detail.GetProperty("revision").GetInt64(),
                expectedFormRevision = detail.GetProperty("submissionRevision").GetInt64(), fieldPatch = new { }, idempotencyKey = Guid.NewGuid().ToString("N") });
            Assert.AreEqual("active", await db.ExecuteScalarAsync<string>("SELECT StatusKey FROM fn_workflow_instance WHERE Id=@instanceId", new { instanceId }));
            Assert.AreEqual(1, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_business_cmd WHERE InstanceId=@instanceId AND StatusKey='requested'", new { instanceId }));
            Assert.AreEqual(0, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_todo WHERE InstanceId=@instanceId AND StatusKey='active'", new { instanceId }));
            return (request.GetProperty("id").GetGuid(), instanceId, ruleId);
        }
        async Task Deliver(Guid instanceId, bool results = false, bool twice = false)
        {
            await using var scope = factory.Services.CreateAsyncScope(); scope.ServiceProvider.GetRequiredService<ICurrentTenantContextWriter>().SetHost();
            var serializer = scope.ServiceProvider.GetRequiredService<IIntegrationEventSerializer>();
            IIntegrationEventHandler handler = results ? ActivatorUtilities.CreateInstance<WorkflowBusinessCommandResultHandler>(scope.ServiceProvider)
                : ActivatorUtilities.CreateInstance<WorkflowBusinessCommandRequestedHandler>(scope.ServiceProvider);
            var messages = await db.QueryAsync<(Guid Id, byte[] Payload)>("SELECT Id,Payload FROM fn_outbox_message WHERE MessageType=@type ORDER BY OccurredAtUtc,Id", new { type = handler.EventType });
            foreach (var message in messages.Where(m => results ? serializer.Deserialize<WorkflowBusinessCommandResultIntegrationEvent>(m.Payload).InstanceId == instanceId : serializer.Deserialize<WorkflowBusinessCommandRequestedIntegrationEvent>(m.Payload).Binding.InstanceId == instanceId))
            {
                var context = new IntegrationEventContext(message.Id, handler.EventType, 1, null, null, DateTimeOffset.UtcNow);
                await handler.HandleAsync(context, message.Payload, default);
                if (twice) await handler.HandleAsync(context with { MessageId = Guid.CreateVersion7() }, message.Payload, default);
            }
        }
        async Task<int> Process(bool work = false) { await using var scope = factory.Services.CreateAsyncScope(); return work ? await scope.ServiceProvider.GetRequiredService<WorkflowActivityWorkProcessor>().ProcessPendingAsync(default)
            : await scope.ServiceProvider.GetRequiredService<WorkflowBusinessCommandProcessor>().ProcessPendingAsync(default); }
        async Task Action(Guid instanceId, string action) => _ = await PostAsync(client, $"/api/v1/workflow/instances/{instanceId:D}/{action}", new {
            expectedRevision = await db.ExecuteScalarAsync<long>("SELECT Revision FROM fn_workflow_instance WHERE Id=@instanceId", new { instanceId }), reason = "节点时序验收", idempotencyKey = Guid.NewGuid().ToString("N") });

        var success = await Start();
        // 所有者已提交而审批回执事务失败；重投必须读原凭据，不得重复应用规则。
        receiptFault.Arm(); await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => Deliver(success.InstanceId));
        Assert.AreEqual(0, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_dataapproval_cmd_receipt WHERE InstanceId=@id", new { id = success.InstanceId }));
        Assert.AreEqual(1, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_serialnumbers_approval_cmd WHERE RuleId=@id", new { id = success.RuleId }));
        await Deliver(success.InstanceId, twice: true);
        Assert.AreEqual("Applied", await db.ExecuteScalarAsync<string>("SELECT DisplayName FROM fn_serialnumbers_rule WHERE Id=@id", new { id = success.RuleId }));
        Assert.AreEqual(1, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_dataapproval_cmd_receipt WHERE InstanceId=@id", new { id = success.InstanceId }));
        await Action(success.InstanceId, "pause"); await Deliver(success.InstanceId, results: true, twice: true);
        Assert.AreEqual(0, await Process());
        Assert.AreEqual(0, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_activity_work WHERE InstanceId=@id", new { id = success.InstanceId }));
        await Action(success.InstanceId, "resume"); Assert.AreEqual(1, await Process()); Assert.AreEqual(1, await Process(work: true));
        Assert.AreEqual("completed", await db.ExecuteScalarAsync<string>("SELECT StatusKey FROM fn_workflow_instance WHERE Id=@id", new { id = success.InstanceId }));
        await using (var scope = factory.Services.CreateAsyncScope()) {
            scope.ServiceProvider.GetRequiredService<ICurrentTenantContextWriter>().SetHost(); var serializer = scope.ServiceProvider.GetRequiredService<IIntegrationEventSerializer>();
            var sink = scope.ServiceProvider.GetServices<IWorkflowInstanceCompletedSink>().Single(item => item.GetType().Assembly == typeof(DataApprovalModule).Assembly);
            var messages = await db.QueryAsync<byte[]>("SELECT Payload FROM fn_outbox_message WHERE MessageType=@type", new { type = WorkflowNotificationIntegrationEventTypes.InstanceCompleted });
            var result = messages.Select(m => serializer.Deserialize<WorkflowInstanceCompletedIntegrationEvent>(m)).Single(m => m.InstanceId == success.InstanceId);
            await sink.HandleAsync(new(Guid.CreateVersion7(), WorkflowNotificationIntegrationEventTypes.InstanceCompleted, 1, null, null, DateTimeOffset.UtcNow), result, default);
        }
        Assert.AreEqual("approved", await db.ExecuteScalarAsync<string>("SELECT StatusKey FROM fn_dataapproval_request WHERE Id=@id", new { id = success.RequestId }));
        Assert.AreEqual(2L, await db.ExecuteScalarAsync<long>("SELECT Version FROM fn_serialnumbers_rule WHERE Id=@id", new { id = success.RuleId }));
        await Deliver(success.InstanceId, twice: true); Assert.AreEqual(0, await Process());

        // 取消先到时由所有者写无效果墓碑，迟到执行不能再产生应用。
        var cancelled = await Start(); await Action(cancelled.InstanceId, "cancel"); Assert.AreEqual(1, await Process());
        await Deliver(cancelled.InstanceId, twice: true); await Deliver(cancelled.InstanceId, results: true, twice: true);
        Assert.AreEqual("Before", await db.ExecuteScalarAsync<string>("SELECT DisplayName FROM fn_serialnumbers_rule WHERE Id=@id", new { id = cancelled.RuleId }));
        Assert.AreEqual("cancelled", await db.ExecuteScalarAsync<string>("SELECT StatusKey FROM fn_workflow_business_cmd WHERE InstanceId=@id", new { id = cancelled.InstanceId }));

        // 所有者已提交但Workflow尚未收到回执时取消，只允许按自身应用版本精确恢复。
        var late = await Start(); await Deliver(late.InstanceId); await Action(late.InstanceId, "cancel"); Assert.AreEqual(1, await Process());
        await Deliver(late.InstanceId, twice: true); await Deliver(late.InstanceId, results: true, twice: true);
        Assert.AreEqual("Before", await db.ExecuteScalarAsync<string>("SELECT DisplayName FROM fn_serialnumbers_rule WHERE Id=@id", new { id = late.RuleId }));
        Assert.AreEqual(3L, await db.ExecuteScalarAsync<long>("SELECT Version FROM fn_serialnumbers_rule WHERE Id=@id", new { id = late.RuleId }));
        Assert.AreEqual("compensated", await db.ExecuteScalarAsync<string>("SELECT StatusKey FROM fn_workflow_business_cmd WHERE InstanceId=@id", new { id = late.InstanceId }));
        Assert.AreEqual(0, await Process(work: true));

        // 模拟DDL已部分成功、DbUp未记账；重跑只恢复缺失对象，不能覆盖在途凭据。
        var receiptCount = await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_dataapproval_cmd_receipt");
        await db.ExecuteAsync(provider == DatabaseProvider.SqlServer
            ? "ALTER TABLE dbo.fn_workflow_business_cmd DROP CONSTRAINT fk_fn_workflow_business_cmd_step; DROP INDEX ix_fn_workflow_business_cmd_ready ON dbo.fn_workflow_business_cmd; ALTER TABLE dbo.fn_dataapproval_cmd_receipt DROP CONSTRAINT ck_fn_dataapproval_cmd_receipt_resultjson; DELETE FROM dbo.SchemaVersions WHERE ScriptName LIKE '%317_WorkflowBusinessCommands.sql' OR ScriptName LIKE '%318_DataApprovalCommandReceipts.sql'"
            : "ALTER TABLE fn_workflow_business_cmd DROP FOREIGN KEY fk_fn_workflow_business_cmd_step; ALTER TABLE fn_workflow_business_cmd DROP INDEX ix_fn_workflow_business_cmd_ready; ALTER TABLE fn_dataapproval_cmd_receipt DROP CHECK ck_fn_dataapproval_cmd_receipt_resultjson; DELETE FROM schemaversions WHERE ScriptName LIKE '%317_WorkflowBusinessCommands.sql' OR ScriptName LIKE '%318_DataApprovalCommandReceipts.sql'");
        await WorkflowHumanMigrationRecoveryTests.MigrateAsync(provider, connection); await WorkflowHumanMigrationRecoveryTests.MigrateAsync(provider, connection);
        Assert.AreEqual(receiptCount, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_dataapproval_cmd_receipt"));
        Assert.AreEqual(1, await db.ExecuteScalarAsync<int>(provider == DatabaseProvider.SqlServer
            ? "SELECT COUNT(*) FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID('fn_workflow_business_cmd') AND name='fk_fn_workflow_business_cmd_step'"
            : "SELECT COUNT(*) FROM information_schema.TABLE_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_business_cmd' AND CONSTRAINT_NAME='fk_fn_workflow_business_cmd_step'"));
        Assert.AreEqual(1, await db.ExecuteScalarAsync<int>(provider == DatabaseProvider.SqlServer
            ? "SELECT COUNT(*) FROM sys.indexes WHERE object_id=OBJECT_ID('fn_workflow_business_cmd') AND name='ix_fn_workflow_business_cmd_ready'"
            : "SELECT COUNT(DISTINCT INDEX_NAME) FROM information_schema.STATISTICS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_business_cmd' AND INDEX_NAME='ix_fn_workflow_business_cmd_ready'"));
        if (provider == DatabaseProvider.MySql) Assert.AreEqual(0, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM information_schema.ROUTINES WHERE ROUTINE_SCHEMA=DATABASE() AND ROUTINE_NAME IN ('fn_workflow_business_cmd_ensure','fn_dataapproval_cmd_receipt_ensure')"));
    }
    /// <summary>只使下一次本模块回执插入失败，真实所有者写入和事务执行器均保持正式实现。</summary>
    private sealed class ReceiptFaultExecutor(ICommandExecutor inner, ReceiptFault fault) : ICommandExecutor
    {
        public Task<int> ExecuteAsync(SqlStatement statement, object? parameters = null, CancellationToken cancellationToken = default) =>
            statement.Name == "data_approval.command.insert" && fault.Take() ? Task.FromResult(0) : inner.ExecuteAsync(statement, parameters, cancellationToken);
    }
    private sealed class ReceiptFault
    {
        private int armed;
        public void Arm() => Interlocked.Exchange(ref armed, 1);
        public bool Take() => Interlocked.Exchange(ref armed, 0) == 1;
    }
}
