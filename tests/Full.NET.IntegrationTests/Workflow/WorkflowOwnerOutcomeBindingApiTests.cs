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
using Full.NET.Modules.DataApproval.Contracts;
using Full.NET.Modules.DataApproval;
using Full.NET.Modules.SerialNumbers.Contracts;
using Full.NET.Modules.Workflow.Contracts;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using MySqlConnector;
using static Full.NET.IntegrationTests.Workflow.WorkflowDurableDelayApiTests;

namespace Full.NET.IntegrationTests.Workflow;

/// <summary>真实所有者只接纳已绑定根实例的终态，业务ID、主体或场景相同不能代替绑定授权。</summary>
[TestClass]
public sealed class WorkflowOwnerOutcomeBindingApiTests
{
    [TestMethod]
    public async Task Owner_binding_with_sql_server() => await VerifyAsync(DatabaseProvider.SqlServer, await SharedDatabaseFixture.CreateSqlServerDatabaseAsync());
    [TestMethod]
    public async Task Owner_binding_with_mysql() => await VerifyAsync(DatabaseProvider.MySql, await SharedDatabaseFixture.CreateMySqlDatabaseAsync());
    private static async Task VerifyAsync(DatabaseProvider provider, string connection)
    {
        using var factory = new FullNetApiFactory(provider, connection, includeEnterpriseRequest: true, configureTestServices: services => {
            // 使用Worker正式注册的公共Sink契约，不扩大模块内部实现的可见性或启动后台扫描循环。
            var worker = new ServiceCollection(); new DataApprovalModule().AddBackgroundServices(worker, new ConfigurationBuilder().Build());
            foreach (var descriptor in worker.Where(item => item.ServiceType == typeof(IWorkflowInstanceCompletedSink) || item.ServiceType == typeof(IWorkflowInstanceRejectedSink)))
                services.Add(descriptor);
        }); await factory.InitializeAsync(useSchemaTemplate: false);
        var person = await factory.CreateHostIdentityAsync("owner.binding." + Guid.NewGuid().ToString("N"), [DataApprovalPermissions.Create,
            DataApprovalPermissions.Read, DataApprovalPermissions.ScenariosManage, SerialNumberRulePermissions.Create, SerialNumberRulePermissions.Read,
            WorkflowPermissions.FormsCreate, WorkflowPermissions.FormsPublish, WorkflowPermissions.DefinitionsCreate, WorkflowPermissions.DefinitionsPublish,
            WorkflowPermissions.TodosRead, WorkflowPermissions.TodosApprove, WorkflowPermissions.InstancesStart]);
        using var client = factory.CreateClientForHost("localhost"); client.DefaultRequestHeaders.Authorization = new("Bearer", person.AccessToken);
        await using DbConnection db = provider == DatabaseProvider.SqlServer ? new SqlConnection(connection) : new MySqlConnection(Migration114To126RecoveryTestSupport.MySqlQueryConnectionString(connection));
        var form = await PostAsync(client, "/api/v1/workflow/forms", new { formKey = "owner." + Guid.NewGuid().ToString("N"), draft = new { schemaVersion = 1, adapterVersion = 1,
            sections = new[] { new { sectionKey = "main", fields = new[] { new { fieldKey = "note", fieldTypeKey = "text", required = false, constraints = new { } } } } } } }, HttpStatusCode.Created);
        var formVersion = (await PostAsync(client, $"/api/v1/workflow/forms/{form.GetProperty("id").GetGuid():D}/publish", new { expectedRevision = 1 })).GetProperty("id").GetGuid();
        object Node(string key, string kind, object config) => new { nodeKey = key, nodeTypeKey = kind, nodeSchemaVersion = 1, config };
        var definition = await PostAsync(client, "/api/v1/workflow/definitions", new { definitionKey = "owner." + Guid.NewGuid().ToString("N"), draft = new { schemaVersion = 1,
            workflowKindKey = "approval", nodes = new[] { Node("start", "start", new { nextNodeKeys = new[] { "review" } }),
                Node("review", "human.approval", new { nextNodeKeys = new[] { "end" }, assigneePolicy = new { sources = new[] { new { resolverKindKey = "specified_users", userIds = new[] { person.UserId } } } } }), Node("end", "end", new { }) } } }, HttpStatusCode.Created);
        var version = (await PostAsync(client, $"/api/v1/workflow/definitions/{definition.GetProperty("id").GetGuid():D}/publish", new { expectedRevision = 1, formVersionId = formVersion })).GetProperty("id").GetGuid();
        using (var binding = await client.PutAsJsonAsync($"/api/v1/data-approvals/scenarios/{DataApprovalScenarioKeys.SerialRuleHostUpdate}", new { isEnabled = true, workflowDefinitionVersionId = version, version = (long?)null }))
            Assert.AreEqual(HttpStatusCode.OK, binding.StatusCode, await binding.Content.ReadAsStringAsync());
        var rule = await PostAsync(client, "/api/v1/serial-numbers/rules", new CreateSerialNumberRuleRequest("owner." + Guid.NewGuid().ToString("N"), "Before", null,
            SerialNumberRuleScope.Host, SerialNumberResetInterval.Never, "OWN-{sequence:4}", 1, 9999, 0, true), HttpStatusCode.Created);
        var ruleId = rule.GetProperty("id").GetGuid();
        foreach (var businessType in new[] { DataApprovalWorkflowBusinessTypes.SerialRuleUpdate, " " + DataApprovalWorkflowBusinessTypes.SerialRuleDisable + " ", "demo.enterprise_request" }) {
            var denied = await PostAsync(client, "/api/v1/workflow/instances", new { definitionVersionId = version, businessType,
                businessId = ruleId.ToString("D"), initialValues = new { }, idempotencyKey = Guid.NewGuid().ToString("N") }, HttpStatusCode.Forbidden);
            Assert.AreEqual("workflow.business_owner_required", denied.GetProperty("code").GetString());
        }
        var snapshot = JsonSerializer.Serialize(new UpdateSerialNumberRuleRequest("Approved", null, SerialNumberRuleScope.Host, SerialNumberResetInterval.Never,
            "OWN-{sequence:4}", 1, 9999, 0, true, rule.GetProperty("version").GetInt64()), new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var request = await PostAsync(client, "/api/v1/data-approvals/requests", new CreateDataApprovalRequestBody(DataApprovalScenarioKeys.SerialRuleHostUpdate,
            ruleId, snapshot, Guid.NewGuid().ToString("N")), HttpStatusCode.Created);
        var requestId = request.GetProperty("id").GetGuid(); var instanceId = request.GetProperty("workflowInstanceId").GetGuid();
        await using var scope = factory.Services.CreateAsyncScope(); scope.ServiceProvider.GetRequiredService<ICurrentTenantContextWriter>().SetHost();
        var context = new IntegrationEventContext(Guid.CreateVersion7(), "test", 1, null, null, DateTimeOffset.UtcNow);
        var rejected = scope.ServiceProvider.GetServices<IWorkflowInstanceRejectedSink>().Single(item => item.GetType().Assembly == typeof(DataApprovalModule).Assembly);
        var completed = scope.ServiceProvider.GetServices<IWorkflowInstanceCompletedSink>().Single(item => item.GetType().Assembly == typeof(DataApprovalModule).Assembly);
        await rejected.HandleAsync(context, new(Guid.CreateVersion7(), person.UserId, DataApprovalWorkflowBusinessTypes.SerialRuleUpdate, requestId.ToString("D"), DateTimeOffset.UtcNow), default);
        await completed.HandleAsync(context, new(Guid.CreateVersion7(), person.UserId, DataApprovalWorkflowBusinessTypes.SerialRuleUpdate, requestId.ToString("D"), DateTimeOffset.UtcNow), default);
        await rejected.HandleAsync(context, new(instanceId, Guid.CreateVersion7(), DataApprovalWorkflowBusinessTypes.SerialRuleUpdate, requestId.ToString("D"), DateTimeOffset.UtcNow), default);
        Assert.AreEqual(DataApprovalStatusKeys.InReview, await db.ExecuteScalarAsync<string>("SELECT StatusKey FROM fn_dataapproval_request WHERE Id=@requestId", new { requestId }));
        var todo = await db.ExecuteScalarAsync<Guid>("SELECT Id FROM fn_workflow_todo WHERE InstanceId=@instanceId AND StatusKey='active'", new { instanceId });
        var detail = await client.GetFromJsonAsync<JsonElement>($"/api/v1/workflow/todos/{todo:D}");
        await PostAsync(client, $"/api/v1/workflow/todos/{todo:D}/approve", new { expectedRevision = detail.GetProperty("revision").GetInt64(),
            expectedFormRevision = detail.GetProperty("submissionRevision").GetInt64(), fieldPatch = new { }, idempotencyKey = Guid.NewGuid().ToString("N") });
        var serializer = scope.ServiceProvider.GetRequiredService<IIntegrationEventSerializer>();
        var events = await db.QueryAsync<(Guid Id, byte[] Payload)>("SELECT Id,Payload FROM fn_outbox_message WHERE MessageType=@type", new { type = WorkflowNotificationIntegrationEventTypes.InstanceCompleted });
        var message = events.Single(row => serializer.Deserialize<WorkflowInstanceCompletedIntegrationEvent>(row.Payload).InstanceId == instanceId);
        var value = serializer.Deserialize<WorkflowInstanceCompletedIntegrationEvent>(message.Payload);
        // 模拟自动流程先完成、独立所有者关联事务尚未提交；重投必须保留，不能丢失真实终态。
        await db.ExecuteAsync("UPDATE fn_dataapproval_request SET WorkflowInstanceId=NULL,StatusKey='pending' WHERE Id=@requestId", new { requestId });
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => completed.HandleAsync(context with { MessageId = message.Id }, value, default));
        Assert.AreEqual(DataApprovalStatusKeys.Pending, await db.ExecuteScalarAsync<string>("SELECT StatusKey FROM fn_dataapproval_request WHERE Id=@requestId", new { requestId }));
        await db.ExecuteAsync("UPDATE fn_dataapproval_request SET WorkflowInstanceId=@instanceId,StatusKey='in_review' WHERE Id=@requestId", new { requestId, instanceId });
        await completed.HandleAsync(context with { MessageId = message.Id, MessageType = WorkflowNotificationIntegrationEventTypes.InstanceCompleted }, value, default);
        await completed.HandleAsync(context with { MessageId = Guid.CreateVersion7() }, value, default);
        Assert.AreEqual(DataApprovalStatusKeys.Approved, await db.ExecuteScalarAsync<string>("SELECT StatusKey FROM fn_dataapproval_request WHERE Id=@requestId", new { requestId }));
        var updated = await client.GetFromJsonAsync<SerialNumberRuleResponse>($"/api/v1/serial-numbers/rules/{ruleId:D}");
        Assert.AreEqual("Approved", updated!.DisplayName); Assert.AreEqual(rule.GetProperty("version").GetInt64() + 1, updated.Version);
    }
}
