using System.Data.Common;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Dapper;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.IntegrationTests.Migrations;
using Full.NET.Modules.EnterpriseRequest;
using Full.NET.Modules.EnterpriseRequest.Contracts;
using Full.NET.Modules.EnterpriseRequest.Generated;
using Full.NET.Modules.Notifications;
using Full.NET.Modules.Organization.Contracts;
using Full.NET.Modules.Workflow.Contracts;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;

namespace Full.NET.IntegrationTests.EnterpriseRequest;

internal static partial class EnterpriseRequestAssertions
{
    /// <summary>用真实恢复 Worker 复现待办完成后未推进的持久化故障，验证绑定申请的暂停与取消闭环。</summary>
    public static async Task VerifyBoundRuntimeFailureAsync(FullNetApiFactory factory, CancellationToken ct = default)
    {
        await factory.InitializeAsync(ct);
        using var client = factory.CreateClientForHost("localhost");
        var token = await LoginAndEnterAcmeTenantAsync(client, ct);
        await EnterpriseRequestWorkflowBootstrap.PublishDemoApprovalDefinitionInTenantAsync(client, token, ct);
        var organizationUnitId = await CreateOrganizationUnitAsync(client, token, ct);
        using var create = AuthorizedRuntimeRequest(HttpMethod.Post, BasePath, token, new
        {
            requestNumber = $"FAIL-{Guid.NewGuid():N}"[..16], title = "运行中故障验收",
            status = EnterpriseRequestStatusKeys.Draft, totalAmount = 42m, applicantUserId = Guid.NewGuid(),
        });
        create.Headers.Add(OrganizationRequestHeaders.OrganizationUnitId, organizationUnitId.ToString("D"));
        using var createdResponse = await client.SendAsync(create, ct);
        Assert.AreEqual(HttpStatusCode.Created, createdResponse.StatusCode, await createdResponse.Content.ReadAsStringAsync(ct));
        var created = await createdResponse.Content.ReadFromJsonAsync<EnterpriseRequestResponse>(ct);
        Assert.IsNotNull(created);
        using var submit = AuthorizedRuntimeRequest(HttpMethod.Post, $"{BasePath}/{created.Id:D}/submit-for-approval", token);
        using var submittedResponse = await client.SendAsync(submit, ct);
        Assert.AreEqual(HttpStatusCode.OK, submittedResponse.StatusCode, await submittedResponse.Content.ReadAsStringAsync(ct));
        var submitted = await submittedResponse.Content.ReadFromJsonAsync<EnterpriseRequestResponse>(ct);
        Assert.IsNotNull(submitted);

        await using DbConnection connection = factory.Provider == DatabaseProvider.SqlServer
            ? new SqlConnection(factory.ConnectionString) : ReviewFixMigrationRecoverySupport.MySqlConnection(factory.ConnectionString);
        await connection.OpenAsync(ct);
        var binding = await connection.QuerySingleAsync<ApprovalRecoveryProbe>("""
            SELECT Id, TenantId, RequestId, WorkflowInstanceId, SubmittedById, RequestVersion
            FROM demo_enterprise_request_approval_submission WHERE TenantId = @TenantId AND RequestId = @Id
            """, new { submitted.TenantId, submitted.Id });
        var startup = await connection.QuerySingleAsync<ApprovalOutboxProbe>("""
            SELECT Id, TenantId, Payload, OccurredAtUtc FROM fn_outbox_message
            WHERE TenantId = @TenantId AND MessageType = @MessageType
            """, new { submitted.TenantId, MessageType = EnterpriseRequestApprovalSubmittedIntegrationEvent.EventType });
        await using var scope = factory.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var tenant = services.GetRequiredService<ICurrentTenantContextWriter>();
        tenant.SetHost();
        var startupHandler = (IIntegrationEventHandler)ActivatorUtilities.CreateInstance(services,
            typeof(EnterpriseRequestModule).Assembly.GetType(
                "Full.NET.Modules.EnterpriseRequest.Features.SubmitForApproval.EnterpriseRequestApprovalSubmittedHandler", true)!);
        var startupContext = new IntegrationEventContext(startup.Id, startupHandler.EventType, 1,
            startup.TenantId, null, startup.OccurredAtUtc);
        await startupHandler.HandleAsync(startupContext, startup.Payload, ct);
        Assert.IsTrue(tenant.IsHost);
        var started = await ReadProgress(client, token, submitted.Id, ct);
        Assert.AreEqual(EnterpriseRequestApprovalDeliveryState.Started, started.DeliveryState);
        Assert.IsNotNull(started.StartedAtUtc);
        Assert.AreEqual(binding.WorkflowInstanceId, started.WorkflowInstanceId);

        // 在独占库补齐崩溃后残留的未决席位及已提交投票，取消不得抹掉既有投票事实。
        var work = await connection.QuerySingleAsync<RuntimeFailureWorkProbe>(
            "SELECT Id, StepId FROM fn_workflow_todo WHERE InstanceId = @Id AND StatusKey = 'active'",
            new { Id = binding.WorkflowInstanceId });
        var decidedTodoId = Guid.CreateVersion7();
        var decidedUserId = Guid.CreateVersion7();
        var voteTime = DateTimeOffset.UtcNow;
        await connection.ExecuteAsync("""
            INSERT INTO fn_workflow_todo
            (Id, InstanceId, StepId, AssigneeUserId, StatusKey, Revision, ArrivedAtUtc, CompletedAtUtc, ResultActionKey, ReminderCount)
            SELECT @Id, InstanceId, StepId, @UserId, 'completed', 1, ArrivedAtUtc, @Now, 'approve', 0
            FROM fn_workflow_todo WHERE Id = @OriginalId
            """, new { Id = decidedTodoId, UserId = decidedUserId, Now = voteTime, OriginalId = work.Id });
        await connection.ExecuteAsync("""
            INSERT INTO fn_workflow_approval_slot
            (Id, InstanceId, StepId, TodoId, AssigneeUserId, DecisionKey, Revision, CreatedAtUtc, DecidedAtUtc)
            SELECT @Id, @InstanceId, @StepId, @TodoId, @UserId, NULL, 1, @Now, NULL
            WHERE NOT EXISTS (SELECT 1 FROM fn_workflow_approval_slot WHERE TodoId = @TodoId)
            """, new { Id = Guid.CreateVersion7(), InstanceId = binding.WorkflowInstanceId, work.StepId,
                TodoId = work.Id, UserId = binding.SubmittedById, Now = voteTime });
        await connection.ExecuteAsync("""
            INSERT INTO fn_workflow_approval_slot
            (Id, InstanceId, StepId, TodoId, AssigneeUserId, DecisionKey, Revision, CreatedAtUtc, DecidedAtUtc)
            VALUES (@Id, @InstanceId, @StepId, @TodoId, @UserId, 'approve', 1, @Now, @Now)
            """, new { Id = Guid.CreateVersion7(), InstanceId = binding.WorkflowInstanceId, work.StepId,
                TodoId = decidedTodoId, UserId = decidedUserId, Now = voteTime });
        Assert.AreEqual(1, await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM fn_workflow_approval_slot WHERE InstanceId = @Id AND DecisionKey IS NULL",
            new { Id = binding.WorkflowInstanceId }));
        // 仅修改本用例独占数据库中的故障条件；暂停状态必须由实际 Worker 事务产生。
        Assert.AreEqual(1, await connection.ExecuteAsync("""
            UPDATE fn_workflow_todo SET StatusKey = 'completed', CompletedAtUtc = @Now, ResultActionKey = 'system'
            WHERE InstanceId = @Id AND StatusKey = 'active'
            """, new { Id = binding.WorkflowInstanceId, Now = DateTimeOffset.UtcNow }));
        Assert.AreEqual(1, await connection.ExecuteAsync("""
            UPDATE fn_workflow_instance SET LeaseOwnerKey = 'abandoned-worker', LeaseExpiresAtUtc = @Expired
            WHERE Id = @Id AND StatusKey = 'active'
            """, new { Id = binding.WorkflowInstanceId, Expired = DateTimeOffset.UtcNow.AddMinutes(-1) }));
        using var worker = await IntegrationWorkerHostFactory.BuildAsync(factory.Provider, factory.ConnectionString,
            new Dictionary<string, string?>
            {
                ["Workflow:RecoveryWorker:BatchSize"] = "10", ["Workflow:RecoveryWorker:PollMilliseconds"] = "100",
                ["Workflow:RecoveryWorker:MaxAttempts"] = "1", ["Workflow:RecoveryWorker:RetryDelaySeconds"] = "1",
                ["Workflow:RecoveryWorker:RetryBackoffMode"] = "fixed", ["Workflow:RecoveryWorker:RetryMaxDelaySeconds"] = "1",
            }, "Full.NET.IntegrationTests.EnterpriseRuntimeFailure.Worker", "WorkflowRecoveryHostedProcessor");
        await worker.StartAsync(ct);
        try
        {
            var deadline = DateTimeOffset.UtcNow.AddSeconds(20);
            while (DateTimeOffset.UtcNow < deadline)
            {
                if (await connection.ExecuteScalarAsync<string>("SELECT StatusKey FROM fn_workflow_instance WHERE Id = @Id",
                        new { Id = binding.WorkflowInstanceId }) == "suspended"
                    && await connection.ExecuteScalarAsync<int>("""
                        SELECT COUNT(*) FROM fn_workflow_recovery_task WHERE InstanceId = @Id AND StatusKey = 'dead_lettered'
                        """, new { Id = binding.WorkflowInstanceId }) > 0) break;
                await Task.Delay(100, ct);
            }
        }
        finally { await worker.StopAsync(ct); }
        Assert.AreEqual("suspended", await connection.ExecuteScalarAsync<string>(
            "SELECT StatusKey FROM fn_workflow_instance WHERE Id = @Id", new { Id = binding.WorkflowInstanceId }));
        Assert.IsTrue(await connection.ExecuteScalarAsync<int>("""
            SELECT COUNT(*) FROM fn_workflow_recovery_task WHERE InstanceId = @Id AND StatusKey = 'dead_lettered' AND AttemptCount = 1
            """, new { Id = binding.WorkflowInstanceId }) > 0);
        await AssertUnfinalized();
        await startupHandler.HandleAsync(startupContext, startup.Payload, ct);
        await AssertUnfinalized();

