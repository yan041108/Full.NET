using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using Dapper;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Modules.EnterpriseRequest;
using Full.NET.Modules.EnterpriseRequest.Contracts;
using Full.NET.Modules.EnterpriseRequest.Generated;
using Full.NET.Modules.Notifications;
using Full.NET.Modules.Organization.Contracts;
using Full.NET.Modules.Workflow.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Full.NET.IntegrationTests.EnterpriseRequest;

internal static partial class EnterpriseRequestAssertions
{
    /// <summary>复用故障验收的独占数据库，以公开暂停和恢复接口验证原待办可继续审批及终态幂等。</summary>
    private static async Task VerifyBoundPausedRecoveryAndApprovalAsync(HttpClient client, string token,
        Guid organizationUnitId, DbConnection connection, IServiceProvider workerServices, CancellationToken ct)
    {
        var oldMessages = (await connection.QueryAsync<Guid>("SELECT Id FROM fn_outbox_message")).ToHashSet();
        using var create = AuthorizedRuntimeRequest(HttpMethod.Post, BasePath, token, new
        {
            requestNumber = $"REC-{Guid.NewGuid():N}"[..16], title = "暂停恢复后继续审批",
            status = EnterpriseRequestStatusKeys.Draft, totalAmount = 84m, applicantUserId = Guid.NewGuid(),
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
        var binding = await connection.QuerySingleAsync<ApprovalRecoveryProbe>("""
            SELECT Id, TenantId, RequestId, WorkflowInstanceId, SubmittedById, RequestVersion
            FROM demo_enterprise_request_approval_submission WHERE TenantId = @TenantId AND RequestId = @Id
            """, new { submitted.TenantId, submitted.Id });
        var startup = (await connection.QueryAsync<ApprovalOutboxProbe>("""
            SELECT Id, TenantId, Payload, OccurredAtUtc FROM fn_outbox_message
            WHERE TenantId = @TenantId AND MessageType = @MessageType
            """, new { submitted.TenantId, MessageType = EnterpriseRequestApprovalSubmittedIntegrationEvent.EventType }))
            .Single(message => !oldMessages.Contains(message.Id));
        await using var scope = workerServices.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var tenant = services.GetRequiredService<ICurrentTenantContextWriter>();
        tenant.SetHost();
        var handlers = services.GetServices<IIntegrationEventHandler>().ToArray();
        var startupHandler = handlers.Single(handler => handler.EventType == EnterpriseRequestApprovalSubmittedIntegrationEvent.EventType);
        var startupContext = new IntegrationEventContext(startup.Id, startupHandler.EventType, 1,
            startup.TenantId, null, startup.OccurredAtUtc);
        await startupHandler.HandleAsync(startupContext, startup.Payload, ct);
        Assert.IsTrue(tenant.IsHost);
        var started = await ReadProgress(client, token, submitted.Id, ct);
        Assert.AreEqual(EnterpriseRequestApprovalDeliveryState.Started, started.DeliveryState);
        var work = await connection.QuerySingleAsync<RuntimeFailureWorkProbe>(
            "SELECT Id, StepId FROM fn_workflow_todo WHERE InstanceId = @Id AND StatusKey = 'active'",
            new { Id = binding.WorkflowInstanceId });

        await Lifecycle("pause", 1, "bound-pause", HttpStatusCode.OK);
        await AssertPending("suspended", 2);
        await Decide("bound-paused-approve", HttpStatusCode.Conflict);
        await AssertPending("suspended", 2);
        await Lifecycle("recover", 1, "bound-stale-recover", HttpStatusCode.Conflict);
        await AssertPending("suspended", 2);
        for (var attempt = 0; attempt < 2; attempt++) await Lifecycle("recover", 2, "bound-recover", HttpStatusCode.OK);
        await startupHandler.HandleAsync(startupContext, startup.Payload, ct);
        await AssertPending("active", 3);
        Assert.IsTrue(tenant.IsHost);
        Assert.AreEqual(1, await connection.ExecuteScalarAsync<int>("""
            SELECT COUNT(*) FROM fn_workflow_execution_log WHERE InstanceId = @Id AND TransitionKey = 'instance.recover'
            """, new { Id = binding.WorkflowInstanceId }));
        // 恢复保留待办修订号；审批重放必须保持已完成实例和业务版本不变。
        for (var attempt = 0; attempt < 2; attempt++) await Decide("bound-recovered-approve", HttpStatusCode.OK);
        var completed = await connection.QuerySingleAsync<ApprovalOutboxProbe>("""
            SELECT Id, TenantId, Payload, OccurredAtUtc FROM fn_outbox_message
            WHERE TenantId = @TenantId AND MessageType = @MessageType
            """, new { submitted.TenantId, MessageType = WorkflowNotificationIntegrationEventTypes.InstanceCompleted });
        var sinks = services.GetServices<IWorkflowInstanceCompletedSink>().ToArray();
        Assert.AreEqual(1, sinks.Count(sink => sink.GetType().Assembly == typeof(EnterpriseRequestModule).Assembly));
        Assert.AreEqual(1, sinks.Count(sink => sink.GetType().Assembly == typeof(NotificationsModule).Assembly));
        var completedHandler = handlers.Single(handler => handler.EventType == WorkflowNotificationIntegrationEventTypes.InstanceCompleted);
        var completedContext = new IntegrationEventContext(completed.Id, completedHandler.EventType, 1,
            completed.TenantId, null, completed.OccurredAtUtc);
        for (var attempt = 0; attempt < 3; attempt++) await completedHandler.HandleAsync(completedContext, completed.Payload, ct);
        await startupHandler.HandleAsync(startupContext, startup.Payload, ct);
        Assert.IsTrue(tenant.IsHost);
        var final = await ReadProgress(client, token, submitted.Id, ct);
        Assert.AreEqual(EnterpriseRequestApprovalDeliveryState.Finalized, final.DeliveryState);
        Assert.AreEqual(EnterpriseRequestStatusKeys.Approved, final.RequestStatus);
        Assert.AreEqual(submitted.Version + 1, final.RequestVersion);
        Assert.AreEqual(submitted.Version, final.SubmittedVersion);
        Assert.AreEqual(binding.WorkflowInstanceId, final.WorkflowInstanceId);
        Assert.AreEqual(started.StartedAtUtc, final.StartedAtUtc);
        Assert.AreEqual(started.SubmittedAtUtc, final.SubmittedAtUtc);
        Assert.IsNotNull(final.CompletedAtUtc);
        Assert.IsNotNull(final.FinalNotification);
        Assert.AreEqual("completed", await connection.ExecuteScalarAsync<string>(
            "SELECT StatusKey FROM fn_workflow_instance WHERE Id = @Id", new { Id = binding.WorkflowInstanceId }));
        Assert.AreEqual(4L, await connection.ExecuteScalarAsync<long>(
            "SELECT Revision FROM fn_workflow_instance WHERE Id = @Id", new { Id = binding.WorkflowInstanceId }));
        Assert.AreEqual(1, await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM fn_workflow_todo WHERE InstanceId = @Id", new { Id = binding.WorkflowInstanceId }));
        var notificationKey = $"workflow-{completed.Id:N}";
        var tenantScopeKey = $"tenant:{submitted.TenantId:N}";
        Assert.AreEqual(1, await connection.ExecuteScalarAsync<int>("""
            SELECT COUNT(*) FROM fn_notifications_intent WHERE TenantScopeKey = @tenantScopeKey
            AND ProducerKey = 'workflow' AND IdempotencyKey = @notificationKey
            """, new { tenantScopeKey, notificationKey }));
        Assert.AreEqual(1, await connection.ExecuteScalarAsync<int>("""
            SELECT COUNT(*) FROM fn_notifications_inbox_message m JOIN fn_notifications_intent i
            ON i.Id = m.IntentId AND i.TenantScopeKey = m.TenantScopeKey
            WHERE i.TenantScopeKey = @tenantScopeKey AND i.ProducerKey = 'workflow'
            AND i.IdempotencyKey = @notificationKey AND m.RecipientUserId = @RecipientId
            """, new { tenantScopeKey, notificationKey, RecipientId = binding.SubmittedById }));

        async Task AssertPending(string status, long revision)
        {
            var progress = await ReadProgress(client, token, submitted.Id, ct);
            Assert.AreEqual(EnterpriseRequestStatusKeys.Submitted, progress.RequestStatus);
            Assert.AreEqual(submitted.Version, progress.RequestVersion);
            Assert.AreEqual(EnterpriseRequestApprovalDeliveryState.Started, progress.DeliveryState);
            Assert.AreEqual(binding.WorkflowInstanceId, progress.WorkflowInstanceId);
            Assert.AreEqual(started.StartedAtUtc, progress.StartedAtUtc);
            Assert.AreEqual(started.SubmittedAtUtc, progress.SubmittedAtUtc);
            Assert.AreEqual(submitted.Version, progress.SubmittedVersion);
            Assert.IsNull(progress.CompletedAtUtc);
            Assert.IsNull(progress.FinalNotification);
            Assert.AreEqual(status, await connection.ExecuteScalarAsync<string>(
                "SELECT StatusKey FROM fn_workflow_instance WHERE Id = @Id", new { Id = binding.WorkflowInstanceId }));
            Assert.AreEqual(revision, await connection.ExecuteScalarAsync<long>(
                "SELECT Revision FROM fn_workflow_instance WHERE Id = @Id", new { Id = binding.WorkflowInstanceId }));
            Assert.AreEqual(work.Id, await connection.ExecuteScalarAsync<Guid>(
                "SELECT Id FROM fn_workflow_todo WHERE InstanceId = @Id AND StatusKey = 'active'",
                new { Id = binding.WorkflowInstanceId }));
            Assert.AreEqual(1, await connection.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM fn_workflow_instance WHERE TenantId = @TenantId AND BusinessId = @BusinessId",
                new { submitted.TenantId, BusinessId = submitted.Id.ToString("D") }));
        }

        async Task Lifecycle(string action, long revision, string key, HttpStatusCode expected)
        {
            using var request = AuthorizedRuntimeRequest(HttpMethod.Post, $"/api/v1/workflow/instances/{binding.WorkflowInstanceId:D}/{action}",
                token, new { expectedRevision = revision, reason = "绑定申请恢复验收", idempotencyKey = key });
            using var response = await client.SendAsync(request, ct);
            Assert.AreEqual(expected, response.StatusCode, await response.Content.ReadAsStringAsync(ct));
        }

        async Task Decide(string key, HttpStatusCode expected)
        {
            using var request = AuthorizedRuntimeRequest(HttpMethod.Post, $"/api/v1/workflow/todos/{work.Id:D}/approve", token,
                new { expectedRevision = 1, fieldPatch = new { }, comment = "恢复后审批", idempotencyKey = key });
            using var response = await client.SendAsync(request, ct);
            Assert.AreEqual(expected, response.StatusCode, await response.Content.ReadAsStringAsync(ct));
        }
    }
}
