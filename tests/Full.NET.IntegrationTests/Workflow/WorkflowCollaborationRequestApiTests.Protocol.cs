using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Dapper;
using DbUp;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Tenancy;
using Full.NET.IntegrationTests.Api;
using Full.NET.Migrations.DbUp;
using Full.NET.Data.Abstractions;
using Full.NET.Data.MySql;
using Full.NET.Modules.Workflow.Execution;
using Full.NET.Modules.Workflow.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Full.NET.IntegrationTests.Workflow;

public sealed partial class WorkflowCollaborationRequestApiTests
{
    private static Task ConcurrentRelayAsync(FullNetApiFactory factory)
    {
        async Task Tick()
        {
            await using var worker = factory.Services.CreateAsyncScope();
            worker.ServiceProvider.GetRequiredService<ICurrentTenantContextWriter>().SetHost();
            await worker.ServiceProvider.GetRequiredService<WorkflowCollaborationRelay>().ProcessPendingAsync(default);
        }
        return Task.WhenAll(Tick(), Tick());
    }

    // 使用真实Outbox正文和本地目标作用域调用消费者；不据此声称物理Broker链路通过。
    private static async Task<int> RelayAsync(FullNetApiFactory factory, DbConnection db)
    {
        var count = 0;
        for (var tick = 0; tick < 4; tick++)
        {
            await using (var worker = factory.Services.CreateAsyncScope())
            {
                worker.ServiceProvider.GetRequiredService<ICurrentTenantContextWriter>().SetHost();
                count += await worker.ServiceProvider.GetRequiredService<WorkflowCollaborationFilePreparationProcessor>().ProcessPendingAsync(default);
            }
            foreach (var message in await db.QueryAsync<Message>("SELECT Id,MessageType,SchemaVersion,TenantId,TraceId,OccurredAtUtc,Payload FROM fn_outbox_message WHERE MessageType=@type AND Id NOT IN (SELECT EnvelopeMessageId FROM fn_workflow_collaboration_delivery)", new { type = WorkflowCollaborationIntegrationEventTypes.Requested }))
            {
                await using var receiver = factory.Services.CreateAsyncScope();
                var writer = receiver.ServiceProvider.GetRequiredService<ICurrentTenantContextWriter>();
                if (message.TenantId is { } id) writer.SetTenant(new TenantContext(id, "source", "源")); else writer.SetHost();
                var handler = new WorkflowCollaborationRequestedHandler(receiver.ServiceProvider.GetRequiredService<IIntegrationEventSerializer>(),
                    receiver.ServiceProvider.GetRequiredService<IQueryExecutor>(), receiver.ServiceProvider.GetRequiredService<ICommandExecutor>(),
                    receiver.ServiceProvider.GetRequiredService<Full.NET.Abstractions.Time.IClock>(), writer);
                await handler.HandleAsync(new(message.Id, message.MessageType, message.SchemaVersion, message.TenantId, message.TraceId, message.OccurredAtUtc), message.Payload, default);
            }
            await using (var worker = factory.Services.CreateAsyncScope())
            {
                worker.ServiceProvider.GetRequiredService<ICurrentTenantContextWriter>().SetHost();
                count += await worker.ServiceProvider.GetRequiredService<WorkflowCollaborationRelay>().ProcessPendingAsync(default);
            }
            var messages = await db.QueryAsync<Message>("""
                SELECT m.Id,m.MessageType,m.SchemaVersion,m.TenantId,m.TraceId,m.OccurredAtUtc,m.Payload
                FROM fn_outbox_message m INNER JOIN fn_workflow_collaboration_control c ON c.StatusKey='published'
                WHERE m.MessageType IN (@created,@completed,@cancelled)
                """, new { created = WorkflowCollaborationIntegrationEventTypes.Created, completed = WorkflowCollaborationIntegrationEventTypes.Completed, cancelled = WorkflowCollaborationIntegrationEventTypes.Cancelled });
            foreach (var message in messages.DistinctBy(item => item.Id))
            {
                await using var consumer = factory.Services.CreateAsyncScope();
                var tenant = consumer.ServiceProvider.GetRequiredService<ICurrentTenantContextWriter>();
                if (message.TenantId is { } id) tenant.SetTenant(new TenantContext(id, "target", "目标")); else tenant.SetHost();
                // API夹具不启用Worker订阅；使用Worker同一静态Handler及本地DI闭包，不启动后台循环。
                var inbox = consumer.ServiceProvider.GetRequiredService<WorkflowCollaborationControlInbox>();
                IIntegrationEventHandler handler = message.MessageType switch
                {
                    WorkflowCollaborationIntegrationEventTypes.Created => new WorkflowCollaborationCreatedHandler(inbox),
                    WorkflowCollaborationIntegrationEventTypes.Completed => new WorkflowCollaborationCompletedHandler(inbox),
                    WorkflowCollaborationIntegrationEventTypes.Cancelled => new WorkflowCollaborationCancelledHandler(inbox),
                    _ => throw new InvalidDataException("Unexpected collaboration control event.")
                };
                var context = new IntegrationEventContext(message.Id, message.MessageType, message.SchemaVersion, message.TenantId, message.TraceId, message.OccurredAtUtc);
                await handler.HandleAsync(context, message.Payload, default);
                await handler.HandleAsync(context with { MessageId = Guid.CreateVersion7() }, message.Payload, default);
                var value = consumer.ServiceProvider.GetRequiredService<IIntegrationEventSerializer>().Deserialize<WorkflowCollaborationControlIntegrationEvent>(message.Payload)!;
                Assert.AreEqual(message.Id, await db.ExecuteScalarAsync<Guid>("SELECT EnvelopeMessageId FROM fn_workflow_collaboration_control WHERE Id=@id", new { id = value.ControlId }), "重投必须保留第一次物理收件");
                Assert.AreEqual(message.TenantId, tenant.Id);
            }
        }
        return count;
    }