        var revision = await connection.ExecuteScalarAsync<long>("SELECT Revision FROM fn_workflow_instance WHERE Id = @Id",
            new { Id = binding.WorkflowInstanceId });
        using (var recover = AuthorizedRuntimeRequest(HttpMethod.Post, $"/api/v1/workflow/instances/{binding.WorkflowInstanceId:D}/recover",
                   token, new { expectedRevision = revision, reason = "缺少活动待办时禁止假恢复", idempotencyKey = "runtime-missing-todo" }))
        using (var response = await client.SendAsync(recover, ct))
        {
            Assert.AreEqual(HttpStatusCode.Conflict, response.StatusCode, await response.Content.ReadAsStringAsync(ct));
            StringAssert.Contains(await response.Content.ReadAsStringAsync(ct), "workflow.todo.not_active");
        }
        await AssertUnfinalized();
        using (var staleCancel = AuthorizedRuntimeRequest(HttpMethod.Post, $"/api/v1/workflow/instances/{binding.WorkflowInstanceId:D}/cancel",
                   token, new { expectedRevision = revision - 1, reason = "旧版本取消", idempotencyKey = "runtime-stale-cancel" }))
        using (var response = await client.SendAsync(staleCancel, ct))
            Assert.AreEqual(HttpStatusCode.Conflict, response.StatusCode, await response.Content.ReadAsStringAsync(ct));
        await AssertUnfinalized();
        // 两次相同公开取消请求及终态重投必须共享同一修订和业务版本，不能生成重复意图。
        for (var attempt = 0; attempt < 2; attempt++)
        {
            using var cancel = AuthorizedRuntimeRequest(HttpMethod.Post, $"/api/v1/workflow/instances/{binding.WorkflowInstanceId:D}/cancel",
                token, new { expectedRevision = revision, reason = "运行中失败后受控取消", idempotencyKey = "runtime-cancel" });
            using var response = await client.SendAsync(cancel, ct);
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync(ct));
        }
        var terminal = await connection.QuerySingleAsync<ApprovalOutboxProbe>("""
            SELECT Id, TenantId, Payload, OccurredAtUtc FROM fn_outbox_message
            WHERE TenantId = @TenantId AND MessageType = @MessageType
            """, new { submitted.TenantId, MessageType = WorkflowNotificationIntegrationEventTypes.InstanceCancelled });
        // 业务终态消费者只在 Worker 注册；从真实 Worker 作用域取得完整扇出，避免 API 夹具遗漏回写。
        await using var terminalScope = worker.Services.CreateAsyncScope();
        var terminalServices = terminalScope.ServiceProvider;
        var terminalTenant = terminalServices.GetRequiredService<ICurrentTenantContextWriter>();
        terminalTenant.SetHost();
        var terminalSinks = terminalServices.GetServices<IWorkflowInstanceCancelledSink>().ToArray();
        Assert.AreEqual(1, terminalSinks.Count(sink => sink.GetType().Assembly == typeof(EnterpriseRequestModule).Assembly));
        Assert.AreEqual(1, terminalSinks.Count(sink => sink.GetType().Assembly == typeof(NotificationsModule).Assembly));
        var terminalHandler = terminalServices.GetServices<IIntegrationEventHandler>()
            .Single(handler => handler.EventType == WorkflowNotificationIntegrationEventTypes.InstanceCancelled);
        var terminalContext = new IntegrationEventContext(terminal.Id, terminalHandler.EventType, 1,
            terminal.TenantId, null, terminal.OccurredAtUtc);
        for (var attempt = 0; attempt < 3; attempt++) await terminalHandler.HandleAsync(terminalContext, terminal.Payload, ct);
        Assert.IsTrue(terminalTenant.IsHost);
        var finalized = await ReadProgress(client, token, submitted.Id, ct);
        Assert.AreEqual(EnterpriseRequestApprovalDeliveryState.Finalized, finalized.DeliveryState);
        Assert.AreEqual(EnterpriseRequestStatusKeys.Cancelled, finalized.RequestStatus);
        Assert.AreEqual(submitted.Version + 1, finalized.RequestVersion);
        Assert.AreEqual(started.StartedAtUtc, finalized.StartedAtUtc);
        Assert.AreEqual(binding.WorkflowInstanceId, finalized.WorkflowInstanceId);
        Assert.AreEqual(submitted.Version, finalized.SubmittedVersion);
        Assert.IsNotNull(finalized.CompletedAtUtc);
        Assert.IsNotNull(finalized.FinalNotification);
        Assert.AreEqual(revision + 1, await connection.ExecuteScalarAsync<long>(
            "SELECT Revision FROM fn_workflow_instance WHERE Id = @Id", new { Id = binding.WorkflowInstanceId }));
        Assert.AreEqual(0, await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM fn_workflow_approval_slot WHERE InstanceId = @Id AND DecisionKey IS NULL",
            new { Id = binding.WorkflowInstanceId }));
        Assert.AreEqual(1, await connection.ExecuteScalarAsync<int>("""
            SELECT COUNT(*) FROM fn_workflow_approval_slot WHERE InstanceId = @Id
            AND DecisionKey = 'cancelled' AND Revision = 2 AND DecidedAtUtc IS NOT NULL
            """, new { Id = binding.WorkflowInstanceId }));
        Assert.AreEqual(1, await connection.ExecuteScalarAsync<int>("""
            SELECT COUNT(*) FROM fn_workflow_approval_slot WHERE InstanceId = @Id
            AND DecisionKey = 'approve' AND Revision = 1 AND DecidedAtUtc = @VoteTime
            """, new { Id = binding.WorkflowInstanceId, VoteTime = voteTime }));
        Assert.AreEqual("suspended", await connection.ExecuteScalarAsync<string>("""
            SELECT FromStatusKey FROM fn_workflow_execution_log
            WHERE InstanceId = @Id AND TransitionKey = 'instance.cancel'
            """, new { Id = binding.WorkflowInstanceId }));
        var notificationKey = $"workflow-{terminal.Id:N}";
        var tenantScopeKey = $"tenant:{submitted.TenantId:N}";
        Assert.AreEqual(1, await connection.ExecuteScalarAsync<int>("""
            SELECT COUNT(*) FROM fn_notifications_intent WHERE TenantScopeKey = @tenantScopeKey
            AND ProducerKey = 'workflow' AND IdempotencyKey = @notificationKey
            """, new { tenantScopeKey, notificationKey }));
        Assert.AreEqual(1, await connection.ExecuteScalarAsync<int>("""
            SELECT COUNT(*) FROM fn_notifications_inbox_message m JOIN fn_notifications_intent i
            ON i.Id = m.IntentId AND i.TenantScopeKey = m.TenantScopeKey
            WHERE i.TenantScopeKey = @tenantScopeKey AND i.ProducerKey = 'workflow' AND i.IdempotencyKey = @notificationKey
            AND m.RecipientUserId = @RecipientId
            """, new { tenantScopeKey, notificationKey, RecipientId = binding.SubmittedById }));

        // 复用同一个独占库和 Worker 服务提供程序，补齐有效待办恢复路径而不增加建库和容器启动。
        await VerifyBoundPausedRecoveryAndApprovalAsync(client, token, organizationUnitId, connection, worker.Services, ct);

        async Task AssertUnfinalized()
        {
            var progress = await ReadProgress(client, token, submitted.Id, ct);
            Assert.AreEqual(EnterpriseRequestStatusKeys.Submitted, progress.RequestStatus);
            Assert.AreEqual(submitted.Version, progress.RequestVersion);
            Assert.AreEqual(EnterpriseRequestApprovalDeliveryState.Started, progress.DeliveryState);
            Assert.AreEqual(started.StartedAtUtc, progress.StartedAtUtc);
            Assert.AreEqual(started.SubmittedAtUtc, progress.SubmittedAtUtc);
            Assert.AreEqual(submitted.Version, progress.SubmittedVersion);
            Assert.AreEqual(binding.WorkflowInstanceId, progress.WorkflowInstanceId);
            Assert.IsNull(progress.CompletedAtUtc);
            Assert.IsNull(progress.FinalNotification);
            Assert.AreEqual("suspended", await connection.ExecuteScalarAsync<string>(
                "SELECT StatusKey FROM fn_workflow_instance WHERE Id = @Id", new { Id = binding.WorkflowInstanceId }));
            Assert.AreEqual(1, await connection.ExecuteScalarAsync<int>("""
                SELECT COUNT(*) FROM fn_workflow_instance WHERE TenantId = @TenantId
                AND BusinessType = @BusinessType AND BusinessId = @BusinessId
                """, new { submitted.TenantId, EnterpriseRequestWorkflowConstants.BusinessType, BusinessId = submitted.Id.ToString("D") }));
            Assert.AreEqual(0, await connection.ExecuteScalarAsync<int>("""
                SELECT COUNT(*) FROM fn_outbox_message WHERE TenantId = @TenantId AND MessageType IN @MessageTypes
                """, new { submitted.TenantId, MessageTypes = new[] { WorkflowNotificationIntegrationEventTypes.InstanceCancelled,
                    WorkflowNotificationIntegrationEventTypes.InstanceCompleted, WorkflowNotificationIntegrationEventTypes.InstanceRejected } }));
        }
    }

    private sealed record RuntimeFailureWorkProbe(Guid Id, Guid StepId);

    /// <summary>所有运行故障验收的公开操作均携带真实租户会话；故障夹具不替代接口授权。</summary>
    private static HttpRequestMessage AuthorizedRuntimeRequest(HttpMethod method, string url, string token, object? body = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null) request.Content = JsonContent.Create(body);
        return request;
    }
}