    private static async Task ProtocolCaseAsync(FullNetApiFactory factory, DbConnection db, HttpClient source, HttpClient target,
        Guid? sourceTenant, Guid? targetTenant, Guid instance, Guid call, Guid grant, InsertFault fault, string scenario, int before)
    {
        var completedBefore = await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_outbox_message WHERE MessageType=@type", new { type = WorkflowNotificationIntegrationEventTypes.InstanceCompleted });
        if (scenario == "claims") { await VerifyClaimListAsync(factory, db, source, target, instance, call, grant); return; }
        if (scenario == "uncreated")
        {
            await PostAsync(source, $"/api/v1/workflow/instances/{instance:D}/cancel", new { expectedRevision = 1, reason = "目标创建前取消", idempotencyKey = "early-cancel" });
            await RelayAsync(factory, db);
            Assert.AreEqual(before + 1, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_instance"));
            Assert.AreEqual(0, await db.ExecuteScalarAsync<int>("SELECT Reserved FROM fn_workflow_exec_budget WHERE InstanceId=@instance", new { instance }));
            Assert.AreEqual("cancelled", await db.ExecuteScalarAsync<string>("SELECT StatusKey FROM fn_workflow_collaboration_call WHERE Id=@call", new { call }));
            Assert.AreEqual(1, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_collaboration_control WHERE CallId=@call AND KindKey='cancelled' AND StatusKey='applied' AND BodyJson LIKE '%not_created%'", new { call }));
            return;
        }
        if (scenario is "paused" or "paused_revoked")
        {
            await PostAsync(source, $"/api/v1/workflow/instances/{instance:D}/pause", new { expectedRevision = 1, reason = "保留真实等待代次", idempotencyKey = "pause-source" });
            await RelayAsync(factory, db);
            Assert.AreEqual("requested", await db.ExecuteScalarAsync<string>("SELECT StatusKey FROM fn_workflow_collaboration_call WHERE Id=@call", new { call }));
            Assert.AreEqual(0, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_collaboration_control WHERE CallId=@call AND KindKey='cancelled'", new { call }));
            if (scenario == "paused_revoked")
            {
                await PostAsync(source, $"/api/v1/workflow/collaboration-grants/{grant:D}/revoke", new { expectedRevision = 2, idempotencyKey = "paused-revoke", reason = "暂停期间撤销" });
                await RelayAsync(factory, db);
                Assert.AreEqual("denied", await db.ExecuteScalarAsync<string>("SELECT StatusKey FROM fn_workflow_collaboration_call WHERE Id=@call", new { call }));
                Assert.AreEqual("suspended", await db.ExecuteScalarAsync<string>("SELECT StatusKey FROM fn_workflow_instance WHERE Id=@instance", new { instance }));
                Assert.IsTrue(await db.ExecuteScalarAsync<int>("SELECT Reserved FROM fn_workflow_exec_budget WHERE InstanceId=@instance", new { instance }) < 32);
                return;
            }
            await PostAsync(source, $"/api/v1/workflow/instances/{instance:D}/resume", new { expectedRevision = 2, reason = "恢复原等待", idempotencyKey = "resume-source" });
            scenario = "success";
        }
        await RelayAsync(factory, db);
        var targetId = await db.ExecuteScalarAsync<Guid>("SELECT TargetInstanceId FROM fn_workflow_collaboration_admission WHERE Id=@call", new { call });
        Assert.AreEqual("accepted", await db.ExecuteScalarAsync<string>("SELECT StatusKey FROM fn_workflow_collaboration_call WHERE Id=@call", new { call }));
        if (scenario == "unknown")
        {
            await UnknownEffectAsync(factory, db, source, target, instance, targetId, call);
            return;
        }
        if (scenario == "revoked")
            await PostAsync(source, $"/api/v1/workflow/collaboration-grants/{grant:D}/revoke", new { expectedRevision = 2, idempotencyKey = "revoke-live", reason = "运行中撤销" });
        else
        {
            var todoId = await db.ExecuteScalarAsync<Guid>("SELECT Id FROM fn_workflow_todo WHERE InstanceId=@targetId AND StatusKey='active'", new { targetId });
            var todo = await target.GetFromJsonAsync<JsonElement>($"/api/v1/workflow/todos/{todoId:D}");
            await PostAsync(target, $"/api/v1/workflow/todos/{todoId:D}/" + (scenario == "rejected" ? "reject" : "approve"), new {
                expectedRevision = todo.GetProperty("revision").GetInt64(), expectedFormRevision = todo.GetProperty("submissionRevision").GetInt64(), fieldPatch = new { }, comment = "目标事实", idempotencyKey = "target-outcome" });
        }
        if (scenario == "conflict")
            await db.ExecuteAsync("UPDATE fn_workflow_form_submission SET SubmissionJson=@json,Revision=Revision+1 WHERE InstanceId=@instance", new { instance, json = "{\"title\":\"changed\",\"secret\":\"source-private\"}" });
        if (scenario == "success")
        {
            var hits = fault.CompletionHits;
            fault.Completion = true;
            await RelayAsync(factory, db); fault.Completion = false;
            Assert.IsTrue(fault.CompletionHits > hits, "本次必须实际触及接纳事务中途失败");
            Assert.AreEqual("accepted", await db.ExecuteScalarAsync<string>("SELECT StatusKey FROM fn_workflow_collaboration_call WHERE Id=@call", new { call }));
            Assert.AreEqual("active", await db.ExecuteScalarAsync<string>("SELECT StatusKey FROM fn_workflow_step WHERE Id=@call", new { call }));
            Assert.AreEqual(32, await db.ExecuteScalarAsync<int>("SELECT Reserved FROM fn_workflow_exec_budget WHERE InstanceId=@instance", new { instance }));
            Assert.IsFalse((await db.ExecuteScalarAsync<string>("SELECT SubmissionJson FROM fn_workflow_form_submission WHERE InstanceId=@instance", new { instance }))!.Contains("answer", StringComparison.Ordinal));
        }
        await ConcurrentRelayAsync(factory); await RelayAsync(factory, db);
        var expected = scenario == "success" ? "completed" : "denied";
        Assert.AreEqual(expected, await db.ExecuteScalarAsync<string>("SELECT StatusKey FROM fn_workflow_collaboration_call WHERE Id=@call", new { call }));
        Assert.AreEqual(scenario == "revoked" ? "cancelled" : scenario == "rejected" ? "rejected" : "completed", await db.ExecuteScalarAsync<string>("SELECT StatusKey FROM fn_workflow_instance WHERE Id=@targetId", new { targetId }));
        var sourceJson = JsonSerializer.Deserialize<JsonElement>((await db.ExecuteScalarAsync<string>("SELECT SubmissionJson FROM fn_workflow_form_submission WHERE InstanceId=@instance", new { instance }))!);
        Assert.AreEqual("source-private", sourceJson.GetProperty("secret").GetString());
        var used = await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_step WHERE InstanceId=@targetId", new { targetId });
        Assert.AreEqual(used, await db.ExecuteScalarAsync<int>("SELECT Reserved FROM fn_workflow_exec_budget WHERE InstanceId=@instance", new { instance }));
        if (scenario == "success")
        {
            Assert.AreEqual("shared", sourceJson.GetProperty("answer").GetString());
            await using var worker = factory.Services.CreateAsyncScope();
            var tenant = worker.ServiceProvider.GetRequiredService<ICurrentTenantContextWriter>();
            if (sourceTenant is { } id) tenant.SetTenant(new TenantContext(id, "source", "源")); else tenant.SetHost();
            await worker.ServiceProvider.GetRequiredService<WorkflowActivityWorkProcessor>().ProcessPendingAsync(default);
            Assert.AreEqual("completed", await db.ExecuteScalarAsync<string>("SELECT StatusKey FROM fn_workflow_instance WHERE Id=@instance", new { instance }));
            Assert.AreEqual(completedBefore + 1, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_outbox_message WHERE MessageType=@type", new { type = WorkflowNotificationIntegrationEventTypes.InstanceCompleted }));
        }
        else
        {
            Assert.IsFalse(sourceJson.TryGetProperty("answer", out _));
            Assert.AreEqual("suspended", await db.ExecuteScalarAsync<string>("SELECT StatusKey FROM fn_workflow_instance WHERE Id=@instance", new { instance }));
            foreach (var action in new[] { "resume", "recover" })
                await PostAsync(source, $"/api/v1/workflow/instances/{instance:D}/{action}", new { expectedRevision = await db.ExecuteScalarAsync<long>("SELECT Revision FROM fn_workflow_instance WHERE Id=@instance", new { instance }), reason = "不允许伪造成功", idempotencyKey = action }, HttpStatusCode.Conflict);
            Assert.AreEqual(completedBefore, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_outbox_message WHERE MessageType=@type", new { type = WorkflowNotificationIntegrationEventTypes.InstanceCompleted }));
        }
        if (scenario == "revoked")
        {
            using var denied = await target.GetAsync($"/api/v1/workflow/instances/{targetId:D}"); Assert.AreEqual(HttpStatusCode.Forbidden, denied.StatusCode);
            Assert.IsNull(await db.ExecuteScalarAsync<Guid?>("SELECT CancelledById FROM fn_workflow_instance WHERE Id=@targetId", new { targetId }));
            Assert.AreEqual(1, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_domain_audit WHERE InstanceId=@targetId AND OperationKey='instance.cancel' AND ActorUserId IS NULL AND DetailJson LIKE '%collaboration_cancel%'", new { targetId }));
        }
    }

    private static async Task UnknownEffectAsync(FullNetApiFactory factory, DbConnection db, HttpClient source, HttpClient target, Guid instance, Guid targetId, Guid call)
    {
        var step = await db.ExecuteScalarAsync<Guid>("SELECT Id FROM fn_workflow_step WHERE InstanceId=@targetId AND NodeKey='approve'", new { targetId });
        var attempt = Guid.CreateVersion7(); var now = DateTimeOffset.UtcNow; var hash = new string('a', 64);
        // 在真实目标根注入持久未知结果；此用例不声称执行过物理HTTP。
        await db.ExecuteAsync("""
            INSERT INTO fn_workflow_connector_call (Id,InstanceId,TenantScopeKey,NodeKey,ContinuationNodeKey,ConnectorKey,ConnectorVersion,ConnectorHash,
                ModeKey,InputJson,InputHash,InputFormRevision,RequestJson,RequestHash,StatusKey,LeaseGeneration,AttemptCount,ApplyAttemptCount,DueAtUtc,Revision,CreatedAtUtc,CurrentAttemptId)
            SELECT @step,Id,TenantScopeKey,'approve','end','test.collaboration.unknown',1,@hash,'sync','{}',@hash,1,'{}',@hash,'outcome_unknown',1,1,0,@now,1,@now,@attempt
            FROM fn_workflow_instance WHERE Id=@targetId;
            INSERT INTO fn_workflow_connector_attempt (Id,CallId,TenantScopeKey,LeaseGeneration,LeaseOwnerKey,RequestHash,StatusKey,OutcomeKey,StartedAtUtc,CompletedAtUtc)
            SELECT @attempt,@step,TenantScopeKey,1,'collaboration-test',@hash,'received','outcome_unknown',@now,@now FROM fn_workflow_instance WHERE Id=@targetId
            """, new { step, targetId, attempt, now, hash });
        await PostAsync(source, $"/api/v1/workflow/instances/{instance:D}/cancel", new { expectedRevision = 1, reason = "等待真实对账证据", idempotencyKey = "cancel-with-unknown" });
        await RelayAsync(factory, db);
        Assert.AreEqual("cancelled", await db.ExecuteScalarAsync<string>("SELECT StatusKey FROM fn_workflow_instance WHERE Id=@targetId", new { targetId }));
        Assert.AreEqual("cancel_requested", await db.ExecuteScalarAsync<string>("SELECT StatusKey FROM fn_workflow_collaboration_admission WHERE Id=@call", new { call }));
        Assert.AreEqual(0, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_collaboration_control WHERE CallId=@call AND KindKey='cancelled'", new { call }));
        Assert.AreEqual(32, await db.ExecuteScalarAsync<int>("SELECT Reserved FROM fn_workflow_exec_budget WHERE InstanceId=@instance", new { instance }));
        var path = $"/api/v1/workflow/instances/{targetId:D}/connector-calls/{step:D}";
        var metadata = await target.GetFromJsonAsync<JsonElement>(path);
        Assert.IsTrue(metadata.GetProperty("requiresReconciliation").GetBoolean());
        var revisions = new { expectedInstanceRevision = await db.ExecuteScalarAsync<long>("SELECT Revision FROM fn_workflow_instance WHERE Id=@targetId", new { targetId }), expectedRevision = metadata.GetProperty("revision").GetInt64() };
        await PostAsync(target, path + "/reconcile", new { revisions.expectedInstanceRevision, revisions.expectedRevision, actionKey = "retry", reason = "不能借对账重试", evidenceReference = "ticket:blocked", idempotencyKey = "unsafe" }, HttpStatusCode.Forbidden);
        var resolution = new { revisions.expectedInstanceRevision, revisions.expectedRevision, actionKey = "confirm_no_effect", reason = "业务所有者确认未执行", evidenceReference = "ticket:collaboration-no-effect", idempotencyKey = "no-effect" };
        var receipt = await PostAsync(target, path + "/reconcile", resolution);
        Assert.AreEqual(receipt.GetRawText(), (await PostAsync(target, path + "/reconcile", resolution)).GetRawText());
        await RelayAsync(factory, db);
        Assert.AreEqual("cancelled", await db.ExecuteScalarAsync<string>("SELECT StatusKey FROM fn_workflow_collaboration_admission WHERE Id=@call", new { call }));
        Assert.AreEqual("cancelled", await db.ExecuteScalarAsync<string>("SELECT StatusKey FROM fn_workflow_collaboration_call WHERE Id=@call", new { call }));
        Assert.AreEqual("outcome_unknown", await db.ExecuteScalarAsync<string>("SELECT OutcomeKey FROM fn_workflow_connector_attempt WHERE Id=@attempt", new { attempt }), "不能删除或改写未知尝试来伪造收敛");
        Assert.IsFalse((await target.GetFromJsonAsync<JsonElement>(path)).GetProperty("requiresReconciliation").GetBoolean());
    }

    private static async Task RecoverControlsAsync(DatabaseProvider provider, string connection, DbConnection db)
    {
        var before = (await db.QueryAsync<string>("SELECT BodyHash FROM fn_workflow_collaboration_control ORDER BY BodyHash")).ToArray();
        await db.ExecuteAsync(provider == DatabaseProvider.SqlServer
            ? "DROP INDEX ux_fn_workflow_collaboration_control_envelope ON fn_workflow_collaboration_control; ALTER TABLE fn_workflow_collaboration_control DROP CONSTRAINT fk_fn_workflow_collaboration_control_call; ALTER TABLE fn_workflow_domain_audit DROP CONSTRAINT ck_fn_workflow_domain_audit_actor; DELETE FROM dbo.SchemaVersions WHERE ScriptName LIKE '%337_WorkflowCollaborationControls.sql'"
            : "ALTER TABLE fn_workflow_collaboration_control DROP INDEX ux_fn_workflow_collaboration_control_envelope; ALTER TABLE fn_workflow_collaboration_control DROP FOREIGN KEY fk_fn_workflow_collaboration_control_call; ALTER TABLE fn_workflow_domain_audit DROP CHECK ck_fn_workflow_domain_audit_actor; DELETE FROM schemaversions WHERE ScriptName LIKE '%337_WorkflowCollaborationControls.sql'");
        foreach (var expected in new[] { 1, 0 })
        {
            var builder = provider == DatabaseProvider.SqlServer ? DeployChanges.To.SqlDatabase(connection) : DeployChanges.To.MySqlDatabase(MySqlConnectionStringPolicy.Create(connection, MySqlGuidStorageMode.Binary16, allowUserVariables: true));
            var result = builder.WithScriptsEmbeddedInAssembly(typeof(DbUpMigrationRunner).Assembly, name => name.Contains($".Migrations.{provider}.", StringComparison.Ordinal) && name.EndsWith(".337_WorkflowCollaborationControls.sql", StringComparison.Ordinal))
                .WithExecutionTimeout(TimeSpan.FromSeconds(300)).Build().PerformUpgrade();
            Assert.IsTrue(result.Successful, result.Error?.ToString()); Assert.HasCount(expected, result.Scripts);
        }
        CollectionAssert.AreEqual(before, (await db.QueryAsync<string>("SELECT BodyHash FROM fn_workflow_collaboration_control ORDER BY BodyHash")).ToArray());
    }
}
